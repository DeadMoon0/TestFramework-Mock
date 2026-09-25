namespace TestFramework.Mock;

/// <summary>
/// Argument matchers for use inside a <c>mock.Call(...)</c> expression.
/// </summary>
public static class MockArg
{
    /// <summary>
    /// Matches any value of <typeparamref name="T"/> — null too, where <typeparamref name="T"/> can be
    /// null — and nothing of another type, so <c>MockArg.Any&lt;string&gt;()</c> on an <c>object</c>
    /// parameter does not answer an <c>int</c>.
    /// Only meaningful as a whole argument of a <c>Call</c> expression: the call is never executed
    /// there, it is read, and using it inside a larger argument expression is refused. Invoked as a
    /// normal method it returns <c>default</c>.
    /// </summary>
    public static T Any<T>()
    {
        return default!;
    }
}
