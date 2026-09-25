using System;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Tests.Fixtures;

namespace TestFramework.Mock.Tests;

/// <summary>
/// What a call leaves behind in the environment. Nothing here touches a run: the double records,
/// and bringing the record into a run is Core's finder and version verbs (see <see cref="MockFindTests"/>).
/// </summary>
public class ArtifactFlowTests
{
    [Fact]
    public void Compute_PublishesWithTheArgumentCapturedAtCallTime()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Compute((string path, MockArtifacts artifacts) =>
            {
                artifacts.Publish("createdFile", path);
                return true;
            })).Create();

        Assert.True(mock.Object.CreateFile("report.txt"));

        Assert.Equal("report.txt", Published(mock, "createdFile"));
    }

    [Fact]
    public void ProducesArtifact_Stacks_TwoArtifactsFromOneCall()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>()))
                .Returns(true)
                .ProducesArtifact((string path) => new("createdFile", path))
                .ProducesArtifact((string path) => new("createdFileName", path.ToUpperInvariant()))).Create();

        mock.Object.CreateFile("report.txt");

        Assert.Equal("report.txt", Published(mock, "createdFile"));
        Assert.Equal("REPORT.TXT", Published(mock, "createdFileName"));
    }

    [Fact]
    public void ALaterPublishOfAnIdentity_ReplacesWhatTheEnvironmentHolds()
    {
        // As a second write to a file does. Which states become versions in a run is the timeline's
        // to say, by where it takes a look.
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>()))
                .Returns(true)
                .ProducesArtifact((string path) => new("createdFile", path))).Create();

        mock.Object.CreateFile("v1.txt");
        mock.Object.CreateFile("v2.txt");

        Assert.Equal("v2.txt", Published(mock, "createdFile"));
    }

    [Fact]
    public void DistinctIdentities_AreRecordedSideBySide()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.Copy(MockArg.Any<string>(), MockArg.Any<string>()))
                .Returns(1)
                .ProducesArtifact((string from, string to) => new($"copy:{to}", from))).Create();

        mock.Object.Copy("a.txt", "x");
        mock.Object.Copy("b.txt", "y");

        Assert.Equal("a.txt", Published(mock, "copy:x"));
        Assert.Equal("b.txt", Published(mock, "copy:y"));
    }

    [Fact]
    public void ThrowsPlusProducesArtifact_IsRefusedAtInstanceCreation_TheArtifactCouldNeverHappen()
    {
        InlinePack<IFileStore> pack = new(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>()))
                .ProducesArtifact((string path) => new("attemptedFile", path))
                .Throws(new InvalidOperationException("disk full")));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("could never be published", refusal.Message);
    }

    [Fact]
    public void ADeclaredArtifact_IsNotAssumed_WhenTheCallThrows()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>()))
                .Returns((string path) => path.Length > 0 ? throw new InvalidOperationException("disk full") : false)
                .ProducesArtifact((string path) => new("createdFile", path))).Create();

        Assert.Throws<InvalidOperationException>(() => mock.Object.CreateFile("doomed.txt"));

        Assert.False(mock.TryGetLatestPublish("createdFile", out _));
    }

    [Fact]
    public void WhatAComputeBodyPublishedByHandBeforeThrowing_Stands()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Compute((string path, MockArtifacts artifacts) =>
            {
                artifacts.Publish("attemptedFile", path);
                throw new InvalidOperationException("disk full");
            })).Create();

        Assert.Throws<InvalidOperationException>(() => mock.Object.CreateFile("doomed.txt"));

        Assert.Equal("doomed.txt", Published(mock, "attemptedFile"));
    }

    [Fact]
    public void AVoidCall_CanPublish_WithoutStatingABody()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.Delete(MockArg.Any<string>()))
                .ProducesArtifact((string path) => new("deletedFile", path))).Create();

        mock.Object.Delete("old.txt");

        Assert.Equal("old.txt", Published(mock, "deletedFile"));
    }

    [Fact]
    public void AFrozenRecord_RefusesAPublish()
    {
        MockArtifactRecord record = new();
        MockArtifacts artifacts = new(record);

        record.FreezeForRunEnd();

        Assert.Throws<FrameworkStateException>(() => artifacts.Publish("late", null));
    }

    private static object? Published(MockInstance<IFileStore> mock, string identity)
    {
        Assert.True(mock.TryGetLatestPublish(identity, out object? payload), $"nothing was published as '{identity}'");
        return payload;
    }
}
