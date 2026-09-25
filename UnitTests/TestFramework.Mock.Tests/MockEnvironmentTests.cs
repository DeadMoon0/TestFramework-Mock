using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Mock.Tests.Fixtures;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// The whole road, driven the way a user drives it: one timeline, a MockEnvironment per run, the
/// system under test composed in-process with the pack's double standing in for its dependency.
/// </summary>
public class MockEnvironmentTests(ITestOutputHelper output)
{
    private static readonly Timeline _timeline = Timeline.Create()
        .Trigger(MockExt.Host((ReportService service) => service.Save("report.txt")))
            .Name("save")
        .Build();

    private static MockEnvironment CreateEnvironment()
    {
        return MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
        }).Include<FileStorePack>();
    }

    [Fact]
    public async Task TheHostedCallRuns_AgainstThePacksDouble_NotTheRealDependency()
    {
        TimelineRun run = await _timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.MockResult<bool>("save"));
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("report.txt")));
    }

    [Fact]
    public async Task TheFinishedRun_SaysWhichPackStoodWhere()
    {
        TimelineRun run = await _timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.EffectiveSettings.TryGet(MockResourceKinds.Host, typeof(IFileStore).FullName!, out string? pack));
        Assert.Equal(nameof(FileStorePack), pack);
    }

    [Fact]
    public async Task TheDouble_FreezesWithItsRun()
    {
        TimelineRun run = await _timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();
        run.EnsureRanToCompletion();

        MockInstance<IFileStore> mock = run.Mock<IFileStore>();
        Assert.Throws<FrameworkStateException>(() => mock.Object.CreateFile("after-the-run.txt"));
        Assert.Single(mock.RecordedCalls);
    }

    [Fact]
    public async Task WithoutAFind_ThePublishStaysInTheEnvironment_AndNeverEntersTheRun()
    {
        // This timeline finds nothing, so the payload is recorded where the double left it and the
        // run's artifact store never hears of it - as with a file nobody went looking for.
        TimelineRun run = await _timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();
        run.EnsureRanToCompletion();

        Assert.True(run.Mock<IFileStore>().TryGetLatestPublish("createdFile", out object? payload));
        Assert.Equal("report.txt", payload);
        Assert.DoesNotContain(run.ArtifactStore.GetAll(), artifact => artifact.Identifier.Identifier == "createdFile");
    }

    [Fact]
    public async Task TwoRuns_GetTwoDoubles_NothingLeaksBetweenThem()
    {
        MockEnvironment environment = CreateEnvironment();
        TimelineRun first = await _timeline.SetupRun(null, output).SetEnv(environment).RunAsync();
        TimelineRun second = await _timeline.SetupRun(null, output).SetEnv(environment).RunAsync();

        first.EnsureRanToCompletion();
        second.EnsureRanToCompletion();
        Assert.NotSame(first.Mock<IFileStore>(), second.Mock<IFileStore>());
        Assert.Single(second.Mock<IFileStore>().RecordedCalls);
    }

    [Fact]
    public void ASecondPackForOneService_IsRefusedNamingBoth_AndTheSamePackAgainIsNot()
    {
        MockEnvironment environment = CreateEnvironment();

        Assert.Same(environment, environment.Include<FileStorePack>());

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(
            () => environment.Include<OtherFileStorePack>());
        Assert.Contains(nameof(FileStorePack), refusal.Message);
        Assert.Contains(nameof(OtherFileStorePack), refusal.Message);
    }

    [Fact]
    public async Task WithoutTheEnvironment_TheTriggerRefusesByName()
    {
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            TimelineRun run = await _timeline.SetupRun(null, output).RunAsync();
            run.EnsureRanToCompletion();
        });
    }

    [Fact]
    public async Task KeyedRegistrations_AreReplacedToo_SoNoPathReachesTheRealDependency()
    {
        // Damage without it: the keyed registration survived, and a service asking for it by key was
        // handed the real dependency while the test believed it ran against the double.
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((KeyedReportService service) => service.Archive("a.txt"))).Name("archive")
            .Build();
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddKeyedSingleton<IFileStore, UnreachableFileStore>("archive");
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<KeyedReportService>();
        }).Include<FileStorePack>();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("a.txt")));
        Assert.True(run.EffectiveSettings.TryGet(MockResourceKinds.Host, $"{typeof(IFileStore).FullName}[archive]", out string? pack));
        Assert.Equal(nameof(FileStorePack), pack);
    }

    [Fact]
    public async Task EveryRun_GetsAFreshPack_SoAPacksFieldCannotCarryOneRunIntoTheNext()
    {
        // Damage without it: one pack object served every run, so CountingPack's field made the second
        // run's double answer differently from the first.
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
        }).Include<CountingPack>();

        TimelineRun first = await _timeline.SetupRun(null, output).SetEnv(environment).RunAsync();
        TimelineRun second = await _timeline.SetupRun(null, output).SetEnv(environment).RunAsync();

        first.EnsureRanToCompletion();
        second.EnsureRanToCompletion();
        Assert.True(first.MockResult<bool>("save"));
        Assert.True(second.MockResult<bool>("save"));
    }

    [Fact]
    public async Task APackIncludedAfterARunUsedTheEnvironment_IsRefused_AndTheSamePackAgainIsNot()
    {
        // Damage without the seal: a later Include silently changed what the next runs hosted, and raced
        // with runs already reading the pack list.
        MockEnvironment environment = CreateEnvironment();
        (await _timeline.SetupRun(null, output).SetEnv(environment).RunAsync()).EnsureRanToCompletion();

        Assert.Same(environment, environment.Include<FileStorePack>());
        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => environment.Include<AuditLogPack>());
        Assert.Contains(nameof(AuditLogPack), refusal.Message);
    }

    [Fact]
    public async Task WhatTheSystemUnderTestDoesWhileBeingDisposed_StillReachesTheDouble()
    {
        // Damage without the order: the double froze before the provider disposed the service, so the
        // flush inside Dispose was refused - on a background thread, a refusal like that can end the
        // test process.
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((FlushingReportService service) => service.Save("report.txt"))).Name("save")
            .Build();
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<FlushingReportService>();
        }).Include<FileStorePack>();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        MockInstance<IFileStore> mock = run.Mock<IFileStore>();
        Assert.Equal(1, mock.CountCalls(f => f.CreateFile("flushed-on-dispose.txt")));
        Assert.All(mock.RecordedCalls, call => Assert.True(call.Matched));
        Assert.Throws<FrameworkStateException>(() => mock.Object.CreateFile("after-the-run.txt"));
    }
}
