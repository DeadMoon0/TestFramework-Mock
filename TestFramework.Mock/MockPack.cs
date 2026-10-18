using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;

namespace TestFramework.Mock;

/// <summary>
/// One immutable declaration of a test double — the pack. A pack says nothing about
/// which run it serves: it is instantiated per run, so recordings and published artifacts are
/// run-isolated and parallel runs never share state.
/// </summary>
/// <typeparam name="TService">The service the pack replaces.</typeparam>
public abstract class MockPack<TService> : MockPackGeneric
    where TService : class
{
    internal sealed override Type ServiceType => typeof(TService);

    /// <summary>
    /// States every call this double answers. Runs once per instance; what it declares is
    /// frozen from then on.
    /// </summary>
    protected abstract void Configure(MockBuilder<TService> mock);

    internal sealed override MockInstanceGeneric CreateInstanceGeneric(RunContext context)
    {
        return this.CreateInstance(context);
    }

    /// <summary>
    /// Refuses a pack that could never become a double, while it is being included rather than when a run
    /// starts: a service the engine cannot proxy, and a <see cref="Configure"/> whose setups contradict
    /// themselves.
    /// </summary>
    /// <remarks>
    /// <see cref="Configure"/> runs here on this throwaway instance, against a builder that is thrown away
    /// with it. Every run still configures a pack of its own, so nothing declared here reaches a run.
    /// </remarks>
    internal sealed override void ValidateShape()
    {
        if (!typeof(TService).IsInterface)
        {
            throw new FrameworkConfigurationException(
                $"'{typeof(TService).Name}' is not an interface; the engine proxies interfaces only for now.")
            {
                RecoverySteps = ["Mock the service's interface instead.", "Class mocking (virtual members) is a later stage of the engine."],
            };
        }

        if (!typeof(TService).IsVisible && !GrantsTheProxyGenerator(typeof(TService).Assembly))
        {
            throw new FrameworkConfigurationException(
                $"'{typeof(TService).Name}' is not public, so the engine cannot generate a double for it.")
            {
                RecoverySteps =
                [
                    "Make the interface public, or",
                    $"grant the proxy generator access in {typeof(TService).Assembly.GetName().Name}: [assembly: InternalsVisibleTo(\"DynamicProxyGenAssembly2\")].",
                ],
            };
        }

        MockBuilder<TService> builder = new();
        this.Configure(builder);
        _ = builder.BuildSetups();
    }

    /// <summary>
    /// One run's live double. Internal: instances are the engine's to create, per run, never a test body's.
    /// </summary>
    internal MockInstance<TService> CreateInstance(RunContext context)
    {
        MockBuilder<TService> builder = new();
        this.Configure(builder);
        return new MockInstance<TService>(builder.BuildSetups(), context);
    }

    private static bool GrantsTheProxyGenerator(Assembly assembly)
        => assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Any(static grant => grant.AssemblyName.Split(',')[0].Trim() == "DynamicProxyGenAssembly2");
}
