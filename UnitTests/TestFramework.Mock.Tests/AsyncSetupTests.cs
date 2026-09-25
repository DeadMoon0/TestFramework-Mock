using System;
using System.Threading.Tasks;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Tests.Fixtures;

namespace TestFramework.Mock.Tests;

/// <summary>
/// The async verbs behave the way a real async dependency does, and a declared artifact belongs to an
/// async call only once its task has finished successfully.
/// </summary>
public class AsyncSetupTests
{
    [Fact]
    public async Task ReturnsAsync_AndCompletes_AnswerEveryTaskShape()
    {
        MockInstance<IAsyncSink> mock = new InlinePack<IAsyncSink>(m =>
        {
            m.Call(s => s.GetAsync()).ReturnsAsync(5);
            m.Call(s => s.GetValueAsync()).ReturnsAsync(6);
            m.Call(s => s.SendAsync(MockArg.Any<string>())).Completes();
            m.Call(s => s.SendValueAsync()).Completes();
        }).Create();

        Assert.Equal(5, await mock.Object.GetAsync());
        Assert.Equal(6, await mock.Object.GetValueAsync());
        await mock.Object.SendAsync("x");
        await mock.Object.SendValueAsync();
    }

    [Fact]
    public async Task ThrowsAsync_HandsBackAFailedTask_InsteadOfThrowingAtTheCall()
    {
        // Damage with Throws on an async method: the exception arrives at the call itself, which no real
        // async method does - code that starts several calls before awaiting them behaves differently.
        InvalidOperationException stated = new("unavailable");
        MockInstance<IAsyncSink> mock = new InlinePack<IAsyncSink>(m =>
        {
            m.Call(s => s.GetAsync()).ThrowsAsync(stated);
            m.Call(s => s.GetValueAsync()).ThrowsAsync(stated);
            m.Call(s => s.SendAsync(MockArg.Any<string>())).ThrowsAsync(stated);
            m.Call(s => s.SendValueAsync()).ThrowsAsync(stated);
        }).Create();

        Task<int> get = mock.Object.GetAsync();
        ValueTask<int> getValue = mock.Object.GetValueAsync();
        Task send = mock.Object.SendAsync("x");
        ValueTask sendValue = mock.Object.SendValueAsync();

        Assert.Same(stated, await Assert.ThrowsAsync<InvalidOperationException>(() => get));
        Assert.Same(stated, await Assert.ThrowsAsync<InvalidOperationException>(async () => await getValue));
        Assert.Same(stated, await Assert.ThrowsAsync<InvalidOperationException>(() => send));
        Assert.Same(stated, await Assert.ThrowsAsync<InvalidOperationException>(async () => await sendValue));
    }

    [Fact]
    public void ThrowsAsyncWithProducesArtifact_IsRefused_TheArtifactCouldNeverHappen()
    {
        InlinePack<IAsyncSink> pack = new(m =>
            m.Call(s => s.SendAsync(MockArg.Any<string>()))
                .ThrowsAsync(new InvalidOperationException("unavailable"))
                .ProducesArtifact((string what) => new("sent", what)));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("always fails", refusal.Message);
    }

    [Fact]
    public void AnAsyncCallWhoseTaskFailsLater_NeverPublishesItsDeclaredArtifact()
    {
        // Damage without it: the artifact was published the moment the method returned its task, so an
        // operation that went on to fail still left evidence that it had succeeded.
        TaskCompletionSource pending = new();
        MockInstance<IAsyncSink> mock = new InlinePack<IAsyncSink>(m =>
            m.Call(s => s.SendAsync(MockArg.Any<string>()))
                .Returns(pending.Task)
                .ProducesArtifact((string what) => new("sent", what))).Create();

        Task send = mock.Object.SendAsync("mail");
        Assert.False(mock.TryGetLatestPublish("sent", out _));

        pending.SetException(new InvalidOperationException("the send failed"));
        Assert.True(send.IsFaulted);
        Assert.False(mock.TryGetLatestPublish("sent", out _));
    }

    [Fact]
    public void AnAsyncCallWhoseTaskSucceedsLater_PublishesWhenItFinishes()
    {
        TaskCompletionSource pending = new();
        MockInstance<IAsyncSink> mock = new InlinePack<IAsyncSink>(m =>
            m.Call(s => s.SendAsync(MockArg.Any<string>()))
                .Returns(pending.Task)
                .ProducesArtifact((string what) => new("sent", what))).Create();

        mock.Object.SendAsync("mail");
        Assert.False(mock.TryGetLatestPublish("sent", out _));

        pending.SetResult();
        Assert.True(mock.TryGetLatestPublish("sent", out object? payload));
        Assert.Equal("mail", payload);
    }
}
