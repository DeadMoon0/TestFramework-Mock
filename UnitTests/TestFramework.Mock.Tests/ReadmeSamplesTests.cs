using System;
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
    public async Task<bool> RegisterAsync(string email)
    {
        return await mail.SendAsync(email, "Welcome");
    }
}

// Mirrors the Mock-Pack in the "Quick Start" of both READMEs.
public sealed class MailSenderPack : MockDefinition<IMailSender>
{
    protected override void Configure(MockBuilder<IMailSender> mock)
    {
        mock.Call(m => m.SendAsync(MockArg.Any<string>(), MockArg.Any<string>()))
            .Returns(Task.FromResult(true))
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
            .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Name("register")
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
                (string email, SignupService signup) => signup.RegisterAsync(email))).Name("register")
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
            .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("ada@example.com"))).Name("first")
            .FindArtifact("sentMail", new MockArtifactFinder("sentMail"))
            .Trigger(MockExt.Host((SignupService signup) => signup.RegisterAsync("grace@example.com"))).Name("second")
            .CaptureArtifactVersion("sentMail")
            .Build();

        TimelineRun run = await timeline.SetupRun().SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(2, run.ArtifactStore.GetMockArtifact("sentMail").VersionCount);
        Assert.Equal("ada@example.com: Welcome", run.ArtifactStore.GetMockArtifact("sentMail").First.Payload);
        Assert.Equal("grace@example.com: Welcome", run.ArtifactStore.GetMockArtifact("sentMail").Last.Payload);
    }
}
