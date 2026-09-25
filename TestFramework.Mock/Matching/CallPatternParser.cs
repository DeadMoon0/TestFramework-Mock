using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

using TestFramework.Core.Exceptions;

namespace TestFramework.Mock.Matching;

/// <summary>
/// Reads a <c>m =&gt; m.Method(...)</c> expression into a <see cref="CallPattern"/>. An
/// <see cref="Arg"/> call becomes a matcher; every other argument expression is evaluated once,
/// here, and becomes an exact match — so a setup captures values at declaration time, not at
/// call time.
/// </summary>
internal static class CallPatternParser
{
    public static CallPattern Parse(LambdaExpression call)
    {
        if (call.Body is not MethodCallExpression methodCall
            || !ReferenceEquals(methodCall.Object, call.Parameters[0]))
        {
            throw new FrameworkConfigurationException(
                $"A setup must be a single direct method call on the mocked service; '{call.Body}' is not one.",
                recoverySteps: ["Write the setup as mock.Call(m => m.Method(...)), with Arg matchers or values as arguments."]);
        }

        List<IArgMatcher> matchers = [];
        ParameterInfo[] parameters = methodCall.Method.GetParameters();
        for (int i = 0; i < methodCall.Arguments.Count; i++)
        {
            matchers.Add(ParseArgument(methodCall.Arguments[i], parameters[i]));
        }

        return new CallPattern(methodCall.Method, matchers);
    }

    private static IArgMatcher ParseArgument(Expression argument, ParameterInfo parameter)
    {
        if (argument is MethodCallExpression call
            && call.Method.DeclaringType == typeof(Arg)
            && call.Method.Name == nameof(Arg.Any))
        {
            return new AnyMatcher(call.Method.GetGenericArguments()[0]);
        }

        object? value = Expression.Lambda(argument).Compile().DynamicInvoke();
        return new ExactMatcher(value);
    }
}
