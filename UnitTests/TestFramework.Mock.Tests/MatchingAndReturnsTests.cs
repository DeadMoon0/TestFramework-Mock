using System;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Tests.Fixtures;

namespace TestFramework.Mock.Tests;

public class MatchingAndReturnsTests
{
    [Fact]
    public void AnyMatcher_AnswersEveryValue()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true)).Create();

        Assert.True(mock.Object.CreateFile("one"));
        Assert.True(mock.Object.CreateFile("two"));
    }

    [Fact]
    public void ExactMatcher_AnswersOnlyItsValue_AndTheRefusalNamesTheCall()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile("expected.txt")).Returns(true)).Create();

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(
            () => mock.Object.CreateFile("other.txt"));

        Assert.Contains("No setup matches", refusal.Message);
        Assert.Contains("other.txt", refusal.Message);
    }

    [Fact]
    public void Returns_Lambda_ReceivesTheCallArgument()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.ReadText(MockArg.Any<string>())).Returns((string path) => path + "!")).Create();

        Assert.Equal("a.txt!", mock.Object.ReadText("a.txt"));
    }

    [Fact]
    public void Returns_Lambda_ReceivesBothArguments()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.Copy(MockArg.Any<string>(), "target")).Returns((string from, string to) => from.Length + to.Length)).Create();

        Assert.Equal(12, mock.Object.Copy("source", "target"));
    }

    [Fact]
    public void ACallMatchingTwoSetups_IsRefusedNamingBoth_AnyAmbiguityIsAFailure()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
        {
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(false);
            m.Call(f => f.CreateFile("special.txt")).Returns(true);
        }).Create();

        Assert.False(mock.Object.CreateFile("plain.txt"));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(
            () => mock.Object.CreateFile("special.txt"));
        Assert.Contains("more than one setup", refusal.Message);
    }

    [Fact]
    public void Throws_ThrowsTheStatedException()
    {
        InvalidOperationException stated = new("disk full");
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Throws(stated)).Create();

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
            () => mock.Object.CreateFile("x"));
        Assert.Same(stated, thrown);
    }

    [Fact]
    public void MistypedLambda_IsRefusedWhereItWasDeclared()
    {
        InlinePack<IFileStore> pack = new(m =>
            m.Call(f => f.ReadText(MockArg.Any<string>())).Returns((int wrong) => wrong.ToString()));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("does not fit the method", refusal.Message);
    }

    [Fact]
    public void ValueReturningSetupWithoutResult_IsRefusedAtInstanceCreation()
    {
        InlinePack<IFileStore> pack = new(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).ProducesArtifact((string path) => new(path, null)));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("states no result", refusal.Message);
    }

    [Fact]
    public void ASecondResult_IsRefused()
    {
        InlinePack<IFileStore> pack = new(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true).Throws(new InvalidOperationException()));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("already states its result", refusal.Message);
    }

    [Fact]
    public void NonInterfaceService_IsRefusedByName()
    {
        InlinePack<FileStoreBase> pack = new(m => { });

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains(nameof(FileStoreBase), refusal.Message);
        Assert.Contains("interface", refusal.Message);
    }
}
