using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// The declaration half every setup shares: one call pattern, at most one result, any number of
/// artifact producers. Instances of the derived types are handed out by
/// <see cref="MockBuilder{TService}.Call"/> and cannot be built directly.
/// </summary>
public abstract class MockCallSetupBase
{
    private readonly List<Action<object?[], MockArtifacts>> _artifactProducers = [];
    private Func<object?[], MockArtifacts, object?>? _response;

    private protected MockCallSetupBase(CallPattern pattern)
    {
        this.Pattern = pattern;
    }

    internal CallPattern Pattern { get; }

    internal bool HasResponse => this._response is not null;

    internal bool DeclaresUnreachableArtifacts => this.AlwaysThrows && this._artifactProducers.Count > 0;

    private bool AlwaysThrows { get; set; }

    internal object? Invoke(object?[] arguments, MockArtifacts artifacts)
    {
        // The result runs first: a declared artifact is what a completed call produced, so a
        // call that throws is never assumed to have produced it. What a body published by hand
        // before throwing stands — those publishes happened.
        object? result = this._response is null ? null : this._response(arguments, artifacts);
        foreach (Action<object?[], MockArtifacts> producer in this._artifactProducers)
        {
            producer(arguments, artifacts);
        }

        return result;
    }

    private protected void SetThrows(Exception exception)
    {
        this.SetResponse((_, _) => throw exception);
        this.AlwaysThrows = true;
    }

    private protected void SetResponse(Func<object?[], MockArtifacts, object?> response)
    {
        if (this._response is not null)
        {
            throw new FrameworkConfigurationException(
                $"'{this.Pattern.Describe()}' already states its result; a setup states it once.",
                recoverySteps: ["Keep one of Returns, Compute or Throws on this setup — or add a second mock.Call(...) if two behaviours are meant."]);
        }

        this._response = response;
    }

    private protected void AddArtifactProducer(Action<object?[], MockArtifacts> producer)
    {
        this._artifactProducers.Add(producer);
    }

    /// <summary>
    /// Refuses a typed lambda whose parameters do not fit the mocked method — at declaration
    /// time, so the mistake surfaces where it was written and not at the first call.
    /// </summary>
    private protected void ValidateArgumentTypes(params Type[] lambdaArgumentTypes)
    {
        ParameterInfo[] parameters = this.Pattern.Method.GetParameters();
        bool fits = parameters.Length == lambdaArgumentTypes.Length
            && parameters.Zip(lambdaArgumentTypes).All(pair => pair.Second.IsAssignableFrom(pair.First.ParameterType));
        if (!fits)
        {
            throw new FrameworkConfigurationException(
                $"The lambda on '{this.Pattern.Describe()}' takes ({string.Join(", ", lambdaArgumentTypes.Select(t => t.Name))}), which does not fit the method.",
                recoverySteps: ["Match the lambda's parameters to the mocked method's signature."],
                availableOptions: [.. parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")]);
        }
    }

    private protected static T Argument<T>(object?[] arguments, int index)
    {
        return (T)arguments[index]!;
    }
}
