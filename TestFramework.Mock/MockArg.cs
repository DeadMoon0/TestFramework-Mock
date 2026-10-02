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

    /// <summary>
    /// The placeholder for an <c>out</c> or <c>ref</c> position:
    /// <c>mock.Call(s =&gt; s.TryGet("key", out MockArg.Ref&lt;string&gt;.Any))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A field, not a method like <see cref="Any{T}"/>, because C# accepts only a variable in an <c>out</c> or
    /// <c>ref</c> position, and inside an expression tree only a field can be that variable - <c>out _</c> and
    /// <c>out var</c> are refused by the compiler there.
    /// </para>
    /// <para>
    /// At an <c>out</c> position it is the only accepted form: an out argument brings no value, so there is
    /// nothing to match. At a <c>ref</c> position it matches any incoming value. What the call writes back is
    /// stated with <c>Assigns</c> on the setup. The field is only read while a setup is declared, never written,
    /// and is meaningless anywhere else.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The parameter's type, without the <c>out</c> or <c>ref</c>.</typeparam>
    public static class Ref<T>
    {
        /// <summary>
        /// Stands in an <c>out</c> or <c>ref</c> position of a <c>Call</c> expression.
        /// </summary>
#pragma warning disable CA2211 // Only a field can stand in a by-ref position of an expression tree; see the type's remarks.
        public static T Any = default!;
#pragma warning restore CA2211
    }
}
