# TestFramework-Mock - arc42 Architecture Documentation

> Date: 2026-09-25

## 1. Introduction and Goals

TestFramework-Mock is the in-process extension of the TestFramework ecosystem. It hosts the system under
test from the application's own service composition, stands declared test doubles in for chosen
dependencies, and lets a timeline call into it and inspect what happened.

Primary goals:

- test the application's real composition, not a hand-rebuilt copy of it
- declare a test double once, as an immutable Mock-Pack, and reuse it across tests
- keep every run isolated: its own host, its own doubles, its own call logs
- make misuse a stated failure at the earliest point: when the timeline is built, when a pack is
  instantiated, or when the offending call arrives
- stay an ordinary environment, so everything the rest of the framework offers applies unchanged

## 2. Constraints

- Runtime targets are .NET 8 (`net8.0`) and .NET 10 (`net10.0`); the package multi-targets both.
- The package extends `TestFramework.Core` and follows its sockets: environment provider and component,
  steps, the per-run state slot, and the artifact describer, reference and finder.
- It holds one role in the family: an edge pack whose outside field is the system under test's own
  service composition, hosted in-process (see the family's `ARCHITECTURE.md`).
- The proxy engine is Castle DynamicProxy. The package does not depend on another mocking library.
- `Newtonsoft.Json` is the family's JSON library; this package serialises nothing itself and references
  no other.

## 3. System Scope and Context

- `TestFramework.Core`: runs the timeline, owns the run's stores, freezing, retries and timeouts
- test authors: write Mock-Packs, compose a `MockEnvironment`, and drive it with `MockExt.Host`
- the system under test: the author's own classes, resolved from their own registrations
- `Microsoft.Extensions.DependencyInjection`: builds the per-run service provider

Out of scope: hosting an HTTP pipeline in-process (a later bridge package), class mocking, and anything
that leaves the process.

## 4. Solution Strategy

- **Mock is an environment.** `MockEnvironment` is an environment provider with one component, the mock
  host. It is started because `Host` steps require its resource kind, exactly as other packages' steps
  require theirs.
- **A double records; a step reads.** A double is called on a thread of the system under test, inside no
  step, so it never writes into the run. It records in its environment, and a timeline brings a record
  into the run with Core's `FindArtifact` and `CaptureArtifactVersion` — the same verbs used for a file
  another program wrote. Whether something is a new artifact or a new version is therefore Core's, stated
  by where the timeline looks, and never the pack's.
- **Declarations are immutable; instances are per run.** A Mock-Pack is instantiated once per run, so
  recordings are run-isolated and parallel runs share nothing.
- **Refuse rather than guess.** Unmatched calls, ambiguous matches, missing results, unreachable artifacts,
  duplicate packs and ambiguous identities are all stated failures that name what exists.

## 5. Building Block View

- `MockDefinition<TService>` / `MockBuilder<TService>`: the authoring surface of a Mock-Pack
- `MockCallSetup<TService, TResult>` / `MockCallSetup<TService>`: one setup's verbs; the per-argument-count
  overloads are generated from `MockCallSetup.Arities.tt`
- `Arg`, `CallPattern`, `CallPatternParser`: read a `m => m.Method(...)` expression into a matcher per
  argument, evaluating plain values once at declaration time
- `MockInstance<TService>`: one run's live double — the Castle proxy, the call recorder and the artifact
  record; frozen with the run
- `MockInterceptor`: the one interception path — record, match exactly one setup or refuse, answer
- `MockEnvironment`, `MockHostEnvComponent`, `MockHostState`: compose the services, apply the packs,
  record them on the run's effective settings, and hold the provider in the run's state slot
- `MockExt.Host` → `MockHostCallTrigger<TService, TResult>` / `MockHostCallTrigger<TService>`: resolve and
  call the system under test, awaiting tasks; the per-argument-count overloads are generated from
  `MockExt.Arities.tt`
- `MockArtifacts`, `MockArtifactRecord`: the channel a setup publishes through, and the environment's
  record of the latest payload per identity
- `MockArtifactFinder` and the `MockCaptured…` artifact triple: find a recorded identity for Core's
  `FindArtifact`, and re-resolve it for `CaptureArtifactVersion`
- `MockRunExtensions`: reads over the finished run — `Mock<T>()`, `MockResult<T>(label)`,
  `GetMockArtifact(identifier)`

## 6. Runtime View

1. **Environment creation.** Core creates the mock host because a `Host` step requires `mock.host`. The
   component runs the author's composition, instantiates each pack, removes every registration of the
   replaced service, adds the double as a singleton, records `mock.host/<service> = <pack>` on the run's
   effective settings, builds the provider and places the host state in the run's state slot.
2. **A hosted call.** A `Host` step resolves the service from the host and calls it with arguments bound
   from run variables. A returned task is awaited. Calls the system under test makes into a double pass
   through the interceptor: recorded, matched against exactly one setup, answered; declared artifacts are
   recorded after the result.
3. **Finding.** A `FindArtifact` step with a `MockArtifactFinder` asks the host for the identity. If a
   double recorded it, Core adds the artifact under the finding step's own context; `CaptureArtifactVersion`
   re-resolves the latest record into a new version.
4. **Teardown.** The component freezes every double first — from then on calls and publishes are refused
   by name — and then disposes the provider. The finished run still exposes the frozen doubles for
   verification.

## 7. Deployment View

A class library, shipped as one NuGet package, `TestFramework.Mock`, consumed by test projects and
executed inside the test host process. There is no service deployment unit.

## 8. Cross-Cutting Concepts

- **Freezing:** the call recorder and the artifact record freeze with the run, so a finished run is a
  snapshot; the freeze is internal, so no caller can freeze a running double.
- **Honest failures:** every refusal derives from the framework's exception types and carries recovery
  steps and, where there is a set to choose from, the available options.
- **Early validation:** awaitable results `Host` would not await are refused when the timeline is built;
  pack mistakes are refused when the pack is instantiated, before the system under test runs.
- **Generated arities:** one T4 template per verb family, output committed; a reflection test pins that
  every argument count up to eight exists.
- **Family conventions:** the suite runs the shared convention checks — steps clone and freeze, one JSON
  library, no internals granted to anyone but the package's own suite.

## 9. Architecture Decisions

- **Own engine on Castle DynamicProxy rather than wrapping another mocking library.**
  Rationale: freezing, recording and artifact hooks need engine-level control, and the family keeps its
  dependency surface small.
- **Strict only, no precedence between setups.**
  Rationale: a lenient double invents answers, and "last declared wins" makes a test's meaning depend on
  declaration order. Both are guesses the framework refuses to make.
- **Doubles record into the environment; Core's finder brings records into the run.**
  Rationale: a double runs inside no step and cannot write to the run under any attempt's licence. Treated
  as an environment it needs no special rule: the finding step writes under its own context, with Core's
  one add-or-version verb.
- **`Host`, not `Call`, for the timeline verb.**
  Rationale: on a pack, `mock.Call(...)` declares a double; the timeline verb does the opposite and must
  not share the name.

## 10. Quality Requirements

- Run isolation: two runs of one timeline never share a double, a log or a record
- Predictability: a call has exactly one answer or a stated failure
- Early failure: mistakes surface where they were written, not at the first call
- Traceability: the frozen run says which pack stood in for which service
- Ease of use: the common flow is a pack, an environment, `Host` and `FindArtifact`

## 11. Risks and Technical Debt

- Interfaces and methods only: properties, events and classes cannot be mocked yet
- Matchers are exact values and `Arg.Any<T>()`; no predicate matcher, no call sequences
- `Host` does not pass the step's cancellation token to the call, and has no synchronous `void` overload
- Recorded arguments and payloads are held by reference, so later mutation by the system under test
  changes the record

## 12. Glossary

- Mock-Pack: one immutable declaration of a test double, `MockDefinition<TService>`
- Double: one run's live instance of a pack, standing in for the service
- Setup: one call pattern a pack answers, with its result and artifact producers
- Mock host: the per-run service provider the environment builds
- Publish: a double recording a payload under an identity in its environment
- Look: a `FindArtifact` or `CaptureArtifactVersion` step reading that record into the run
