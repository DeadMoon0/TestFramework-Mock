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
    private readonly Dictionary<Type, IncludedPack> _packsByService = [];
    private readonly List<Func<MockDefinitionGeneric>> _packFactories = [];
    private IReadOnlyList<ResourceNode>? _declaredNodes;
    private IReadOnlySet<Type>? _declaredServiceTypes;
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
    /// <remarks>
    /// The delegate runs once to declare what the environment offers, and again for every run, so each run
    /// gets instances of its own. It must register the same services each time; a run whose composition
    /// registers a different set is refused, because the run was planned against the first.
    /// </remarks>
    public static MockEnvironment For(Action<IServiceCollection> composeServices)
    {
        ArgumentNullException.ThrowIfNull(composeServices);

        return new MockEnvironment(composeServices);
    }

    /// <summary>
    /// Includes one Mock-Pack, standing in for a service the composition registers. Including the same
    /// pack again is idempotent; a second pack replacing the same service is refused naming both, because
    /// a container would hand out the later one and the earlier would vanish silently. Every pack is
    /// included before the first run that uses this environment; after that the environment is sealed, so
    /// every run hosts the same declaration.
    /// </summary>
    /// <remarks>
    /// A pack whose service the composition never registers is refused when the run is planned: it would
    /// stand in for nothing, every call count would read zero, and a renamed dependency would never be
    /// noticed. When the double is meant to add a service the composition lacks, include it with
    /// <see cref="IncludeAdditional{TPack}"/>.
    /// </remarks>
    public MockEnvironment Include<TPack>()
        where TPack : MockDefinitionGeneric, new()
        => this.Add<TPack>(additional: false);

    /// <summary>
    /// Includes one Mock-Pack for a service the composition does not register, adding the double as that
    /// service. Everything else is as <see cref="Include{TPack}"/>.
    /// </summary>
    /// <remarks>
    /// Refused when the composition does register the service: the pack then replaces a registration, which
    /// is what <see cref="Include{TPack}"/> says.
    /// </remarks>
    public MockEnvironment IncludeAdditional<TPack>()
        where TPack : MockDefinitionGeneric, new()
        => this.Add<TPack>(additional: true);

    private MockEnvironment Add<TPack>(bool additional)
        where TPack : MockDefinitionGeneric, new()
    {
        TPack probe = new();

        // Here rather than when a run starts: everything the shape check needs is known now, and a refusal at
        // the line that included the pack points at the line to change.
        probe.ValidateShape();
        Type serviceType = probe.ServiceType;

        lock (this._gate)
        {
            this._packsByService.TryGetValue(serviceType, out IncludedPack? existing);
            if (existing is not null && existing.PackType == typeof(TPack) && existing.Additional == additional)
            {
                return this;
            }

            if (this._sealed)
            {
                throw new FrameworkConfigurationException(
                    $"'{typeof(TPack).Name}' was included after a run had already used this environment.")
                {
                    RecoverySteps = ["Include every pack while building the environment, before the first run is set up with it."],
                    AvailableOptions = [.. this._packsByService.Values.Select(pack => pack.PackType.Name)],
                };
            }

            if (existing is not null)
            {
                throw new FrameworkConfigurationException(
                    $"Two packs replace '{serviceType.Name}': '{existing.PackType.Name}' and '{typeof(TPack).Name}'.")
                {
                    RecoverySteps = ["Keep one pack per service; merge the setups into one definition if both behaviours are meant."],
                };
            }

            this._packsByService[serviceType] = new IncludedPack(typeof(TPack), additional);

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

    /// <summary>
    /// Composes one run's services, refusing a composition that registers different services than the
    /// environment declared.
    /// </summary>
    internal ServiceCollection ComposeServicesForRun()
    {
        IReadOnlySet<Type> declared = this.Declare().ServiceTypes;

        ServiceCollection services = new();
        this._composeServices(services);

        HashSet<Type> composed = RegisteredServiceTypes(services);
        if (!composed.SetEquals(declared))
        {
            string added = Describe(composed.Except(declared));
            string missing = Describe(declared.Except(composed));
            throw new FrameworkConfigurationException(
                $"The composition handed to MockEnvironment.For registered different services for this run than when the run was planned (added: {added}; missing: {missing}).")
            {
                RecoverySteps = ["Register the same services every time the delegate runs; a service that is only sometimes present belongs in its own environment."],
            };
        }

        return services;
    }

    /// <summary>Whether a pack stands in for a service the composition registers, or adds one of its own.</summary>
    internal bool IsAdditional(Type serviceType)
    {
        lock (this._gate)
        {
            return this._packsByService.TryGetValue(serviceType, out IncludedPack? pack) && pack.Additional;
        }
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
    /// Composed once, the first time it is read, and kept: reading it again does not run the delegate again.
    /// A keyed-only registration is left out, because a <c>Host</c> step resolves its service without a key;
    /// an open generic registration is declared under its definition's name, which a <c>Host</c> step for a
    /// closed type of it requires.
    /// </remarks>
    /// <exception cref="FrameworkConfigurationException">An included pack's service is not registered by the
    /// composition, or an additional pack's service is.</exception>
    public IReadOnlyList<ResourceNode> Nodes => this.Declare().Nodes;

    private (IReadOnlyList<ResourceNode> Nodes, IReadOnlySet<Type> ServiceTypes) Declare()
    {
        lock (this._gate)
        {
            if (this._declaredNodes is not null && this._declaredServiceTypes is not null)
            {
                return (this._declaredNodes, this._declaredServiceTypes);
            }

            ServiceCollection services = new();
            this._composeServices(services);
            HashSet<Type> registered = RegisteredServiceTypes(services);

            foreach ((Type serviceType, IncludedPack pack) in this._packsByService)
            {
                RefuseMismatchedPack(serviceType, pack, registered.Contains(serviceType));
            }

            IEnumerable<Type> serviceTypes = services
                .Where(static descriptor => !descriptor.IsKeyedService)
                .Select(static descriptor => descriptor.ServiceType)
                .Concat(this._packsByService.Keys);

            this._declaredNodes = new HostedServices(this.SourceName, serviceTypes).Nodes;
            this._declaredServiceTypes = registered;
            this._sealed = true;
            return (this._declaredNodes, this._declaredServiceTypes);
        }
    }

    private static void RefuseMismatchedPack(Type serviceType, IncludedPack pack, bool registered)
    {
        if (!registered && !pack.Additional)
        {
            throw new FrameworkConfigurationException(
                $"'{pack.PackType.Name}' replaces '{serviceType.Name}', but the composition handed to MockEnvironment.For never registers '{serviceType.Name}'.")
            {
                RecoverySteps =
                [
                    $"Check that the composition registers '{serviceType.Name}' - a renamed or moved dependency is the usual cause.",
                    $"To add the double as a service the composition lacks, include it with IncludeAdditional<{pack.PackType.Name}>().",
                ],
            };
        }

        if (registered && pack.Additional)
        {
            throw new FrameworkConfigurationException(
                $"'{pack.PackType.Name}' was included as additional, but the composition already registers '{serviceType.Name}'.")
            {
                RecoverySteps = [$"Include it with Include<{pack.PackType.Name}>(), so it replaces that registration."],
            };
        }
    }

    private static HashSet<Type> RegisteredServiceTypes(IServiceCollection services)
        => [.. services.Select(static descriptor => descriptor.ServiceType)];

    private static string Describe(IEnumerable<Type> types)
    {
        string[] names = [.. types.Select(static type => $"'{type.Name}'").OrderBy(static name => name, StringComparer.Ordinal)];
        return names.Length == 0 ? "none" : string.Join(", ", names);
    }

    private sealed record IncludedPack(Type PackType, bool Additional);

    private sealed class HostedServices(string sourceName, IEnumerable<Type> serviceTypes) : DeclaredNodeSource
    {
        public override string SourceName => sourceName;

        protected override IEnumerable<DeclaredResource> Declarations
            => serviceTypes
                .Select(MockHost.ResourceName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .Select(name => new DeclaredResource { Kind = MockResourceKinds.HostKind, Identifier = name, Values = new Dictionary<ValueKey, string>(), DeclaredBy = $"mock host service '{name}'" });
    }
}
