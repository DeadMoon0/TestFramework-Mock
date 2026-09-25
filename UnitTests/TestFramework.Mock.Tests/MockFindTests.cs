using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Artifacts;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Timelines;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Tests.Fixtures;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// What a double published enters a run the way anything an outside system produced does: Core's
/// FindArtifact brings it in, Core's CaptureArtifactVersion takes a later look. Mock owns no verb of
/// its own for either, so add-versus-version is Core's, stated by where the timeline looks.
/// </summary>
public class MockFindTests(ITestOutputHelper output)
{
    private static MockEnvironment CreateEnvironment()
    {
        return MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
        }).Include<FileStorePack>();
    }

    [Fact]
    public async Task FindBringsThePublishIn_AndEachLaterLookIsAVersion_WithoutConsumingAnything()
    {
        Timeline timeline = Timeline.Create()
            .Trigger(MockExt.Host((ReportService service) => service.Save("first.txt"))).Name("save-first")
            .FindArtifact("createdFile", new MockArtifactFinder("createdFile"))
            .CaptureArtifactVersion("createdFile")
            .Trigger(MockExt.Host((ReportService service) => service.Save("second.txt"))).Name("save-second")
            .CaptureArtifactVersion("createdFile")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        ArtifactInstance<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference> artifact =
            run.ArtifactStore.GetArtifact<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference>("createdFile");

        // Found, looked at again with nothing new (a look reads, it does not drain), then looked at
        // after the second call.
        Assert.Equal(3, artifact.VersionCount);
        Assert.Equal("first.txt", artifact.First.Payload);
        Assert.Equal("second.txt", artifact.Last.Payload);
    }

    [Fact]
    public async Task FindingBeforeAnythingWasPublished_FindsNothing_LikeAnyEmptyFinder()
    {
        Timeline timeline = Timeline.Create()
            .FindArtifact("createdFile", new MockArtifactFinder("createdFile"))
            .Trigger(MockExt.Host((ReportService service) => service.Save("late.txt"))).Name("save")
            .Build();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(CreateEnvironment()).RunAsync();

        run.EnsureRanToCompletion();
        Assert.DoesNotContain(run.ArtifactStore.GetAll(), artifact => artifact.Identifier.Identifier == "createdFile");
    }

    [Fact]
    public void TwoDoublesPublishingOneIdentity_AreRefusedNamingBoth()
    {
        MockInstance<IFileStore> files = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true).ProducesArtifact((string path) => new("entry", path))).Create();
        MockInstance<IAuditLog> audit = new InlinePack<IAuditLog>(m =>
            m.Call(a => a.Write(MockArg.Any<string>())).ProducesArtifact((string line) => new("entry", line))).Create();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        MockHostState host = new(provider, new Dictionary<Type, MockInstanceGeneric>
        {
            [typeof(IFileStore)] = files,
            [typeof(IAuditLog)] = audit,
        });

        files.Object.CreateFile("a.txt");
        audit.Object.Write("created a.txt");

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(
            () => host.TryGetLatestPublish("entry", out _));
        Assert.Contains(nameof(IFileStore), refusal.AvailableOptions);
        Assert.Contains(nameof(IAuditLog), refusal.AvailableOptions);
    }

    [Fact]
    public async Task TheReference_ReResolvesTheLatestPublish_SoTheVersionVerbWorksAgainstIt()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>()))
                .Returns(true)
                .ProducesArtifact((string path) => new("createdFile", path))).Create();
        await using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();
        MockHostState host = new(provider, new Dictionary<Type, MockInstanceGeneric> { [typeof(IFileStore)] = mock });
        RunContext context = RunContext.Detached();
        context.State.GetOrAdd(() => host);

        mock.Object.CreateFile("v1.txt");
        mock.Object.CreateFile("v2.txt");

        MockCapturedArtifactReference reference = new("createdFile");
        ArtifactResolveResult<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference> resolved =
            await reference.ResolveToDataAsync(context, ArtifactVersionIdentifier.Default);
        Assert.True(resolved.Found);
        Assert.Equal("v2.txt", resolved.Data!.Payload);

        ArtifactResolveResult<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference> unknown =
            await new MockCapturedArtifactReference("neverPublished").ResolveToDataAsync(context, ArtifactVersionIdentifier.Default);
        Assert.False(unknown.Found);
    }
}
