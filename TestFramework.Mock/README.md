# TestFramework.Mock

`TestFramework.Mock` is an extension package for `TestFramework.Core`.

It hosts the system under test in-process — your application's own service composition — with chosen
dependencies replaced by declared test doubles, and drives it from a timeline. What the doubles record
becomes run artifacts through Core's ordinary finder verb, and the frozen run says which doubles it ran
against.

The public entry points are `MockDefinition<TService>` (a Mock-Pack), `MockEnvironment`, `MockExt.Host`
and `MockArtifactFinder`.

## Install

```bash
dotnet add package TestFramework.Mock
```

## Quick Start

A Mock-Pack declares one double, once:

```csharp
using System.Threading.Tasks;
using TestFramework.Mock;

public sealed class MailSenderPack : MockDefinition<IMailSender>
{
    protected override void Configure(MockBuilder<IMailSender> mock)
    {
        mock.Call(m => m.SendAsync(MockArg.Any<string>(), MockArg.Any<string>()))
            .Returns(Task.FromResult(true))
            .ProducesArtifact((string to, string subject) => new("sentMail", $"{to}: {subject}"));
    }
}
```

The environment takes the registrations production makes and puts the pack's double where the real
dependency was registered. The timeline calls into the hosted service and finds what the double recorded:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TestFramework.Core.Timelines;
using TestFramework.Mock;
using TestFramework.Mock.Artifacts;

MockEnvironment environment = MockEnvironment.For(services =>
{
    services.AddSingleton<IMailSender, SmtpMailSender>();
    services.AddSingleton<SignupService>();
}).Include<MailSenderPack>();

Timeline timeline = Timeline.Create()
    .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Name("register")
    .FindArtifact("sentMail", new MockArtifactFinder("sentMail"))
    .Build();

TimelineRun run = await timeline.SetupRun().SetEnv(environment).RunAsync();

run.EnsureRanToCompletion();
bool registered = run.MockResult<bool>("register");
int sent = run.Mock<IMailSender>().CountCalls(m => m.SendAsync("ada@example.com", MockArg.Any<string>()));
object? mail = run.ArtifactStore.GetMockArtifact("sentMail").Last.Payload;   // "ada@example.com: Welcome"
```

`SmtpMailSender` is never constructed: the environment removes every registration of `IMailSender`
before it adds the double.

## The Model

Mock is an environment like any other. Three pieces, each with one job:

| Piece | What it is | Lifetime |
|---|---|---|
| **Mock-Pack** — `MockDefinition<TService>` | one immutable declaration of a double: which calls it answers and how | declared once, instantiated per run |
| **`MockEnvironment`** | hosts your service composition with each included pack's double standing in | one host per run, frozen and disposed with it |
| **Timeline verbs** — `MockExt.Host`, `FindArtifact` | call the hosted system under test; bring what a double recorded into the run | steps of the timeline |

A double never writes into the run. It records what a call left behind in its environment — the way a
real dependency would leave a file or a row — and a timeline step brings that into the run with Core's
`FindArtifact`, exactly as it would find a file another program wrote. After the run, the doubles are
frozen: their call logs are a snapshot, and a late call is refused by name.

## Writing A Mock-Pack

A setup names one call on the service. Arguments are matched by value, or by `MockArg.Any<T>()`:

```csharp
mock.Call(f => f.ReadText("config.json")).Returns("{}");
mock.Call(f => f.ReadText(MockArg.Any<string>())).Throws(new FileNotFoundException());
```

The second setup here would overlap the first for `"config.json"` — and a call that more than one setup
matches is refused naming them all. There is no precedence rule; narrow the matchers instead.

- `MockArg.Any<T>()` matches any value of `T`, and nothing of another type: on an `object` parameter,
  `MockArg.Any<string>()` does not answer an `int`. It stands only as a whole argument — inside a larger
  expression such as `MockArg.Any<int>() + 1` it is refused, because it would be read once as a fixed value.
- An exact value is evaluated once, when the pack is instantiated. Arrays and lists compare by their
  elements in order; everything else compares with `Equals`.

| Verb | Meaning |
|---|---|
| `Returns(value)` | every matching call returns this value |
| `Returns((a, b) => ...)` | the result computed from the call's typed arguments |
| `Throws(exception)` | every matching call throws it |
| `Callback((a, b) => ...)` | a body for a void call; a void setup with no body simply does nothing |
| `Compute((a, b, artifacts) => ...)` | the primitive: the result plus `artifacts.Publish(identity, payload)` by hand |
| `ProducesArtifact((a, b) => new(identity, payload))` | records a payload when the call completes; stackable |

Typed lambdas exist for one to eight arguments. Their parameter types are checked against the mocked
method when the pack is instantiated, so a mistyped lambda fails there, naming the method's signature —
not at the first call.

The rules a pack is held to:

- **Strict only.** A call no setup matches is recorded, then refused naming the call and listing the
  setups that exist.
- **A value is never invented.** A setup for a method that returns something must state `Returns`,
  `Compute` or `Throws`, and states it once.
- **A declared artifact is what a completed call produced.** `ProducesArtifact` runs after the result,
  so a call that throws never publishes it. What a `Compute` body published by hand before throwing
  stands. `Throws` together with `ProducesArtifact` on one setup is refused as unreachable.
- **Interfaces only.** The service must be an interface; class mocking is not supported.
- **Declared once.** Every setup is sealed when the double is built. A setup object or builder kept past
  `Configure` refuses further changes, so nothing can alter a double that is already answering calls.
- **A fresh pack per run.** The environment creates a new pack object for every run, so a field on a pack
  never carries one run's state into the next.

## Hosting The System Under Test

```csharp
MockEnvironment environment = MockEnvironment.For(services => services.AddMyApplication())
    .Include<MailSenderPack>()
    .Include<ClockPack>();
