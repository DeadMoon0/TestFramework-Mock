using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using Castle.DynamicProxy;

using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Interception;
using TestFramework.Mock.Matching;
using TestFramework.Mock.Recording;

namespace TestFramework.Mock;

/// <summary>
/// One run's live double: the proxy the system under test receives, the invocation log, and what
/// its calls published. Freezes with the run — after that the log is a trustworthy
/// snapshot and the proxy refuses further calls by name.
/// </summary>
/// <typeparam name="TService">The mocked service.</typeparam>
public sealed class MockInstance<TService> : MockInstanceGeneric
    where TService : class
{
    // Castle caches generated proxy types per generator, so the family shares one.
    private static readonly ProxyGenerator _proxyGenerator = new();

    private readonly MockCallRecorder _recorder = new();
    private readonly MockArtifactRecord _artifactRecord = new();

    internal MockInstance(IReadOnlyList<MockCallSetupBase> setups)
    {
        MockInterceptor interceptor = new(setups, this._recorder, new MockArtifacts(this._artifactRecord));
        this.Object = (TService)_proxyGenerator.CreateInterfaceProxyWithoutTarget(typeof(TService), interceptor);
    }

    /// <summary>
    /// The proxy to hand to the system under test.
    /// </summary>
    public TService Object { get; }

    internal sealed override Type ServiceType => typeof(TService);

    internal sealed override object ProxyObject => this.Object;

    /// <summary>
    /// Every invocation the double received so far, in call order — unmatched ones included.
    /// </summary>
    public IReadOnlyList<RecordedCall> RecordedCalls => this._recorder.Snapshot();

    internal sealed override bool TryGetLatestPublish(string identity, out object? payload)
    {
        return this._artifactRecord.TryGetLatest(identity, out payload);
    }

    /// <summary>
    /// Counts the recorded calls a pattern matches — the verification read.
    /// </summary>
    public int CountCalls<TResult>(Expression<Func<TService, TResult>> call)
    {
        return this.Count(CallPatternParser.Parse(call));
    }

    /// <summary>
    /// Counts the recorded calls a void pattern matches.
    /// </summary>
    public int CountCalls(Expression<Action<TService>> call)
    {
        return this.Count(CallPatternParser.Parse(call));
    }

    /// <summary>
    /// Closes the double with its run: the log becomes immutable and further calls are refused.
    /// Internal for the same reason Core's run-end freezing is: reachable from outside, it would
    /// let a caller freeze a running run's double and stop its system under test mid-flight.
    /// </summary>
    internal sealed override void FreezeForRunEnd()
    {
        this._recorder.FreezeForRunEnd();
        this._artifactRecord.FreezeForRunEnd();
    }

    private int Count(CallPattern pattern)
    {
        return this.RecordedCalls.Count(call => pattern.Matches(call.Method, call.Arguments));
    }
}
