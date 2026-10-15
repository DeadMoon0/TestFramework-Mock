using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Mock.Artifacts;

namespace TestFramework.Mock.Tests;

// README sync note: these tests mirror the public README samples for TestFramework.Mock.
// If you update a test here, update the corresponding README sample as well.

public interface IMailSender
{
    Task<bool> SendAsync(string to, string subject);
}

/// <summary>
/// What production registers. Every member throws, so reaching it proves the pack did not stand in.
/// </summary>
public sealed class SmtpMailSender : IMailSender
{
    public Task<bool> SendAsync(string to, string subject)
    {
        throw new InvalidOperationException("The real mail sender must never be reached in these samples.");
    }
}

public sealed class SignupService(IMailSender mail)
{
    private readonly HashSet<string> _forgotten = [];

    public async Task<bool> RegisterAsync(string email)
    {
        return await mail.SendAsync(email, "Welcome");
    }

    public async Task<bool> RegisterAsync(string email, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return await mail.SendAsync(email, "Welcome");
    }

    public void Forget(string email)
    {
        this._forgotten.Add(email);
    }
}

// Mirrors the Mock-Pack in the "Quick Start" of both READMEs.
public sealed class MailSenderPack : MockDefinition<IMailSender>
{
    protected override void Configure(MockBuilder<IMailSender> mock)
    {
        mock.Call(m => m.SendAsync(MockArg.Any<string>(), MockArg.Any<string>()))
            .ReturnsAsync(true)
            .ProducesArtifact((string to, string subject) => new("sentMail", $"{to}: {subject}"));
    }
}

public class ReadmeSamplesTests
{
    // Mirrors the "Quick Start" sample in TestFramework.Mock/README.md and the root README.md.
    [Fact]
    public async Task QuickStart_HostsTheServiceWithThePack_AndReadsResultCallsAndArtifact()
    {
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IMailSender, SmtpMailSender>();
            services.AddSingleton<SignupService>();
        }).Include<MailSenderPack>();

        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Label("register")
            .FindArtifact("sentMail", new MockArtifactFinder("sentMail"))
            .Build();

        TimelineRun run = await timeline.SetupRun().SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.MockResult<bool>("register"));
        Assert.Equal(1, run.Mock<IMailSender>().CountCalls(m => m.SendAsync("ada@example.com", MockArg.Any<string>())));
        Assert.Equal("ada@example.com: Welcome", run.ArtifactStore.GetMockArtifact("sentMail").Last.Payload);
    }

    // Mirrors the "Calling The System Under Test" sample in TestFramework.Mock/README.md.
    [Fact]
    public async Task HostArguments_ComeFromRunVariables()
    {
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IMailSender, SmtpMailSender>();
            services.AddSingleton<SignupService>();
        }).Include<MailSenderPack>();

        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host(
                Var.Ref<string>("email"),
                (string email, SignupService signup) => signup.RegisterAsync(email))).Label("register")
            .Build();

        TimelineRun run = await timeline.SetupRun()
            .AddVariable("email", "grace@example.com")
            .SetEnv(environment)
            .RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(1, run.Mock<IMailSender>().CountCalls(m => m.SendAsync("grace@example.com", MockArg.Any<string>())));
    }

    // Mirrors the "Taking A Later Look" sample in TestFramework.Mock/README.md.
    [Fact]
    public async Task ALaterLook_IsANewVersionOfTheFoundArtifact()
    {
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IMailSender, SmtpMailSender>();
            services.AddSingleton<SignupService>();
        }).Include<MailSenderPack>();

        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Label("first")
            .FindArtifact("sentMail", new MockArtifactFinder("sentMail"))
            .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("grace@example.com"))).Label("second")
            .CaptureArtifactVersion("sentMail")
            .Build();

        TimelineRun run = await timeline.SetupRun().SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(2, run.ArtifactStore.GetMockArtifact("sentMail").VersionCount);
        Assert.Equal("ada@example.com: Welcome", run.ArtifactStore.GetMockArtifact("sentMail").First.Payload);
        Assert.Equal("grace@example.com: Welcome", run.ArtifactStore.GetMockArtifact("sentMail").Last.Payload);
    }

    // Mirrors the void and cancellation-token bullets under "Calling The System Under Test" in TestFramework.Mock/README.md.
    [Fact]
    public async Task HostShapes_VoidAndCancellationToken_AsTheReadmeShowsThem()
    {
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IMailSender, SmtpMailSender>();
            services.AddSingleton<SignupService>();
        }).Include<MailSenderPack>();

        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((SignupService signup, CancellationToken cancellation) =>
                signup.RegisterAsync("ada@example.com", cancellation))).Label("register")
            .Trigger(MockExt.Host((SignupService signup) => signup.Forget("ada@example.com"))).Label("forget")
            .Build();

        TimelineRun run = await timeline.SetupRun().SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.MockResult<bool>("register"));
    }

    [Fact]
    public void OutAndRefParameters_AsTheReadmeShowsThem()
    {
        MockInstance<Fixtures.IKeyStore> stated = new Fixtures.InlinePack<Fixtures.IKeyStore>(mock =>
            mock.Call(s => s.TryGet("known", out MockArg.Ref<string>.Any))
                .Returns(true)
                .Assigns("value", "hello")).Create();
        MockInstance<Fixtures.IKeyStore> computed = new Fixtures.InlinePack<Fixtures.IKeyStore>(mock =>
            mock.Call(s => s.TryGet(MockArg.Any<string>(), out MockArg.Ref<string>.Any))
                .Returns(true)
                .Assigns("value", (string key) => key.ToUpperInvariant())).Create();
        MockInstance<Fixtures.IKeyStore> bumping = new Fixtures.InlinePack<Fixtures.IKeyStore>(mock =>
            mock.Call(s => s.Bump(ref MockArg.Ref<int>.Any))
                .Assigns("counter", (int counter) => counter + 1)).Create();

        stated.Object.TryGet("known", out string hello);
        computed.Object.TryGet("abc", out string upper);
        int counter = 1;
        bumping.Object.Bump(ref counter);

        Assert.Equal(("hello", "ABC", 2), (hello, upper, counter));
    }
}
