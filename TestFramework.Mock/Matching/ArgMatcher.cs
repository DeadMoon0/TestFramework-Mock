using System;
using System.Collections;
using System.Linq;

namespace TestFramework.Mock.Matching;

/// <summary>
/// One argument position of a call pattern: does a runtime value satisfy it, and how does it
/// describe itself in a refusal.
/// </summary>
internal interface IArgMatcher
{
    bool Matches(object? value);

    string Describe();
}

/// <summary>
/// Matches every value of the stated type. The parameter's own type bounds what can arrive, but it
/// may be wider than the stated one - <c>MockArg.Any&lt;string&gt;()</c> on an <c>object</c>
/// parameter - so the stated type is checked, not assumed.
/// </summary>
internal sealed class AnyMatcher : IArgMatcher
{
    private readonly Type _declaredType;
    private readonly Type _valueType;
    private readonly bool _acceptsNull;

    public AnyMatcher(Type declaredType)
    {
        this._declaredType = declaredType;
        Type? nullableOf = Nullable.GetUnderlyingType(declaredType);
        this._valueType = nullableOf ?? declaredType;
        this._acceptsNull = !declaredType.IsValueType || nullableOf is not null;
    }

    public bool Matches(object? value)
    {
        return value is null ? this._acceptsNull : this._valueType.IsInstanceOfType(value);
    }

    public string Describe()
    {
        return $"any {this._declaredType.Name}";
    }
}

/// <summary>
/// Matches exactly one value. Materialised collections - arrays, lists - compare by their elements in
/// order, because two equal byte arrays are the same argument to anyone reading the setup; a lazy
/// sequence keeps reference equality, since enumerating it here would consume what the system under
/// test passed.
/// </summary>
internal sealed class ExactMatcher : IArgMatcher
{
    private readonly object? _expected;

    public ExactMatcher(object? expected)
    {
        this._expected = expected;
    }

    public bool Matches(object? value)
    {
        if (this._expected is ICollection expected && value is ICollection actual)
        {
            return expected.Count == actual.Count
                && expected.Cast<object?>().SequenceEqual(actual.Cast<object?>());
        }

        return Equals(this._expected, value);
    }

    public string Describe()
    {
        return MockValueText.Describe(this._expected);
    }
}
