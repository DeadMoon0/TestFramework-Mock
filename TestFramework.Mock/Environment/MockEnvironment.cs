using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;

namespace TestFramework.Mock;

/// <summary>
/// The environment that hosts the system under test in-process: the user's own service
/// composition, with each included Mock-Pack's double standing where the real dependency would.
/// The host is created per run, so recordings, published artifacts and the composed provider are
/// run-isolated and parallel runs never share state.
/// </summary>
public sealed class MockEnvironment : EnvironmentProviderBase, IResourceNodeSource
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
        this.MapDeclaredResources(MockResourceKinds.Host, host.Id);
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

    /// <summary>
    /// What this environment is called on the run's resource list.
    /// </summary>
    public string SourceName => "mock host";

    /// <summary>
    /// Every service the hosted composition can hand a <c>Host</c> step, as a resource on the run's list -
    /// so a step calling a service nothing registers is refused before the run starts, naming what is
    /// registered, and a run without this environment is refused the same way.
    /// </summary>
    /// <remarks>
    /// Composed afresh for each read, from the same registrations every run hosts. A keyed-only
    /// registration is left out, because a <c>Host</c> step resolves its service without a key; an open
    /// generic registration is declared under its definition's name, which a <c>Host</c> step for a closed
    /// type of it requires.
    /// </remarks>
    public IReadOnlyList<ResourceNode> Nodes => new HostedServices(this).Nodes;

    private sealed class HostedServices(MockEnvironment environment) : DeclaredNodeSource
    {
        public override string SourceName => environment.SourceName;

        protected override IEnumerable<DeclaredResource> Declarations
        {
            get
            {
                ServiceCollection services = new();
                environment.ComposeServices(services);

                IEnumerable<Type> serviceTypes = services
                    .Where(static descriptor => !descriptor.IsKeyedService)
                    .Select(static descriptor => descriptor.ServiceType)
                    .Concat(environment.PackServiceTypes());

                foreach (string name in serviceTypes.Select(MockHost.ResourceName).Distinct(StringComparer.Ordinal).OrderBy(static name => name, StringComparer.Ordinal))
                {
                    yield return new DeclaredResource(MockResourceKinds.HostKind, name, new Dictionary<ValueKey, string>(), $"mock host service '{name}'");
                }
            }
        }
    }

    private IReadOnlyList<Type> PackServiceTypes()
    {
        lock (this._gate)
        {
            return [.. this._packTypeByService.Keys];
        }
    }
}
