using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Timelines;
using TestFramework.Mock.Tests.Fixtures;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// A hosted call that returns a task is awaited: the step finishes when the call does. Every case
/// uses a system under test that reaches its dependency only after its first await, so a trigger
/// that handed the task back unawaited would pass with the double never called.
/// </summary>
public class AsyncHostTests(ITestOutputHelper output)
{
    private static MockEnvironment CreateEnvironment()
    {
        return MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<AsyncReportService>();
        }).Include<FileStorePack>();
    }

    [Fact]
    public async Task ATaskOfT_IsAwaited_AndTheStepsResultIsWhatTheTaskYielded()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((AsyncReportService service) => service.SaveAsync("report.txt"))).Name("save")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.MockResult<bool>("save"));
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("report.txt")));
    }

    [Fact]
    public async Task APlainTask_IsAwaited_EvenFromAnAsyncLambda()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host(async (AsyncReportService service) => { await service.TouchAsync("touched.txt"); })).Name("touch")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("touched.txt")));
    }

    [Fact]
    public async Task AnAwaitedCallThatFailsAfterItsFirstAwait_FailsItsStep()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((AsyncReportService service) => service.FailAsync())).Name("fail")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        StepResultGeneric result = run.Step("fail").LastResult;
        Assert.Equal(StepState.Error, result.State);
        InvalidOperationException failure = Assert.IsType<InvalidOperationException>(result.Exception);
        Assert.Equal("failed after the first await", failure.Message);
    }

    [Fact]
    public void AnAwaitableHostWouldNotAwait_IsRefusedWhereTheTriggerIsDeclared()
    {
        FrameworkConfigurationException valueTask = Assert.Throws<FrameworkConfigurationException>(
            () => MockExt.Host((AsyncReportService service) => service.SaveValueAsync("x")));
        Assert.Contains("ValueTask<Boolean>", valueTask.Message);
        Assert.Contains(".AsTask()", valueTask.RecoverySteps[0]);

        // Stating the task as the result by hand takes the synchronous overload, so it is refused too.
        FrameworkConfigurationException statedTask = Assert.Throws<FrameworkConfigurationException>(
            () => MockExt.Host<AsyncReportService, Task<bool>>(service => service.SaveAsync("x")));
        Assert.Contains("Task<Boolean>", statedTask.Message);
    }
}
