using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
using TestFramework.Core.Exceptions;

namespace TestFramework.Mock;

/// <summary>
/// The environment that hosts the system under test in-process: the user's own service
/// composition, with each included Mock-Pack's double standing where the real dependency would.
/// The host is created per run, so recordings, published artifacts and the composed provider are
/// run-isolated and parallel runs never share state.
/// </summary>
public sealed class MockEnvironment : EnvironmentProviderBase
{
    private readonly Action<IServiceCollection> _composeServices;
    private readonly object _gate = new();
    private readonly Dictionary<Type, Type> _packTypeByService = [];
    private readonly List<Func<MockDefinitionGeneric>> _packFactories = [];
    private bool _sealed;

    private MockEnvironment(Action<IServiceCollection> composeServices)
    {
        this._composeServices = composeServices;
        MockHostEnvComponent host = new(this);
        this.AddComponent(host);
        this.MapResourceKind(MockResourceKinds.Host, host.Id);
    }

    /// <summary>
    /// States how the system under test composes its services — the same registrations
    /// production makes, handed over rather than rebuilt, so the test hosts what actually ships.
    /// </summary>
    public static MockEnvironment For(Action<IServiceCollection> composeServices)
    {
        ArgumentNullException.ThrowIfNull(composeServices);

        return new MockEnvironment(composeServices);
    }

    /// <summary>
    /// Includes one Mock-Pack. Including the same pack again is idempotent; a second pack
    /// replacing the same service is refused naming both, because a container would hand out the
    /// later one and the earlier would vanish silently. Every pack is included before the first run
    /// that uses this environment; after that the environment is sealed, so every run hosts the same
    /// declaration.
    /// </summary>
    public MockEnvironment Include<TPack>()
        where TPack : MockDefinitionGeneric, new()
    {
        Type serviceType = new TPack().ServiceType;

        lock (this._gate)
        {
            this._packTypeByService.TryGetValue(serviceType, out Type? existing);
            if (existing == typeof(TPack))
            {
                return this;
            }

            if (this._sealed)
            {
                throw new FrameworkConfigurationException(
                    $"'{typeof(TPack).Name}' was included after a run had already used this environment.",
                    recoverySteps: ["Include every pack while building the environment, before the first run is set up with it."],
                    availableOptions: [.. this._packTypeByService.Values.Select(type => type.Name)]);
            }

            if (existing is not null)
            {
                throw new FrameworkConfigurationException(
                    $"Two packs replace '{serviceType.Name}': '{existing.Name}' and '{typeof(TPack).Name}'.",
                    recoverySteps: ["Keep one pack per service; merge the setups into one definition if both behaviours are meant."]);
            }

            this._packTypeByService[serviceType] = typeof(TPack);

            // A fresh pack per run, never one shared object: Configure runs once per run, and a field a
            // pack author reaches for would otherwise carry one run's state into the next - and race
            // between parallel runs.
            this._packFactories.Add(static () => new TPack());
        }

        return this;
    }

    /// <summary>
    /// This run's packs, each newly made. Seals the environment: from the first run on, what it hosts
    /// cannot change underneath the runs that use it.
    /// </summary>
    internal IReadOnlyList<MockDefinitionGeneric> CreatePacksForRun()
    {
        lock (this._gate)
        {
            this._sealed = true;
            return [.. this._packFactories.Select(static create => create())];
        }
    }

    internal void ComposeServices(IServiceCollection services)
    {
        this._composeServices(services);
    }
}
