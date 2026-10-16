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
                "This run has no mock host, so no step can reach the hosted services.")
            {
                RecoverySteps = ["Set the environment on the run: SetupRun(...).SetEnv(MockEnvironment.For(...))."],
            };
        }

        return host;
    }

    /// <summary>
    /// Resolves the system under test from one hosted call's scope.
    /// </summary>
    /// <remarks>
    /// A registered service whose own dependencies the composition lacks fails inside the container, and
    /// that is the first failure most compositions meet: a bare <c>ServiceCollection</c> has none of what the
    /// generic host adds for free. The container's own sentence names the missing piece, so it is carried in
    /// the message word for word rather than left on an inner exception a reader may never see.
    /// </remarks>
    public static TService Resolve<TService>(IServiceProvider callScope)
        where TService : class
    {
        TService? service;
        try
        {
            service = callScope.GetService<TService>();
        }
        catch (InvalidOperationException exception)
        {
            throw new FrameworkConfigurationException(
                $"'{typeof(TService).Name}' is registered in the hosted services, but could not be built: {exception.Message}", exception)
            {
                RecoverySteps =
                [
                    "Register what the message names in the composition handed to MockEnvironment.For(...).",
                    "The generic host normally provides logging, configuration and the host environment; a bare composition needs services.AddLogging() and an IConfiguration registration of its own.",
                ],
            };
        }

        return service
            ?? throw new FrameworkConfigurationException(
                $"'{typeof(TService).Name}' is not registered in the hosted services.")
            {
                RecoverySteps = ["Register it in the composition handed to MockEnvironment.For(...)."],
            };
    }

    /// <summary>
    /// Declares each named argument variable as a step input, typed as the call reads it, so the planner sees
    /// the dependency and checks that whatever produces the variable produces that type.
    /// </summary>
    public static void DeclareArguments(StepIOContract contract, IEnumerable<VariableReferenceGeneric> arguments)
    {
        foreach (VariableReferenceGeneric argument in arguments)
        {
            if (argument.Identifier is not { } identifier)
            {
                continue;
            }

            if (ReadAs(argument) is { } type)
            {
                contract.Input(identifier, type);
            }
            else
            {
                contract.Input(argument);
            }
        }
    }

    // The T of the VariableReference<T> the argument is, found on its own type or a base of it.
    private static Type? ReadAs(VariableReferenceGeneric argument)
    {
        for (Type? type = argument.GetType(); type is not null; type = type.BaseType)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(VariableReference<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
