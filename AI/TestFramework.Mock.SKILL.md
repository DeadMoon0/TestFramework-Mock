<identity>
    <package>TestFramework.Mock</package>
    <role>addon-skill</role>
</identity>

<objective>
    Explain how TestFramework.Mock tests the system under test in-process: declaring test doubles as Mock-Packs, hosting the application's own service composition with MockEnvironment, calling into it with MockExt.Host, bringing what doubles recorded into the run with Core's FindArtifact, and verifying calls from the frozen run.
</objective>

<package_scope>
    Covers Mock-Packs (MockDefinition), the setup verbs, the in-process mock host, the Host trigger including async calls, the mock artifact finder, and the post-run reads. Does not cover HTTP hosting, class mocking, or anything outside the test process.
</package_scope>

<key_concepts>
    Mock is an environment like any other: MockEnvironment is an environment provider whose one component hosts the application's services for the length of a run.
    A Mock-Pack (MockDefinition<TService>) is one immutable declaration of a double; it is instantiated per run, so runs never share doubles, call logs or records.
    MockEnvironment.For(services => ...) takes the registrations production makes; Include<TPack>() removes every registration of the pack's service and adds the run's double as a singleton.
    MockExt.Host resolves the system under test from the host and calls it; the hosted service arrives behind the call's own arguments, which can be bound from run variables.
    A double never writes into the run. ProducesArtifact and Publish record a payload under an identity in the environment; a timeline brings it in with FindArtifact(identity, new MockArtifactFinder(identity)) and takes later looks with CaptureArtifactVersion(identity).
    Doubles are strict: an unmatched call and a call two setups match are both refused by name. There is no precedence between setups.
    The finished run is a snapshot: doubles are frozen, and EffectiveSettings records which pack stood in for which service.
</key_concepts>

<best_practices>
    Declare one named MockDefinition class per replaced service and reuse it across tests.
    Hand MockEnvironment.For the same registration code production uses, instead of rebuilding the composition in the test.
    Use MockArg.Any<T>() for arguments the test does not care about and exact values for the ones it does; keep setups from overlapping.
    Return Task or Task<T> from Host lambdas for async services; the step then finishes when the call does.
    Give every published identity a name that says what it is, and one identity per pack.
    Assert on calls with run.Mock<T>().CountCalls(...) and on returned values with run.MockResult<T>(label).
    Label Host steps with .Name(...) so their results can be read after the run.
</best_practices>

<api_hints>
    Important APIs and shapes:
    - class XPack : MockDefinition<IService> { protected override void Configure(MockBuilder<IService> mock) { ... } }
    - mock.Call(s => s.Method(MockArg.Any<string>(), "exact")) for value-returning and void calls
    - setup verbs: Returns(value), Returns((a, b) => ...), Throws(exception), Callback((a, b) => ...), Compute((a, b, artifacts) => ...), ProducesArtifact((a, b) => new(identity, payload))
    - async setup verbs, only on task-returning methods: ReturnsAsync(value), Completes(), ThrowsAsync(exception)
    - artifacts.Publish(identity, payload) inside a Compute body
    - MockEnvironment.For(services => ...).Include<XPack>()
    - timeline.SetupRun(...).SetEnv(environment)
    - MockExt.Host((Service s) => s.Method(...)) and MockExt.Host(Var.Ref<T1>("name"), ..., (T1 a, ..., Service s) => ...), up to eight variable-bound arguments
    - MockExt.Host((Service s, CancellationToken ct) => s.MethodAsync(ct)) hands the call the step's cancellation token
    - MockExt.Host((Service s) => s.VoidMethod()) for synchronous void methods
    - .FindArtifact("identity", new MockArtifactFinder("identity")) and .CaptureArtifactVersion("identity")
    - run.Mock<IService>() -> MockInstance<IService>: CountCalls(...), RecordedCalls
    - run.MockResult<T>("label")
    - run.ArtifactStore.GetMockArtifact("identity") -> versions whose Payload is what was published

    Behavioral hint:
    The canonical flow is a pack, an environment, a Host step, then FindArtifact for anything the doubles recorded.
</api_hints>

<runtime_behavior>
    Important runtime facts:
    - Packs are instantiated when the run's environment starts, so every pack mistake fails the run before its first step.
    - Typed setup lambdas are checked against the mocked method's parameters at instantiation, naming the real signature.
    - A value-returning setup must state Returns, Compute or Throws; a mocked call never invents a return value.
    - ProducesArtifact runs after the result, so a call that throws never publishes it; Throws plus ProducesArtifact on one setup is refused.
    - Host awaits Task and Task<T>; any other awaitable (ValueTask, a task of a task) is refused when the timeline is built.
    - Every Host call runs in its own DI scope: scoped services are fresh per call and disposed when it ends; singletons are shared across the run.
    - For an async call, ProducesArtifact publishes only when the returned task finishes successfully; ThrowsAsync returns a failed task instead of throwing at the call.
    - A later publish of an identity replaces the earlier one in the environment; a version in the run is a look the timeline took, not one per call.
    - A finder whose identity nothing has published yet finds nothing and logs a warning; two doubles publishing one identity are refused naming both.
    - Call refusals are thrown into the system under test; a system that swallows exceptions can hide them, but the call log still records the call with Matched = false.
    - Teardown disposes the provider first, then freezes every double; calls made during disposal still reach the doubles, later calls and publishes are refused by name.
    - Setups seal when the double is built, the environment seals when the first run uses it, and every run gets a fresh pack object.
    - MockArg.Any<T>() is type-checked and only valid as a whole argument; arrays and lists compare by content.
    - Keyed registrations of a replaced service are replaced too.
