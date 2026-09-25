using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

using TestFramework.Core.Exceptions;

namespace TestFramework.Mock.Matching;

/// <summary>
/// Reads a <c>m =&gt; m.Method(...)</c> expression into a <see cref="CallPattern"/>. A
/// <see cref="MockArg"/> call standing as a whole argument becomes a matcher; every other argument
/// expression is evaluated once, here, and becomes an exact match — so a setup captures values at
/// declaration time, not at call time.
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
                recoverySteps: ["Write the setup as mock.Call(m => m.Method(...)), with MockArg matchers or values as arguments."]);
        }

        List<IArgMatcher> matchers = [];
        foreach (Expression argument in methodCall.Arguments)
        {
            matchers.Add(ParseArgument(argument));
        }

        return new CallPattern(methodCall.Method, matchers);
    }

    private static IArgMatcher ParseArgument(Expression argument)
    {
        // Implicit conversions to the parameter's type wrap the argument (an int passed where an
        // object is taken), so the matcher is looked for inside them.
        Expression unwrapped = argument;
        while (unwrapped is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } conversion)
        {
            unwrapped = conversion.Operand;
        }

        if (unwrapped is MethodCallExpression call && IsMatcher(call))
        {
            return new AnyMatcher(call.Method.GetGenericArguments()[0]);
        }

        // Anywhere else, a matcher would be evaluated once like any other value and quietly become
        // an exact match on default(T) - "MockArg.Any<int>() + 1" would mean "exactly 1".
        if (MatcherFinder.Contains(argument))
        {
            throw new FrameworkConfigurationException(
                $"MockArg is used inside the argument '{argument}', where it would be evaluated once to a fixed value instead of matching.",
                recoverySteps: ["Use MockArg.Any<T>() as a whole argument, or state the exact value the argument must have."]);
        }

        object? value = Expression.Lambda(argument).Compile().DynamicInvoke();
        return new ExactMatcher(value);
    }

    private static bool IsMatcher(MethodCallExpression call)
    {
        return call.Method.DeclaringType == typeof(MockArg);
    }

    private sealed class MatcherFinder : ExpressionVisitor
    {
        private bool _found;

        public static bool Contains(Expression expression)
        {
            MatcherFinder finder = new();
            finder.Visit(expression);
            return finder._found;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            this._found |= IsMatcher(node);
            return base.VisitMethodCall(node);
        }
    }
}
