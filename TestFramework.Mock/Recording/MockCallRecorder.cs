using System.Collections.Generic;
using System.Reflection;

using TestFramework.Core.Exceptions;

namespace TestFramework.Mock.Recording;

/// <summary>
/// The invocation log of one mock instance. Thread-safe; freezes with the run, after which the
/// log is a snapshot that can be trusted the way everything else a finished run exposes can.
/// </summary>
internal sealed class MockCallRecorder
{
    private readonly object _gate = new();
    private readonly List<RecordedCall> _calls = [];
    private bool _frozen;

    public RecordedCall Record(MethodInfo method, IReadOnlyList<object?> arguments, bool matched)
    {
        lock (this._gate)
        {
            if (this._frozen)
            {
                throw new FrameworkStateException(
                    $"The run is finished; the mock no longer accepts calls ('{method.Name}' arrived after freeze).",
                    recoverySteps: ["Make closing whatever still calls this dependency a step of the timeline, so it happens before the run ends."]);
            }

            RecordedCall call = new(method, arguments, matched, this._calls.Count + 1);
            this._calls.Add(call);
            return call;
        }
    }

    public IReadOnlyList<RecordedCall> Snapshot()
    {
        lock (this._gate)
        {
            return [.. this._calls];
        }
    }

    public void FreezeForRunEnd()
    {
        lock (this._gate)
        {
            this._frozen = true;
        }
    }
}
