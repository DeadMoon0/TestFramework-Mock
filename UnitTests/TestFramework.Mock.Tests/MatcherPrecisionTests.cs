using System.Linq;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Tests.Fixtures;

namespace TestFramework.Mock.Tests;

/// <summary>
/// A matcher means exactly what it reads as. Each case names what went wrong while it did not.
/// </summary>
public class MatcherPrecisionTests
{
    [Fact]
    public void AnyOfAType_DoesNotAnswerAValueOfAnotherType()
    {
        // Damage without the check: a setup for strings answered an int passed to the same object
        // parameter, so a test "about strings" passed on a call it never meant.
        MockInstance<IValueSink> mock = new InlinePack<IValueSink>(m =>
            m.Call(s => s.Put(MockArg.Any<string>())).Returns(1)).Create();

        Assert.Equal(1, mock.Object.Put("text"));
        Assert.Equal(1, mock.Object.Put(null));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => mock.Object.Put(42));
        Assert.Contains("No setup matches 'Put(42)'", refusal.Message);
    }

    [Fact]
    public void AnyOfAValueType_MatchesThroughTheConversionTheCompilerInserts()
    {
        // Damage without unwrapping: MockArg.Any<int>() passed to an object parameter is wrapped in a
        // boxing conversion, was not recognised, and became an exact match on 0.
        MockInstance<IValueSink> mock = new InlinePack<IValueSink>(m =>
        {
            m.Call(s => s.Put(MockArg.Any<int>())).Returns(2);
            m.Call(s => s.Maybe(MockArg.Any<int?>())).Returns(3);
        }).Create();

        Assert.Equal(2, mock.Object.Put(7));
        Assert.Throws<FrameworkConfigurationException>(() => mock.Object.Put("7"));
        Assert.Equal(3, mock.Object.Maybe(5));
        Assert.Equal(3, mock.Object.Maybe(null));
    }

    [Fact]
    public void MockArgInsideALargerArgument_IsRefused_InsteadOfBecomingAFixedValue()
    {
        // Damage without the refusal: "MockArg.Any<int>() + 1" was evaluated once and meant "exactly 1".
        InlinePack<IValueSink> pack = new(m => m.Call(s => s.Put(MockArg.Any<int>() + 1)).Returns(1));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("inside the argument", refusal.Message);
    }

    [Fact]
    public void ExactCollections_MatchByContent_AndARefusalShowsTheElements()
    {
        // Damage without it: an array compared by reference never matched, and the refusal read
        // 'Bytes(System.Byte[])', naming the type and hiding the difference.
        MockInstance<IValueSink> mock = new InlinePack<IValueSink>(m =>
            m.Call(s => s.Bytes(new byte[] { 1, 2 })).Returns(1)).Create();

        Assert.Equal(1, mock.Object.Bytes([1, 2]));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => mock.Object.Bytes([1, 3]));
        Assert.Contains("Bytes([1, 3])", refusal.Message);
        Assert.Contains("Bytes([1, 2])", refusal.AvailableOptions.Single());
    }

    [Fact]
    public void ASetupKeptBeyondConfigure_CannotChangeTheBuiltDouble()
    {
        // Damage without the seal: a producer added to the kept setup ran on the live double - a
        // declaration changing underneath the calls it was answering.
        MockCallSetup<IFileStore, bool>? kept = null;
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
        {
            kept = m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true);
        }).Create();

        Assert.Throws<FrameworkStateException>(() => kept!.ProducesArtifact((string path) => new("late", path)));

        mock.Object.CreateFile("a.txt");
        Assert.False(mock.TryGetLatestPublish("late", out _));
    }

    [Fact]
    public void ABuilderKeptBeyondConfigure_CannotDeclareASetupThatWouldNeverAnswer()
    {
        MockBuilder<IFileStore>? kept = null;
        new InlinePack<IFileStore>(m =>
        {
            kept = m;
            m.Call(f => f.Delete(MockArg.Any<string>()));
        }).Create();

        Assert.Throws<FrameworkStateException>(() => kept!.Call(f => f.ReadText(MockArg.Any<string>())));
    }
}
