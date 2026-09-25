using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Exceptions;

namespace TestFramework.Mock;

/// <summary>
/// One run's live mock host: the composed service provider and the doubles standing in it. Lives
/// in the run's state slot while the run executes, and stays readable from the finished run
/// through the environment context — reading is the point of keeping it; everything that could
/// change it is internal.
/// </summary>
public sealed class MockHostState
{
    private readonly ServiceProvider _provider;
    private readonly IReadOnlyDictionary<Type, MockInstanceGeneric> _instances;

    internal MockHostState(ServiceProvider provider, IReadOnlyDictionary<Type, MockInstanceGeneric> instances)
    {
        this._provider = provider;
        this._instances = instances;
    }

    internal IServiceProvider Provider => this._provider;

    internal IEnumerable<MockInstanceGeneric> Instances => this._instances.Values;

    /// <summary>
    /// The double standing in for one service — the verification read.
    /// </summary>
    public MockInstance<TService> Mock<TService>()
        where TService : class
    {
        if (this._instances.TryGetValue(typeof(TService), out MockInstanceGeneric? instance))
        {
            return (MockInstance<TService>)instance;
        }

        throw new FrameworkConfigurationException(
            $"No pack in this run replaces '{typeof(TService).Name}'.",
            recoverySteps: ["Include a MockDefinition for the service on the MockEnvironment."],
            availableOptions: [.. this._instances.Keys.Select(type => type.Name)]);
    }

    /// <summary>
    /// What the one double that published an identity holds for it. Two doubles publishing the same
    /// identity is refused naming both — which of them a finder meant is not something to guess.
    /// </summary>
    internal bool TryGetLatestPublish(string identity, out object? payload)
    {
        MockInstanceGeneric[] publishers = [.. this._instances.Values.Where(instance => instance.TryGetLatestPublish(identity, out _))];
        if (publishers.Length > 1)
        {
            throw new FrameworkConfigurationException(
                $"More than one double published '{identity}', so which one it names is ambiguous.",
                recoverySteps: ["Give each pack's ProducesArtifact or Publish its own identity."],
                availableOptions: [.. publishers.Select(instance => instance.ServiceType.Name)]);
        }

        payload = null;
        return publishers.Length == 1 && publishers[0].TryGetLatestPublish(identity, out payload);
    }

    internal void FreezeForRunEnd()
    {
        foreach (MockInstanceGeneric instance in this._instances.Values)
        {
            instance.FreezeForRunEnd();
        }
    }

    internal Task DisposeProviderAsync()
    {
        return this._provider.DisposeAsync().AsTask();
    }
}
