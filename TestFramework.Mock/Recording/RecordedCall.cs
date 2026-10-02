using System.Collections.Generic;
using System.Reflection;

namespace TestFramework.Mock.Recording;

/// <summary>
/// One invocation the mock received: which method, with what arguments, in what order across the
/// instance — and whether any setup matched it. Unmatched calls are recorded before they are
/// refused, because the call that nothing expected is usually the interesting one.
/// </summary>
/// <remarks>
/// Built only by the recorder, so its constructor is internal and its members are properties: what a call carries
/// is the record most likely to grow - its timing, the thread it arrived on - and a positional record would have
/// broken every compiled reader the day it did.
/// </remarks>
public sealed record RecordedCall
{
    internal RecordedCall(MethodInfo method, IReadOnlyList<object?> arguments, bool matched, int sequence)
    {
        this.Method = method;
        this.Arguments = arguments;
        this.Matched = matched;
        this.Sequence = sequence;
    }

    /// <summary>
    /// The interface method that was called.
    /// </summary>
    public MethodInfo Method { get; }

    /// <summary>
    /// The arguments as they arrived, snapshotted into a list nothing can change.
    /// </summary>
    public IReadOnlyList<object?> Arguments { get; }

    /// <summary>
    /// Whether exactly one setup answered the call; false for unmatched and for ambiguous calls, both of which are
    /// refused after being recorded.
    /// </summary>
    public bool Matched { get; }

    /// <summary>
    /// Call order across the instance; strictly increasing.
    /// </summary>
    public int Sequence { get; }
}
