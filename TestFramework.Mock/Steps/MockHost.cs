using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
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
    /// Requires the mock host, so setting a step that needs it is what starts it.
    /// </summary>
    public static IReadOnlyCollection<EnvironmentRequirement> Requirements()
    {
        return [new EnvironmentRequirement(MockResourceKinds.Host, MockResourceKinds.HostIdentifier)];
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
    /// Resolves the system under test from the run's hosted services.
    /// </summary>
    public static TService Resolve<TService>(RunContext context)
        where TService : class
    {
        return StateOf(context).Provider.GetService<TService>()
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
