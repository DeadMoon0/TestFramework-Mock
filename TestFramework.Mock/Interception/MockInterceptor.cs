using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Castle.DynamicProxy;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Recording;

namespace TestFramework.Mock.Interception;

/// <summary>
/// The engine's one interception path: record the call, refuse by name what no setup expects —
/// and equally what more than one setup expects, because any ambiguity is a failure — then run
/// the one answering setup. The frozen rule is the recorder's alone — a frozen instance refuses
/// there, so this class never re-states it.
/// </summary>
internal sealed class MockInterceptor : IInterceptor
{
    private readonly IReadOnlyList<MockCallSetupBase> _setups;
    private readonly MockCallRecorder _recorder;
    private readonly MockArtifacts _artifacts;

    public MockInterceptor(IReadOnlyList<MockCallSetupBase> setups, MockCallRecorder recorder, MockArtifacts artifacts)
    {
        this._setups = setups;
        this._recorder = recorder;
        this._artifacts = artifacts;
    }

    public void Intercept(IInvocation invocation)
    {
        object?[] arguments = [.. invocation.Arguments];
        List<MockCallSetupBase> matches =
            [.. this._setups.Where(setup => setup.Pattern.Matches(invocation.Method, arguments))];
        this._recorder.Record(invocation.Method, arguments, matched: matches.Count == 1);

        if (matches.Count == 0)
        {
            throw new FrameworkConfigurationException(
                $"No setup matches '{Describe(invocation.Method, arguments)}'.",
                recoverySteps: ["Add a mock.Call(...) for this call to the definition, or widen an existing matcher."],
                availableOptions: [.. this._setups.Select(setup => setup.Pattern.Describe())]);
        }

        if (matches.Count > 1)
        {
            throw new FrameworkConfigurationException(
                $"'{Describe(invocation.Method, arguments)}' matches more than one setup; two candidates for one call is a stated error, never a coin toss.",
                recoverySteps: ["Narrow the overlapping matchers so exactly one setup can answer this call."],
                availableOptions: [.. matches.Select(setup => setup.Pattern.Describe())]);
        }

        invocation.ReturnValue = matches[0].Invoke(arguments, this._artifacts);
    }

    private static string Describe(MethodInfo method, IReadOnlyList<object?> arguments)
    {
        IEnumerable<string> rendered = arguments.Select(argument => argument switch
        {
            null => "null",
            string text => $"\"{text}\"",
            _ => argument.ToString() ?? argument.GetType().Name,
        });
        return $"{method.Name}({string.Join(", ", rendered)})";
    }
}
