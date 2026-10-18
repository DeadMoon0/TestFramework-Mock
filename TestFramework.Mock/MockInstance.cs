using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

using Castle.DynamicProxy;

using TestFramework.Core.Steps;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Interception;
using TestFramework.Mock.Matching;
using TestFramework.Mock.Recording;

namespace TestFramework.Mock;

/// <summary>
/// One run's live double: the proxy the system under test receives, the invocation log, and what
/// its calls published. When the run finishes the log is closed: a later call is still answered,
/// but not recorded, and the run's log says so.
/// </summary>
/// <typeparam name="TService">The mocked service.</typeparam>
public sealed class MockInstance<TService> : MockInstanceGeneric
    where TService : class
{

    private readonly MockCallRecorder _recorder;
    private readonly MockArtifactRecord _artifactRecord;

    internal MockInstance(IReadOnlyList<MockCallSetupBase> setups, RunContext context)
    {
        this._recorder = new MockCallRecorder(context);
        this._artifactRecord = new MockArtifactRecord(context);
        MockInterceptor interceptor = new(setups, this._recorder, new MockArtifacts(this._artifactRecord));
        this.Object = (TService)MockProxies.Generator.CreateInterfaceProxyWithoutTarget(typeof(TService), interceptor);
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

    private int Count(CallPattern pattern)
    {
        return this.RecordedCalls.Count(call => pattern.Matches(call.Method, call.Arguments));
    }
}
