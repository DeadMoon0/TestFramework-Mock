![Icon](https://raw.githubusercontent.com/DeadMoon0/TestFramework-Common/96ef4240c1e55ba95a20b99285219a61407c6355/Assets/Icon.svg)
[![NuGet Version](https://img.shields.io/nuget/v/TestFramework.Mock?label=nuget%20TestFramework.Mock)](https://www.nuget.org/packages/TestFramework.Mock)

# TestFramework-Mock

`TestFramework.Mock` lets a normal TestFramework timeline test the system under test in-process: your
application's own service composition, with chosen dependencies replaced by declared test doubles.

Use it when the thing under test is a class you can resolve from a service collection, and its
dependencies are what you want to control. The timeline shape, variables, artifacts, retries, timeouts
and debugging UI are the same ones the rest of the framework uses.

## Packages

- `TestFramework.Mock`: Mock-Packs, the in-process mock host, the `Host` trigger and the mock artifact
  finder

## Install

```bash
dotnet add package TestFramework.Mock
```

## Clone This Repository

The package icon lives in the `TestFramework-Common` submodule, so clone with submodules:

```bash
git clone --recurse-submodules https://github.com/DeadMoon0/TestFramework-Mock.git
```

Already cloned without them? Run `git submodule update --init --recursive`. Building and `dotnet test`
work either way; only `dotnet pack` needs the submodule.

## What It Does

```csharp
public sealed class MailSenderPack : MockDefinition<IMailSender>
{
    protected override void Configure(MockBuilder<IMailSender> mock)
    {
        mock.Call(m => m.SendAsync(Arg.Any<string>(), Arg.Any<string>()))
            .Returns(Task.FromResult(true))
            .ProducesArtifact((string to, string subject) => new("sentMail", $"{to}: {subject}"));
    }
}

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
Assert.True(run.MockResult<bool>("register"));
Assert.Equal(1, run.Mock<IMailSender>().CountCalls(m => m.SendAsync("ada@example.com", Arg.Any<string>())));
```

Three rules shape everything else:

1. **Strict, and never a coin toss.** A call no setup matches is refused by name; so is a call two setups
   match. A setup for a method that returns something must say what it returns.
2. **Mock is an environment like any other.** A double records what a call left behind; a step brings it
   into the run with Core's `FindArtifact`, the way it would find a file another program wrote.
3. **A finished run is a snapshot.** The doubles freeze with the run, and the run records which pack
   stood in for which service.

## Documentation Map

- Package guide: [TestFramework.Mock/README.md](./TestFramework.Mock/README.md)
- Architecture overview: [Documentation/Arc42.md](./Documentation/Arc42.md)
- Error handling: [Documentation/ERROR-HANDLING-MOCK.md](./Documentation/ERROR-HANDLING-MOCK.md)

## Repository Layout

| Path | Purpose |
|---|---|
| `TestFramework.Mock/` | the shipped package |
| `UnitTests/TestFramework.Mock.Tests/` | unit tests, full timeline runs, README samples and the family convention checks |
| `AI/` | the addon skill for AI assistants |
| `Documentation/` | architecture notes and the error-handling guide |
| `Modules/TestFramework-Common/` | shared icon and licence (submodule) |

## Building And Testing

```bash
dotnet build TestFramework.Mock.slnx -c Release
```

```bash
dotnet test UnitTests/TestFramework.Mock.Tests/TestFramework.Mock.Tests.csproj -c Release
```

The tests host everything in-process, so they need no Docker, no external service and no configuration.

The argument-count overloads are generated from T4 templates, and the generated files are committed. After
editing a `.tt` file, regenerate it with the local tool:

```bash
dotnet tool restore
```

```bash
dotnet t4 TestFramework.Mock/Steps/MockExt.Arities.tt
```

## Related Repositories

- [TestFramework-Core](https://github.com/DeadMoon0/TestFramework-Core) for the runtime engine this package extends
- [TestFramework-Showroom](https://github.com/DeadMoon0/TestFramework-Showroom) for sample workflows and first examples

## CI Pull Requests

- Pull requests run the unit tests through the GitHub Actions workflow `unit-tests`.
- If branch protection requires status checks, `unit-tests` must pass before merge.