```

`For` takes the same registrations production makes, handed over rather than rebuilt, so the test hosts
what actually ships. For each included pack the environment removes every registration of the service —
the plain one, every keyed one, and so everything an `IEnumerable<TService>` would resolve — and puts the
run's double in each of those places, so nothing reaches the real dependency whichever way it asks.

- Including the same pack twice is allowed and has no effect. Two packs for the same service are refused
  naming both.
- The environment seals when the first run uses it: including another pack after that is refused, so every
  run hosts the same declaration.
- Every run gets its own host, doubles and call logs, so parallel runs never share state.
- The finished run records which pack stood where: `run.EffectiveSettings` holds kind `mock.host`, key =
  the service's full name — with `[key]` appended for each keyed registration it replaced — and value =
  the pack's type name.
- At the end of the run the provider is disposed first and the doubles freeze after, so what the system
  under test does while being disposed — a flush, stopping a timer — still reaches its doubles.
- A run hosts one composition. Set the environment with `SetupRun(...).SetEnv(environment)`.

## Calling The System Under Test

`MockExt.Host` resolves the service from the run's host and calls it. The hosted service arrives behind
the call's own arguments, which can be bound from run variables:

```csharp
Timeline timeline = Timeline.Create()
    .Trigger(MockExt.Host(
        Var.Ref<string>("email"),
        (string email, SignupService signup) => signup.RegisterAsync(email))).Name("register")
    .Build();

TimelineRun run = await timeline.SetupRun()
    .AddVariable("email", "grace@example.com")
    .SetEnv(environment)
    .RunAsync();
```

- A call that returns `Task` or `Task<T>` is awaited: the step finishes when the call does, and its
  result is what the task yielded. `async` lambdas work the same way.
- Any other awaitable — a `ValueTask`, a task of a task — is refused when the timeline is built, because
  it would not be awaited. Call `.AsTask()` on a `ValueTask`.
- `run.MockResult<T>(label)` reads what a labelled `Host` step returned.

## Artifacts From Calls

`ProducesArtifact` and `Publish` record a payload under an identity in the environment. A later publish of
the same identity replaces it, as a second write to a file does. Nothing enters the run until a step
looks:

```csharp
.FindArtifact("sentMail", new MockArtifactFinder("sentMail"))
```

Read it with `run.ArtifactStore.GetMockArtifact("sentMail")`. If nothing has published the identity yet,
the finder finds nothing and logs a warning, like any empty finder.

### Taking A Later Look

A version is a look the timeline took, with Core's own verb:

```csharp
Timeline timeline = Timeline.Create()
    .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Name("first")
    .FindArtifact("sentMail", new MockArtifactFinder("sentMail"))
    .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("grace@example.com"))).Name("second")
    .CaptureArtifactVersion("sentMail")
    .Build();
```

The artifact now has two versions: the first mail and the second. Two publishes between two looks
produce one version — the later payload. Every individual call stays in the double's call log.

Finding reads; it consumes nothing. A retried step, or a second look with nothing new, sees what the
environment holds at that moment. Two doubles publishing the same identity are refused naming both
services — give each its own identity.

## Verifying Calls

```csharp
MockInstance<IMailSender> mail = run.Mock<IMailSender>();

int count = mail.CountCalls(m => m.SendAsync(MockArg.Any<string>(), "Welcome"));
IReadOnlyList<RecordedCall> calls = mail.RecordedCalls;   // method, arguments, matched, sequence
```

The call log holds every call in order, including the unmatched and ambiguous ones that were refused —
the call nothing expected is usually the interesting one. It is frozen with the run.

## Troubleshooting

**`No setup matches 'Method(...)'`** — the system under test made a call the pack does not declare. The
message lists the setups that do exist; add one, or widen a matcher.

**`'Method(...)' matches more than one setup`** — two setups overlap for this call. Narrow them so exactly
one answers.

**`'X' is not registered in the hosted services`** — `Host` asked for a service the composition handed to
`MockEnvironment.For(...)` does not register.

**`This run has no mock host`** — the run was started without `SetEnv(MockEnvironment...)`.

**`The hosted call yields ValueTask<...>`** — return a `Task` so `Host` can await it.

**`The run is finished; the mock no longer accepts calls`** — something in the system under test kept
calling after the run ended, usually background work the test started. Make stopping it a step of the
timeline.

The full list, with when each one fires, is in
[Documentation/ERROR-HANDLING-MOCK.md](https://github.com/DeadMoon0/TestFramework-Mock/blob/main/Documentation/ERROR-HANDLING-MOCK.md).

## Current Limits

- Interfaces only, and methods only — properties and events cannot be set up yet.
- Matchers are exact values and `MockArg.Any<T>()`; there is no predicate matcher and no call sequences.
- `Host` does not hand the step's cancellation token to the call, and has no overload for a synchronous
  `void` method.
- Recorded arguments and payloads are held by reference: an object the system under test changes after
  the call changes the record too. Publish a copy when that matters.

## Target Frameworks

- .NET 8 (`net8.0`)
- .NET 10 (`net10.0`)
