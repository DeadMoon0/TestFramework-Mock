using System;
using System.Threading.Tasks;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Tests.Fixtures;

namespace TestFramework.Mock.Tests;

/// <summary>
/// A dependency with out and ref parameters behaves the way a real one does, and a double never hands back an out
/// value nobody stated.
/// </summary>
/// <remarks>
/// The damage this prevents is the double inventing a value. <c>bool TryGet(string key, out string value)</c> used to
/// set up and answer without complaint: the caller got <c>true</c> and a null <c>value</c>, because nothing ever wrote
/// a by-ref argument back.
/// </remarks>
public class ByRefParameterTests
{
    [Fact]
    public void AnOutValueIsWrittenBack()
    {
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.TryGet("known", out MockArg.Ref<string>.Any)).Returns(true).Assigns("value", "hello")).Create();

        Assert.True(mock.Object.TryGet("known", out string value));
        Assert.Equal("hello", value);
    }

    [Fact]
    public void AnOutValueCanBeComputedFromTheCallsArguments()
    {
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.TryGet(MockArg.Any<string>(), out MockArg.Ref<string>.Any))
                .Returns(true)
                .Assigns("value", (string key) => key.ToUpperInvariant())).Create();

        mock.Object.TryGet("abc", out string value);

        Assert.Equal("ABC", value);
    }

    [Fact]
    public void ARefValueIsWrittenBackFromItsIncomingValue()
    {
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.Bump(ref MockArg.Ref<int>.Any)).Assigns("counter", (int counter) => counter + 1)).Create();

        int counter = 4;
        mock.Object.Bump(ref counter);

        Assert.Equal(5, counter);
    }

    [Fact]
    public void AnUnassignedRefKeepsTheCallersValue()
    {
        // The caller supplied a ref value, so leaving it alone states nothing new; only out has to be stated.
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m => m.Call(s => s.Bump(ref MockArg.Ref<int>.Any))).Create();

        int counter = 4;
        mock.Object.Bump(ref counter);

        Assert.Equal(4, counter);
    }

    [Fact]
    public void ARefPositionWithAValueMatchesOnlyThatValue()
    {
        int expected = 7;
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.Bump(ref expected)).Assigns("counter", 0)).Create();

        int counter = 7;
        mock.Object.Bump(ref counter);
        int other = 8;

        Assert.Equal(0, counter);
        Assert.Throws<FrameworkConfigurationException>(() => mock.Object.Bump(ref other));
    }

    [Fact]
    public void AnUnassignedOutIsRefusedWhenTheDoubleIsBuilt()
    {
        InlinePack<IKeyStore> pack = new(m => m.Call(s => s.TryGet("known", out MockArg.Ref<string>.Any)).Returns(true));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());

        Assert.Contains("'value' is an out parameter", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOutPositionAcceptsOnlyThePlaceholder()
    {
        string local = "ignored";

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() =>
            new InlinePack<IKeyStore>(m => m.Call(s => s.TryGet("known", out local)).Returns(true).Assigns("value", "x")).Create());

        Assert.Contains("MockArg.Ref<String>.Any", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlaceholderOutsideAByRefPositionIsRefused()
    {
        Assert.Throws<FrameworkConfigurationException>(() =>
            new InlinePack<IKeyStore>(m => m.Call(s => s.TryGet(MockArg.Ref<string>.Any, out MockArg.Ref<string>.Any))
                .Returns(true).Assigns("value", "x")).Create());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("key")]
    [InlineData("")]
    public void AssignsRefusesANameThatIsNotAnOutOrRefParameter(string name)
    {
        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() =>
            new InlinePack<IKeyStore>(m => m.Call(s => s.TryGet("known", out MockArg.Ref<string>.Any))
                .Returns(true).Assigns(name, "x")).Create());

        Assert.Contains("out String value", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignsRefusesAValueOfTheWrongType()
    {
        Assert.Throws<FrameworkConfigurationException>(() =>
            new InlinePack<IKeyStore>(m => m.Call(s => s.TryGet("known", out MockArg.Ref<string>.Any))
                .Returns(true).Assigns("value", 42)).Create());
    }

    [Fact]
    public void AssignsStatesAParameterOnce()
    {
        Assert.Throws<FrameworkConfigurationException>(() =>
            new InlinePack<IKeyStore>(m => m.Call(s => s.TryGet("known", out MockArg.Ref<string>.Any))
                .Returns(true).Assigns("value", "a").Assigns("value", "b")).Create());
    }

    [Fact]
    public void AssignsOnACallThatAlwaysThrowsIsRefused()
    {
        Assert.Throws<FrameworkConfigurationException>(() =>
            new InlinePack<IKeyStore>(m => m.Call(s => s.Bump(ref MockArg.Ref<int>.Any))
                .Throws(new InvalidOperationException("down")).Assigns("counter", 1)).Create());
    }

    [Fact]
    public void ACallThatThrowsWritesNothingBack()
    {
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.TryGet(MockArg.Any<string>(), out MockArg.Ref<string>.Any))
                .Returns((string key) => key == "bad" ? throw new InvalidOperationException("refused") : true)
                .Assigns("value", "written")).Create();

        string value = "before";
        Assert.Throws<InvalidOperationException>(() => mock.Object.TryGet("bad", out value));

        Assert.Equal("before", value);
    }

    [Fact]
    public void ACallWhoseArtifactCannotBeProducedWritesNothingBack()
    {
        // The call throws from its producer rather than its result; to the caller it is the same failed call.
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.TryGet(MockArg.Any<string>(), out MockArg.Ref<string>.Any))
                .Returns(true)
                .Assigns("value", "written")
                .ProducesArtifact((string key) => key == "bad" ? throw new InvalidOperationException("refused") : new("found", key))).Create();

        string value = "before";
        Assert.Throws<InvalidOperationException>(() => mock.Object.TryGet("bad", out value));

        Assert.Equal("before", value);
    }

    [Fact]
    public async Task ATaskReturningMethodWritesItsOutValueWhenTheCallReturns()
    {
        // An out value has to exist the moment the call returns, so it is written then - even though the task the
        // call hands back fails later.
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.TryGetAsync("known", out MockArg.Ref<string>.Any))
                .ThrowsAsync(new InvalidOperationException("later"))
                .Assigns("value", "now")).Create();

        Task<bool> pending = mock.Object.TryGetAsync("known", out string value);

        Assert.Equal("now", value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
    }

    [Fact]
    public void ARecordedOutPositionHoldsNothing_AndCountCallsMatchesThePlaceholder()
    {
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.TryGet(MockArg.Any<string>(), out MockArg.Ref<string>.Any)).Returns(true).Assigns("value", "x")).Create();

        string stale = "whatever the caller's variable held";
        mock.Object.TryGet("known", out stale);

        Assert.Null(Assert.Single(mock.RecordedCalls).Arguments[1]);
        Assert.Equal(1, mock.CountCalls(s => s.TryGet("known", out MockArg.Ref<string>.Any)));
    }

    [Fact]
    public void AnInParameterIsAnOrdinaryArgument()
    {
        MockInstance<IKeyStore> mock = new InlinePack<IKeyStore>(m =>
            m.Call(s => s.Double(MockArg.Any<int>())).Returns((int value) => value * 2)).Create();

        Assert.Equal(10, mock.Object.Double(5));
    }
}
