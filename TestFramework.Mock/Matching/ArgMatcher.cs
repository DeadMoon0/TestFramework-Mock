using System;

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
/// Matches every value. The parameter's own type already bounds what can arrive.
/// </summary>
internal sealed class AnyMatcher : IArgMatcher
{
    private readonly Type _declaredType;

    public AnyMatcher(Type declaredType)
    {
        this._declaredType = declaredType;
    }

    public bool Matches(object? value)
    {
        return true;
    }

    public string Describe()
    {
        return $"any {this._declaredType.Name}";
    }
}

/// <summary>
/// Matches exactly one value, by equality.
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
        return Equals(this._expected, value);
    }

    public string Describe()
    {
        return this._expected switch
        {
            null => "null",
            string text => $"\"{text}\"",
            _ => this._expected.ToString() ?? this._expected.GetType().Name,
        };
    }
}
