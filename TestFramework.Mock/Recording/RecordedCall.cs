using System.Collections.Generic;
using System.Reflection;

namespace TestFramework.Mock.Recording;

/// <summary>
/// One invocation the mock received: which method, with what arguments, in what order across the
/// instance — and whether any setup matched it. Unmatched calls are recorded before they are
/// refused, because the call that nothing expected is usually the interesting one.
/// </summary>
/// <param name="Method">The interface method that was called.</param>
/// <param name="Arguments">The arguments as they arrived, snapshotted.</param>
/// <param name="Matched">Whether exactly one setup answered the call; false for unmatched and
/// for ambiguous calls, both of which are refused after being recorded.</param>
/// <param name="Sequence">Call order across the instance; strictly increasing.</param>
public sealed record RecordedCall(MethodInfo Method, IReadOnlyList<object?> Arguments, bool Matched, int Sequence);
