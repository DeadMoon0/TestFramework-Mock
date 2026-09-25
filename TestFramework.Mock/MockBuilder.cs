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
    private bool _built;

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
        this.EnsureOpen();
        MockCallSetup<TService, TResult> setup = new(CallPatternParser.Parse(call));
        this._setups.Add(setup);
        return setup;
    }

    /// <summary>
    /// Declares a setup for a void call.
    /// </summary>
    public MockCallSetup<TService> Call(Expression<Action<TService>> call)
    {
        this.EnsureOpen();
        MockCallSetup<TService> setup = new(CallPatternParser.Parse(call));
        this._setups.Add(setup);
        return setup;
    }

    internal IReadOnlyList<MockCallSetupBase> BuildSetups()
    {
        this.EnsureOpen();
        this._built = true;

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

        foreach (MockCallSetupBase setup in this._setups)
        {
            setup.Seal();
        }

        return [.. this._setups];
    }

    private void EnsureOpen()
    {
        if (this._built)
        {
            throw new FrameworkStateException(
                $"A setup for {typeof(TService).Name} was declared after its double was built; it would never answer a call.",
                recoverySteps: ["Declare every setup inside Configure, and do not keep the builder beyond it."]);
        }
    }
}
