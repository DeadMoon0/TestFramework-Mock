using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Timelines;
using TestFramework.Mock.Tests.Fixtures;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// How a hosted call is made: synchronous void calls, the step's cancellation reaching the call, and one
/// scope per call the way production opens one per request.
/// </summary>
public class HostCallTests(ITestOutputHelper output)
{
    private static MockEnvironment CreateEnvironment()
    {
        return MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
            services.AddSingleton<CancellableReportService>();
            services.AddSingleton<DisposalLog>();
            services.AddScoped<ScopedWorker>();
        }).Include<FileStorePack>();
    }

    [Fact]
    public async Task ASynchronousVoidCall_IsASupportedShape()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((ReportService service) => service.Remove("old.txt"))).Label("remove")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output.WriteLine).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.Delete("old.txt")));
    }

    [Fact]
    public async Task TheStepsCancellationToken_ReachesTheCall()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((CancellableReportService service, CancellationToken cancellation) => service.TokenCanBeCancelledAsync(cancellation))).Label("token")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output.WriteLine).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.HostResult<bool>("token"));
    }

    [Fact]
    public async Task ACallHonouringTheToken_StopsWhenTheStepTimesOut()
    {
        // Damage without the token: the call cannot be told to stop, keeps running after its step timed
        // out, and the timeout reports that whatever it was going to say was never heard.
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((CancellableReportService service, CancellationToken cancellation) => service.WaitUntilCancelledAsync(cancellation)))
                .Label("wait").WithTimeout(TimeSpan.FromMilliseconds(300))
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output.WriteLine).SetEnv(CreateEnvironment()).RunAsync();

        StepResultGeneric result = run.Step("wait").LastResult;
        Assert.Equal(StepState.Timeout, result.State);
        Assert.DoesNotContain("still running", result.Exception?.Message ?? string.Empty);
    }

    [Fact]
    public async Task EveryCall_GetsItsOwnScope_AndItsScopedServicesAreDisposedWhenItEnds()
    {
        // Damage without scopes: a scoped service resolved from the root provider behaved like a singleton
        // for the whole run - one instance shared by every call, disposed only at the end of the run.
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((ScopedWorker worker) => worker.Id)).Label("first")
            .Trigger(MockExt.Host((ScopedWorker worker) => worker.Id)).Label("second")
            .Trigger(MockExt.Host((DisposalLog log) => log.Disposed)).Label("disposed")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output.WriteLine).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.NotEqual(run.HostResult<Guid>("first"), run.HostResult<Guid>("second"));
        Assert.Equal(2, run.HostResult<int>("disposed"));
    }

    [Fact]
    public async Task ReadingAResultWithTheWrongType_IsRefusedNamingWhatTheStepReturned()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((ReportService service) => service.Save("a.txt"))).Label("save")
            .Trigger(MockExt.Host((ReportService service) => service.Remove("a.txt"))).Label("remove")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output.WriteLine).SetEnv(CreateEnvironment()).RunAsync();
        run.EnsureRanToCompletion();

        FrameworkConfigurationException wrongType = Assert.Throws<FrameworkConfigurationException>(() => run.HostResult<string>("save"));
        Assert.Contains("MockCallResult<Boolean>", wrongType.Message);

        FrameworkConfigurationException noValue = Assert.Throws<FrameworkConfigurationException>(() => run.HostResult<bool>("remove"));
        Assert.Contains("EmptyStepResultContext", noValue.Message);
    }
}
