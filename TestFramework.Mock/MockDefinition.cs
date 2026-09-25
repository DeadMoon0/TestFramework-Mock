using System;

using TestFramework.Core.Exceptions;

namespace TestFramework.Mock;

/// <summary>
/// One immutable declaration of a test double — the Mock-Pack. A definition says nothing about
/// which run it serves: it is instantiated per run, so recordings and published artifacts are
/// run-isolated and parallel runs never share state.
/// </summary>
/// <typeparam name="TService">The service the pack replaces.</typeparam>
public abstract class MockDefinition<TService> : MockDefinitionGeneric
    where TService : class
{
    internal sealed override Type ServiceType => typeof(TService);

    /// <summary>
    /// States every call this double answers. Runs once per instance; what it declares is
    /// frozen from then on.
    /// </summary>
    protected abstract void Configure(MockBuilder<TService> mock);

    internal sealed override MockInstanceGeneric CreateInstanceGeneric()
    {
        return this.CreateInstance();
    }

    /// <summary>
    /// One run's live double. Internal: instances are the engine's to create — later the
    /// environment's, per run — never a test body's.
    /// </summary>
    internal MockInstance<TService> CreateInstance()
    {
        if (!typeof(TService).IsInterface)
        {
            throw new FrameworkConfigurationException(
                $"'{typeof(TService).Name}' is not an interface; the engine proxies interfaces only for now.",
                recoverySteps: ["Mock the service's interface instead.", "Class mocking (virtual members) is a later stage of the engine."]);
        }

        MockBuilder<TService> builder = new();
        this.Configure(builder);
        return new MockInstance<TService>(builder.BuildSetups());
    }
}
