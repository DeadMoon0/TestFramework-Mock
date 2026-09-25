using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TestFramework.Mock.Matching;

/// <summary>
/// A method plus one matcher per argument position — the immutable "which calls does this setup
/// mean" half of a setup.
/// </summary>
internal sealed class CallPattern
{
    private readonly IReadOnlyList<IArgMatcher> _matchers;

    public CallPattern(MethodInfo method, IReadOnlyList<IArgMatcher> matchers)
    {
        this.Method = method;
        this._matchers = matchers;
    }

    public MethodInfo Method { get; }

    public bool Matches(MethodInfo method, IReadOnlyList<object?> arguments)
    {
        if (!this.Method.Equals(method) || arguments.Count != this._matchers.Count)
        {
            return false;
        }

        for (int i = 0; i < this._matchers.Count; i++)
        {
            if (!this._matchers[i].Matches(arguments[i]))
            {
                return false;
            }
        }

        return true;
    }

    public string Describe()
    {
        return $"{this.Method.Name}({string.Join(", ", this._matchers.Select(m => m.Describe()))})";
    }
}
