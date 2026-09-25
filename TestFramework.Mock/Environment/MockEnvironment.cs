using System;
using System.Collections.Generic;

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
    private readonly Dictionary<Type, MockDefinitionGeneric> _packsByService = [];
    private readonly List<MockDefinitionGeneric> _packs = [];

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
    /// later one and the earlier would vanish silently.
    /// </summary>
    public MockEnvironment Include<TPack>()
        where TPack : MockDefinitionGeneric, new()
    {
        TPack pack = new();
        if (this._packsByService.TryGetValue(pack.ServiceType, out MockDefinitionGeneric? existing))
        {
            if (existing.GetType() == typeof(TPack))
            {
                return this;
            }

            throw new FrameworkConfigurationException(
                $"Two packs replace '{pack.ServiceType.Name}': '{existing.GetType().Name}' and '{typeof(TPack).Name}'.",
                recoverySteps: ["Keep one pack per service; merge the setups into one definition if both behaviours are meant."]);
        }

        this._packsByService[pack.ServiceType] = pack;
        this._packs.Add(pack);
        return this;
    }

    internal IReadOnlyList<MockDefinitionGeneric> Packs => this._packs;

    internal void ComposeServices(IServiceCollection services)
    {
        this._composeServices(services);
    }
}
