using System;
using System.Collections.Generic;
using System.Linq.Expressions;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// What a <see cref="MockDefinition{TService}"/> configures against. Collected setups leave only
/// through <see cref="BuildSetups"/>, which is also where a setup that returns a value but
/// states none is refused — at instance creation, before anything runs.
/// </summary>
/// <typeparam name="TService">The mocked service.</typeparam>
public sealed class MockBuilder<TService>
    where TService : class
{
    private readonly List<MockCallSetupBase> _setups = [];

    internal MockBuilder()
    {
    }

    /// <summary>
    /// Declares a setup for a value-returning call. Setups may overlap on paper, but a call that
    /// more than one setup matches is refused at call time naming them all — any ambiguity is a
    /// failure, never a precedence rule.
    /// </summary>
    public MockCallSetup<TService, TResult> Call<TResult>(Expression<Func<TService, TResult>> call)
    {
        MockCallSetup<TService, TResult> setup = new(CallPatternParser.Parse(call));
        this._setups.Add(setup);
        return setup;
    }

    /// <summary>
    /// Declares a setup for a void call.
    /// </summary>
    public MockCallSetup<TService> Call(Expression<Action<TService>> call)
    {
        MockCallSetup<TService> setup = new(CallPatternParser.Parse(call));
        this._setups.Add(setup);
        return setup;
    }

    internal IReadOnlyList<MockCallSetupBase> BuildSetups()
    {
        foreach (MockCallSetupBase setup in this._setups)
        {
            if (setup.Pattern.Method.ReturnType != typeof(void) && !setup.HasResponse)
            {
                throw new FrameworkConfigurationException(
                    $"'{setup.Pattern.Describe()}' returns {setup.Pattern.Method.ReturnType.Name} but states no result.",
                    recoverySteps: ["State Returns(...), Compute(...) or Throws(...) on the setup — a mocked call never invents a return value."]);
            }

            if (setup.DeclaresUnreachableArtifacts)
            {
                throw new FrameworkConfigurationException(
                    $"'{setup.Pattern.Describe()}' always throws but declares ProducesArtifact; the artifact could never be published.",
                    recoverySteps: ["Publish it by hand from a Compute body before throwing, or drop the declaration."]);
            }
        }

        return [.. this._setups];
    }
}
