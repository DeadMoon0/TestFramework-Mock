using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        ServiceCollection services = new();
        this._owner.ComposeServices(services);

        Dictionary<Type, MockInstanceGeneric> instances = [];
        foreach (MockDefinitionGeneric pack in this._owner.Packs)
        {
            MockInstanceGeneric instance = pack.CreateInstanceGeneric();

            // Remove every registration of the service before adding the double, so nothing —
            // including an IEnumerable<TService> resolution — still reaches the real one.
            services.RemoveAll(instance.ServiceType);
            services.AddSingleton(instance.ServiceType, instance.ProxyObject);
            instances[instance.ServiceType] = instance;

            context.EffectiveSettings.Record(
                MockResourceKinds.Host,
                instance.ServiceType.FullName ?? instance.ServiceType.Name,
                pack.GetType().Name);
        }

        ServiceProvider provider = services.BuildServiceProvider();
        MockHostState state = new(provider, instances);

        MockHostState slotted = context.State.GetOrAdd(() => state);
        if (!ReferenceEquals(slotted, state))
        {
            provider.Dispose();
            throw new FrameworkStateException(
                "This run already holds a mock host; a run hosts one composition.",
                recoverySteps: ["Set one MockEnvironment per run; two compositions are two runs."]);
        }

        return Task.FromResult<object?>(state);
    }

    public override async Task DeconstructAsync(object? state, IEnvironmentProvider environment, RunContext context)
    {
        if (state is not MockHostState host)
        {
            return;
        }

        // Freeze first, dispose after: from here on the doubles refuse further calls by name,
        // instead of a disposed provider answering with an ObjectDisposedException.
        host.FreezeForRunEnd();
        await host.DisposeProviderAsync();
    }
}
