using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;

namespace TestFramework.Mock;

/// <summary>
/// The one component of a <see cref="MockEnvironment"/>: composes the user's services, puts each
/// pack's double where the real dependency was registered, and holds the built provider for the
/// run. Every replacement is recorded on the run's effective settings, so a finished run says it
/// proved something against doubles — and which.
/// </summary>
internal sealed class MockHostEnvComponent : EnvComponent
{
    internal static readonly EnvComponentIdentifier ComponentId = new("mock-host");

    private readonly MockEnvironment _owner;

    public MockHostEnvComponent(MockEnvironment owner)
    {
        this._owner = owner;
    }

    public override EnvComponentIdentifier Id => ComponentId;

    public override Task<object?> CreateAsync(IEnvironmentProvider environment, RunContext context)
    {
        ServiceCollection services = this._owner.ComposeServicesForRun();

        Dictionary<Type, MockInstanceGeneric> instances = [];
        foreach (MockPackGeneric pack in this._owner.CreatePacksForRun())
        {
            MockInstanceGeneric instance = pack.CreateInstanceGeneric(context);
            string service = instance.ServiceType.FullName ?? instance.ServiceType.Name;

            IReadOnlyList<object?> keys = this._owner.IsAdditional(instance.ServiceType)
                ? AddAsNewService(services, instance.ServiceType, instance.ProxyObject)
                : ReplaceEveryRegistration(services, instance.ServiceType, instance.ProxyObject);

            foreach (object? key in keys)
            {
                context.EffectiveSettings.Record(MockResourceKinds.Host, $"{service}[{key}]", pack.GetType().Name);
            }

            instances[instance.ServiceType] = instance;
            context.EffectiveSettings.Record(MockResourceKinds.Host, service, pack.GetType().Name);
        }

        ServiceProvider provider = services.BuildServiceProvider();
        MockHostState state = new(provider, instances);

        MockHostState slotted = context.State.GetOrAdd(() => state);
        if (!ReferenceEquals(slotted, state))
        {
            provider.Dispose();
            throw new FrameworkStateException(
                "This run already holds a mock host; a run hosts one composition.")
            {
                RecoverySteps = ["Set one MockEnvironment per run; two compositions are two runs."],
            };
        }

        return Task.FromResult<object?>(state);
    }

    public override async Task DeconstructAsync(object? state, IEnvironmentProvider environment, RunContext context)
    {
        if (state is not MockHostState host)
        {
            return;
        }

        // Disposing is how the system under test stops its own background work - a timer, a flush on
        // dispose - and while it does so the doubles still answer and record. Their records finish with the
        // run, not here: they are the run's records, so a run whose teardown never reached this point still
        // closes them.
        await host.DisposeProviderAsync();
    }

    /// <summary>
    /// Removes every registration of the service - the plain one, every keyed one, and so every
    /// <c>IEnumerable&lt;TService&gt;</c> - and puts the double in each slot, so nothing that asks the
    /// container for the service reaches the real dependency.
    /// </summary>
    /// <remarks>
    /// Only what is asked for by the service type is replaced. Code that depends on the concrete class, or a
    /// factory registration that constructs the real implementation itself, still reaches it.
    /// </remarks>
    /// <returns>The keys the double now also stands under.</returns>
    private static IReadOnlyList<object?> ReplaceEveryRegistration(IServiceCollection services, Type serviceType, object proxy)
    {
        List<object?> keys = [.. services
            .Where(descriptor => descriptor.ServiceType == serviceType && descriptor.IsKeyedService)
            .Select(static descriptor => descriptor.ServiceKey)
            .Distinct()];

        for (int index = services.Count - 1; index >= 0; index--)
        {
            if (services[index].ServiceType == serviceType)
            {
                services.RemoveAt(index);
            }
        }

        services.AddSingleton(serviceType, proxy);
        foreach (object? key in keys)
        {
            services.AddKeyedSingleton(serviceType, key, proxy);
        }

        return keys;
    }

    private static IReadOnlyList<object?> AddAsNewService(IServiceCollection services, Type serviceType, object proxy)
    {
        services.AddSingleton(serviceType, proxy);
        return [];
    }
}
