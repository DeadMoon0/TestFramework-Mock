using System.Linq;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Recording;
using TestFramework.Mock.Tests.Fixtures;

namespace TestFramework.Mock.Tests;

public class RecordingAndFreezeTests
{
    [Fact]
    public void EveryCallIsRecorded_InOrder_WithItsArguments()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true)).Create();

        mock.Object.CreateFile("first.txt");
        mock.Object.CreateFile("second.txt");

        Assert.Equal(2, mock.RecordedCalls.Count);
        Assert.Equal(new object?[] { "first.txt" }, mock.RecordedCalls[0].Arguments);
        Assert.Equal(new object?[] { "second.txt" }, mock.RecordedCalls[1].Arguments);
        Assert.Equal(new[] { 1, 2 }, mock.RecordedCalls.Select(call => call.Sequence).ToArray());
    }

    [Fact]
    public void AnUnmatchedCall_IsRecordedBeforeItIsRefused()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile("known.txt")).Returns(true)).Create();

        Assert.Throws<FrameworkConfigurationException>(() => mock.Object.CreateFile("unknown.txt"));

        RecordedCall recorded = Assert.Single(mock.RecordedCalls);
        Assert.False(recorded.Matched);
        Assert.Equal(new object?[] { "unknown.txt" }, recorded.Arguments);
    }

    [Fact]
    public void CountCalls_CountsOnlyWhatThePatternMatches()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true)).Create();

        mock.Object.CreateFile("a.txt");
        mock.Object.CreateFile("b.txt");
        mock.Object.CreateFile("a.txt");

        Assert.Equal(3, mock.CountCalls(f => f.CreateFile(MockArg.Any<string>())));
        Assert.Equal(2, mock.CountCalls(f => f.CreateFile("a.txt")));
        Assert.Equal(0, mock.CountCalls(f => f.ReadText(MockArg.Any<string>())));
    }

    [Fact]
    public void AFrozenMock_RefusesFurtherCalls_AndKeepsItsLog()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(true)).Create();
        mock.Object.CreateFile("before.txt");

        mock.FreezeForRunEnd();

        FrameworkStateException refusal = Assert.Throws<FrameworkStateException>(() => mock.Object.CreateFile("after.txt"));
        Assert.Contains("finished", refusal.Message);
        Assert.Single(mock.RecordedCalls);
    }
}
