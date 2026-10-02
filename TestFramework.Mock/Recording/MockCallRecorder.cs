using System;
using System.Collections.Generic;
using System.Reflection;

using TestFramework.Core.Runner;
using TestFramework.Core.Steps;

namespace TestFramework.Mock.Recording;

/// <summary>
/// The invocation log of one mock instance. Thread-safe, and a <see cref="RunRecord{T}"/> underneath, so it
/// finishes when its run does - not when the mock host is torn down - and is a snapshot from then on.
/// </summary>
/// <remarks>
/// A call that arrives after the run finished is not recorded, and the double still answers it. The system under
/// test calls its dependencies from whatever thread it likes, and a refusal thrown on a background thread nobody
/// observes can take the whole test process down for the sake of an entry nobody can assert on any more. The run's
/// gate reports the dropped call in the run's log instead.
/// </remarks>
internal sealed class MockCallRecorder
{
    private readonly object _gate = new();
    private readonly RunRecord<RecordedCall> _calls;

    public MockCallRecorder(RunContext context)
    {
        this._calls = RunRecord<RecordedCall>.Open(context, "mock calls");
    }

    public void Record(MethodInfo method, IReadOnlyList<object?> arguments, bool matched)
    {
        // Copied and wrapped, so the finished log cannot be edited through a cast back to the array the
        // interceptor built. Under one lock, so a call's sequence number and its place in the log agree.
        lock (this._gate)
        {
            this._calls.Append(new RecordedCall(method, Array.AsReadOnly([.. arguments]), matched, this._calls.Count + 1));
        }
    }

    public IReadOnlyList<RecordedCall> Snapshot()
    {
        return this._calls.Snapshot();
    }
}
