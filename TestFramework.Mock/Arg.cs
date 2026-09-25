using System;

namespace TestFramework.Mock;

/// <summary>
/// Argument matchers for use inside a <c>mock.Call(...)</c> expression.
/// </summary>
public static class Arg
{
    /// <summary>
    /// Matches any value of <typeparamref name="T"/>, including null.
    /// Only meaningful inside a <c>Call</c> expression — the call is never executed there, it is
    /// read. Invoked as a normal method it returns <c>default</c> and matches nothing.
    /// </summary>
    public static T Any<T>()
    {
        return default!;
    }
}
