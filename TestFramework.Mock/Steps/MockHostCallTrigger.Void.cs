using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;

namespace TestFramework.Mock;

/// <summary>
/// Resolves one service from the run's mock host and calls it for no value — the void counterpart of
/// <see cref="MockHostCallTrigger{TService, TResult}"/>, for a synchronous void method and an awaited
/// task alike. The step finishes when the call's task does, never when it was merely started.
/// </summary>
/// <typeparam name="TService">The hosted service to call — the system under test, not a double.</typeparam>
public sealed class MockHostCallTrigger<TService> : Step<EmptyStepResultContext>, IHasEnvironmentRequirements
    where TService : class
{
    private readonly Func<VariableStore, TService, CancellationToken, Task> _call;
    private readonly VariableReferenceGeneric[] _arguments;

    internal MockHostCallTrigger(Func<VariableStore, TService, CancellationToken, Task> call, VariableReferenceGeneric[] arguments)
    {
        this._call = call ?? throw new ArgumentNullException(nameof(call));
        this._arguments = arguments;
    }

    /// <summary>
    /// Gets the display name shown in the timeline output.
    /// </summary>
    public override string Name => $"Call {typeof(TService).Name}";

    /// <summary>
    /// Gets a short description of what the trigger does.
    /// </summary>
    public override string Description => $"Resolves {typeof(TService).Name} from the run's mock host and calls it in-process.";

    /// <summary>
    /// Gets a value indicating whether the trigger produces a result payload.
    /// </summary>
    public override bool DoesReturn => false;

    /// <summary>
    /// Requires the hosted service this trigger calls.
    /// </summary>
    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore)
    {
        return MockHost.RequirementsFor<TService>(null);
    }

    /// <summary>
    /// Requires the hosted service this trigger calls - or the open generic registration it is a closed type
    /// of - so the engine refuses a service nothing registers before the run starts, and reaching the mock host
    /// is what starts it.
    /// </summary>
    public IReadOnlyCollection<EnvironmentRequirement> GetEnvironmentRequirements(VariableStore variableStore, ResourceGraph resources)
    {
        return MockHost.RequirementsFor<TService>(resources);
    }

    /// <summary>
    /// Creates a copy of the trigger together with its configured step options.
    /// </summary>
    public override Step<EmptyStepResultContext> Clone()
    {
        return new MockHostCallTrigger<TService>(this._call, this._arguments).WithClonedOptions(this);
    }

    /// <summary>
    /// Creates a runtime instance for this trigger.
    /// </summary>
    public override StepInstance<Step<EmptyStepResultContext>, EmptyStepResultContext> GetInstance() => new(this);

    /// <summary>
    /// Declares the variable inputs the call's arguments are bound from.
    /// </summary>
    public override void DeclareIO(StepIOContract contract)
    {
        MockHost.DeclareArguments(contract, this._arguments);
    }

    /// <summary>
    /// Resolves the service from a scope opened for this call, calls it with the step's cancellation,
    /// and awaits the call. The scope is disposed when the call ends.
    /// </summary>
    /// <param name="context">What this step is given.</param>
    /// <returns>The step's result.</returns>
    public override async Task<EmptyStepResultContext?> Execute(RunContext context)
    {
        await using AsyncServiceScope scope = MockHost.StateOf(context).CreateCallScope();
        TService service = MockHost.Resolve<TService>(scope.ServiceProvider);
        await this._call(context.Variables, service, context.Deadline.Token);
        return EmptyStepResultContext.Instance;
    }
}
