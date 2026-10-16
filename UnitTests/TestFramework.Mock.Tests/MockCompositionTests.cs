using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Mock.Tests.Fixtures;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// What the environment promises about the composition it is handed: a pack stands in for something the
/// composition registers, the composition is declared once and hosted as declared, and a service that cannot
/// be built says why in the container's own words.
/// </summary>
public class MockCompositionTests(ITestOutputHelper output)
{
    private static readonly Timeline _saveReport = Timeline.Create()
        .Trigger(MockExt.Host((ReportService service) => service.Save("report.txt")))
            .Label("save")
        .Build();

    [Fact]
    public async Task APackForAServiceTheCompositionNeverRegisters_IsRefusedBeforeTheRunStarts()
    {
        // A renamed dependency used to leave the double standing in for nothing, with every count reading zero.
        MockEnvironment environment = MockEnvironment.For(services => services.AddSingleton<ReportService>())
            .Include<FileStorePack>();

        FrameworkConfigurationException refusal = await Assert.ThrowsAsync<FrameworkConfigurationException>(
            () => _saveReport.SetupRun(null, output.WriteLine).SetEnv(environment).RunAsync());

        Assert.Contains(nameof(IFileStore), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(refusal.RecoverySteps, step => step.Contains("IncludeAdditional<FileStorePack>()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnAdditionalPack_AddsTheServiceTheCompositionLacks()
    {
        MockEnvironment environment = MockEnvironment.For(services => services.AddSingleton<ReportService>())
            .IncludeAdditional<FileStorePack>();

        TimelineRun run = await _saveReport.SetupRun(null, output.WriteLine).SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("report.txt")));
    }

    [Fact]
    public async Task AnAdditionalPackForAServiceTheCompositionRegisters_IsRefused()
    {
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
        }).IncludeAdditional<FileStorePack>();

        FrameworkConfigurationException refusal = await Assert.ThrowsAsync<FrameworkConfigurationException>(
            () => _saveReport.SetupRun(null, output.WriteLine).SetEnv(environment).RunAsync());

        Assert.Contains("Include<FileStorePack>()", string.Join(" ", refusal.RecoverySteps), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCompositionIsDeclaredOnce_HoweverOftenItsServicesAreRead()
    {
        int composed = 0;
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            composed++;
            services.AddSingleton<IFileStore, UnreachableFileStore>();
        }).Include<FileStorePack>();

        _ = environment.Nodes;
        _ = environment.Nodes;
        _ = environment.Nodes;

        Assert.Equal(1, composed);
    }

    [Fact]
    public async Task ACompositionThatRegistersDifferentServicesForTheRun_IsRefusedByName()
    {
        // The run was planned against the first composition; hosting a different one would host services the
        // plan never saw.
        int composed = 0;
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            composed++;
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
            if (composed > 1)
                services.AddSingleton<IAuditLog, NullAuditLog>();
        }).Include<FileStorePack>();

        TimelineRun run = await _saveReport.SetupRun(null, output.WriteLine).SetEnv(environment).RunAsync();

        TimelineRunFailedException failure = Assert.Throws<TimelineRunFailedException>(run.EnsureRanToCompletion);
        Assert.Contains(failure.FailedSteps, step =>
            step.StepException?.Message.Contains("registered different services", StringComparison.Ordinal) == true
            && step.StepException.Message.Contains(nameof(IAuditLog), StringComparison.Ordinal));
    }

    [Fact]
    public async Task AServiceTheContainerCannotBuild_SaysWhyInTheContainersWords()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((NeedsAClock service) => service.Now())).Label("now")
            .Build();
        MockEnvironment environment = MockEnvironment.For(services => services.AddSingleton<NeedsAClock>());

        TimelineRun run = await timeline.SetupRun(null, output.WriteLine).SetEnv(environment).RunAsync();

        FrameworkConfigurationException refusal = Assert.IsType<FrameworkConfigurationException>(run.Step("now").LastResult.Exception);
        Assert.Contains(nameof(NeedsAClock), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IClock), refusal.Message, StringComparison.Ordinal);
        Assert.Contains(refusal.RecoverySteps, step => step.Contains("AddLogging()", StringComparison.Ordinal));
    }

    public interface IClock
    {
        DateTimeOffset Now { get; }
    }

    public sealed class NeedsAClock(IClock clock)
    {
        public DateTimeOffset Now() => clock.Now;
    }

    private sealed class NullAuditLog : IAuditLog
    {
        public void Write(string entry)
        {
        }
    }
}
