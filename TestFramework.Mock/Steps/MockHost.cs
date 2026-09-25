using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;

namespace TestFramework.Mock;

/// <summary>
/// What every step touching the run's mock host shares: the requirement that starts it, the one
/// way to reach it, and how the call's variable-bound arguments enter the IO contract.
/// </summary>
internal static class MockHost
{
    /// <summary>
    /// The name a hosted service carries on the run's resource list: its full type name, and for an open
    /// generic registration the definition's.
    /// </summary>
    public static string ResourceName(Type serviceType)
    {
        return serviceType.FullName ?? serviceType.Name;
    }

    /// <summary>
    /// Requires the service a <c>Host</c> step calls, with the run's resources in view: the service itself
    /// when it is registered, else the open generic definition it is a closed type of. Whatever is required
    /// is checked by the engine before the run starts, and reaching this environment is what starts the host.
    /// </summary>
    public static IReadOnlyCollection<EnvironmentRequirement> RequirementsFor<TService>(ResourceGraph? resources)
    {
        string exact = ResourceName(typeof(TService));
        if (resources is not null
            && !resources.TryGetNode(MockResourceKinds.Host, exact, out _)
            && typeof(TService).IsConstructedGenericType
            && resources.TryGetNode(MockResourceKinds.Host, ResourceName(typeof(TService).GetGenericTypeDefinition()), out ResourceNode? definition)
            && definition is not null)
        {
            return [MockResourceKinds.HostKind.Requirement(definition.Identifier)];
        }

        return [MockResourceKinds.HostKind.Requirement(exact)];
    }

    /// <summary>
    /// The run's live host, or a refusal naming how to set one.
    /// </summary>
    public static MockHostState StateOf(RunContext context)
    {
        if (!context.State.TryGet(out MockHostState? host) || host is null)
        {
            throw new FrameworkConfigurationException(
                "This run has no mock host, so no step can reach the hosted services.",
                recoverySteps: ["Set the environment on the run: SetupRun(...).SetEnv(MockEnvironment.For(...))."]);
        }

        return host;
    }

    /// <summary>
    /// Resolves the system under test from one hosted call's scope.
    /// </summary>
    public static TService Resolve<TService>(IServiceProvider callScope)
        where TService : class
    {
        return callScope.GetService<TService>()
            ?? throw new FrameworkConfigurationException(
                $"'{typeof(TService).Name}' is not registered in the hosted services.",
                recoverySteps: ["Register it in the composition handed to MockEnvironment.For(...)."]);
    }

    /// <summary>
    /// Declares each named argument variable as a step input, so the planner sees the dependency.
    /// </summary>
    public static void DeclareArguments(StepIOContract contract, IEnumerable<VariableReferenceGeneric> arguments)
    {
        foreach (VariableReferenceGeneric argument in arguments)
        {
            if (argument.HasIdentifier)
            {
                contract.Inputs.Add(new StepIOEntry(argument.Identifier!.Identifier, StepIOKind.Variable));
            }
        }
    }
}
