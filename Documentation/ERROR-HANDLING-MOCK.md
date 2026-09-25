# Error Handling In TestFramework.Mock

Every refusal in this package derives from `TimelineFrameworkException` and carries a message, recovery
steps and — where a set of valid values exists — the available options.

The design rule: a mistake fails at the earliest point that can see it, and the message is enough to fix
it without re-running the test.

## When Each Failure Fires

| When | Situation | Exception |
|---|---|---|
| building the timeline | `Host` call yields an awaitable it would not await (`ValueTask`, a task of a task) | `FrameworkConfigurationException` |
| building the environment | a second pack for a service another pack already replaces | `FrameworkConfigurationException` |
| building the environment | a pack included after a run has already used the environment | `FrameworkConfigurationException` |
| the run's environment starts | a setup is not a single direct method call on the service | `FrameworkConfigurationException` |
| the run's environment starts | `MockArg` used inside a larger argument expression | `FrameworkConfigurationException` |
| the run's environment starts | a typed lambda does not fit the mocked method | `FrameworkConfigurationException` |
| the run's environment starts | a setup states its result twice | `FrameworkConfigurationException` |
| the run's environment starts | a value-returning setup states no result | `FrameworkConfigurationException` |
| the run's environment starts | `Throws` or `ThrowsAsync` together with `ProducesArtifact` on one setup | `FrameworkConfigurationException` |
| the run's environment starts | the mocked service is not an interface | `FrameworkConfigurationException` |
| the run's environment starts | a second mock host in one run | `FrameworkStateException` |
| after the double is built | a setup or builder kept past `Configure` is changed | `FrameworkStateException` |
| a `Host` step runs | the run has no mock host | `FrameworkConfigurationException` |
| a `Host` step runs | the service is not registered in the composition | `FrameworkConfigurationException` |
| the system under test calls a double | no setup matches the call | `FrameworkConfigurationException` |
| the system under test calls a double | more than one setup matches the call | `FrameworkConfigurationException` |
| the system under test calls a double | the run has already finished | `FrameworkStateException` |
| a find or a later look | two doubles published the same identity | `FrameworkConfigurationException` |
| reading the finished run | `run.Mock<T>()` for a service no pack replaced | `FrameworkConfigurationException` |
| reading the finished run | `run.MockResult<T>(label)` with a type the step did not return | `FrameworkConfigurationException` |

Not an error: a `MockArtifactFinder` whose identity nothing has published yet. It finds nothing and logs a
warning, like any empty finder.

Packs are instantiated when the run's environment starts, so every pack mistake fails the run before its
first step — never halfway through.

## Refusals Inside The System Under Test

A call refusal — no match, two matches, a finished run — is thrown **into the system under test**, from
inside the call it made. Normally it propagates out through the `Host` step and fails it with the message
below. A system under test that catches every exception can swallow it; the call is still recorded, with
`Matched` false, so check `run.Mock<T>().RecordedCalls` when a step passes but behaves unexpectedly.

**No setup matches.** Names the call with its arguments and lists the setups that do exist:

```
[FRAMEWORK ERROR] FrameworkConfigurationException
======================================================================
No setup matches 'SendAsync("ada@example.com", "Hello")'.

Recovery:
  -> Add a mock.Call(...) for this call to the definition, or widen an existing matcher.

Available:
  * SendAsync(any String, "Welcome")
```

**More than one setup matches.** Lists exactly the overlapping setups. There is no precedence rule to fall
back on — narrow the matchers so one answers.

**The run is finished.** Something kept calling a double after its run ended. The provider is disposed
before the doubles freeze, so a system under test that stops its work when disposed never meets this; what
does is background work that outlives disposal. Make stopping it a step of the timeline.

## Pack Mistakes

**A mistyped lambda** lists the mocked method's real parameters, so the fix is a copy:

```
[FRAMEWORK ERROR] FrameworkConfigurationException
======================================================================
The lambda on 'ReadText(any String)' takes (Int32), which does not fit the method.

Recovery:
  -> Match the lambda's parameters to the mocked method's signature.

Available:
  * String path
```

**A value-returning setup with no result** is refused rather than answered with `default`: a mocked call
never invents a return value. State `Returns`, `Compute` or `Throws`.

**`Throws` or `ThrowsAsync` with `ProducesArtifact`** is refused as unreachable — the artifact only
publishes when a call completes, and for an async call when its task finishes successfully. A body that should publish and then fail states both by hand in `Compute`.

**`MockArg` inside a larger argument.** `MockArg.Any<int>() + 1` would be evaluated once, to a fixed
value, and silently match only that. Use `MockArg.Any<T>()` as a whole argument, or state the exact value.

**A setup changed after its double was built.** A setup object or builder kept past `Configure` refuses
further changes: the double is already answering calls, and a declaration changing underneath them would
race them.

## Host Mistakes

**An awaitable `Host` would not await** is refused as soon as `MockExt.Host(...)` is called, naming the
type — `ValueTask<Boolean>`, for example. Return a `Task` so the step finishes when the call does; for a
`ValueTask`, call `.AsTask()`.

**No mock host** means the run was started without `SetupRun(...).SetEnv(MockEnvironment.For(...))`.

**Not registered** means the composition handed to `MockEnvironment.For(...)` does not register the class
the `Host` step asked for. Register the system under test itself, not only its dependencies.

## Artifact Mistakes

**Two doubles, one identity.** A finder asked for an identity two doubles published, so which one it names
is ambiguous. The message lists both services; give each pack's `ProducesArtifact` or `Publish` its own
identity.

A captured mock artifact is never set up by a run — it comes into being when a double is called. Declaring
one with `RegisterArtifact` is refused; find it with `FindArtifact` instead.