</runtime_behavior>

<documentation_notes>
    Guidance the agent should preserve:
    - Mock is deliberately treated as an ordinary environment; do not suggest writing to the run's stores from inside a double, or adding a collection step of its own.
    - Interfaces and methods only; properties, events and classes cannot be mocked yet.
    - Matchers are exact values and MockArg.Any<T>(); there is no predicate matcher and no call sequences yet.
    - Recorded arguments and payloads are held by reference; publish a copy when the system under test mutates the object afterwards.
</documentation_notes>

<style_guide>
    Keep one pack per replaced service, named after it: MailSenderPack for IMailSender.
    Keep setups short; move a long body into Compute only when it needs the artifact channel.
    Use identities that describe the evidence ("sentMail"), not the mechanism.
    Keep Var.Ref(...) arguments on Host for values the run varies, and plain literals for values it does not.
</style_guide>

<sample_patterns>
    Replace-and-verify pattern:
    - declare a pack for the dependency
    - host the real composition with the pack included
    - Host the system under test
    - assert with run.Mock<T>().CountCalls(...)

    Evidence pattern:
    - ProducesArtifact on the setup whose call is the evidence
    - FindArtifact after the Host step that causes it
    - CaptureArtifactVersion after each later step that should change it
    - read run.ArtifactStore.GetMockArtifact(...)
</sample_patterns>

<decision_rules>
    Recommend TestFramework.Mock when:
    - the system under test is a class resolvable from a service collection
    - its dependencies are what the test wants to control
    - the test should run in-process with no external services

    Recommend TestFramework.Web or the container packages instead when the system under test must be reached over HTTP or run in its own process.
</decision_rules>

<anti_patterns>
    Avoid:
    - Throws on an async method, which raises at the call; use ThrowsAsync
    - reading a scoped service's lazily loaded data after the Host call ended; its scope is disposed
    - two setups that can match the same call, expecting the later one to win
    - a value-returning setup without Returns, Compute or Throws
    - returning a ValueTask from a Host lambda instead of calling .AsTask()
    - expecting ProducesArtifact to appear in the run without a FindArtifact step
    - publishing the same identity from two packs
    - background work in the system under test that outlives the run and keeps calling doubles
</anti_patterns>

<important_type_map>
    Common type map for discovery and error interpretation:
    - MockDefinition<TService>: a Mock-Pack
    - MockBuilder<TService>: what Configure receives; Call(...) declares a setup
    - MockCallSetup<TService, TResult> / MockCallSetup<TService>: one setup's verbs
    - MockArg: argument matchers for Call expressions
    - MockArtifacts / MockArtifact: the publish channel in Compute, and what ProducesArtifact returns
    - MockEnvironment: the environment provider; For(...) and Include<TPack>()
    - MockExt: the timeline verbs; Host(...)
    - MockHostCallTrigger<TService, TResult> / MockHostCallTrigger<TService>: the steps behind Host
    - MockArtifactFinder: finds a published identity for FindArtifact
    - MockInstance<TService>: a run's frozen double; CountCalls, RecordedCalls
    - MockRunExtensions: Mock<T>(), MockResult<T>(label), GetMockArtifact(identifier)

    Discovery heuristics for the agent:
    - "No setup matches" means the pack lacks a setup for a call the system under test made.
    - "matches more than one setup" means two setups overlap.
    - "no mock host" means SetEnv(MockEnvironment...) is missing.
    - "would not await" means a Host lambda returns a ValueTask or similar.
</important_type_map>

<sources>
    README.md
    TestFramework.Mock/README.md
    Documentation/Arc42.md
    Documentation/ERROR-HANDLING-MOCK.md
</sources>

<grounding_files>
    Most important files for expert grounding, relative to the repository root:
    - TestFramework.Mock/MockDefinition.cs
    - TestFramework.Mock/MockBuilder.cs
    - TestFramework.Mock/MockInstance.cs
    - TestFramework.Mock/Setup/MockCallSetupBase.cs
    - TestFramework.Mock/Setup/MockCallSetup.Result.cs
    - TestFramework.Mock/Interception/MockInterceptor.cs
    - TestFramework.Mock/Environment/MockEnvironment.cs
    - TestFramework.Mock/Environment/MockHostEnvComponent.cs
    - TestFramework.Mock/Steps/MockExt.cs
    - TestFramework.Mock/Steps/MockHostCallTrigger.Result.cs
    - TestFramework.Mock/Artifacts/MockArtifactFinder.cs
    - TestFramework.Mock/Steps/MockRunExtensions.cs
    - UnitTests/TestFramework.Mock.Tests/ReadmeSamplesTests.cs
    - UnitTests/TestFramework.Mock.Tests/MockFindTests.cs
</grounding_files>

<repo_resolution>
    Resolve repository metadata with commands when needed:
    dotnet msbuild TestFramework.Mock/TestFramework.Mock.csproj -getProperty:RepositoryUrl
    dotnet msbuild TestFramework.Mock/TestFramework.Mock.csproj -getProperty:PackageProjectUrl
</repo_resolution>
