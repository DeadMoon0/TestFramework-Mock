using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Environment;
using TestFramework.Core.Environment.Graph;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;
using TestFramework.Core.Variables;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// What an in-process call produced.
/// </summary>
/// <typeparam name="TResult">The called method's return type — for an awaited call, what its task yielded.</typeparam>
/// <param name="Value">The returned value.</param>
public sealed record MockCallResult<TResult>(TResult Value) : StepResultContext;

/// <summary>
/// Resolves one service from the run's mock host and calls it — the trigger that drives the
/// system under test in-process. The hosted object arrives behind the lambda's other concerns,
/// the same convention every setup body follows; arguments bound from run variables join with the
/// generated arities. A call that returns a task is awaited, so the step finishes when the call
/// does; the void counterpart is <see cref="MockHostCallTrigger{TService}"/>.
/// </summary>
/// <typeparam name="TService">The hosted service to call — the system under test, not a double.</typeparam>
/// <typeparam name="TResult">The call's result; for an awaited call, what its task yielded.</typeparam>
public sealed class MockHostCallTrigger<TService, TResult> : Step<MockCallResult<TResult>>, IHasEnvironmentRequirements
    where TService : class
{
    // One class for every arity and for sync and async alike: the generated MockExt.Host overloads
    // carry the argument types and close over their typed variable references, and a synchronous
    // call arrives already completed. What the step itself needs is only "resolve the arguments
    // against this run's variables, make the call with the step's cancellation, await it".
    private readonly Func<VariableStore, TService, CancellationToken, Task<TResult>> _call;
    private readonly VariableReferenceGeneric[] _arguments;

    internal MockHostCallTrigger(Func<VariableStore, TService, CancellationToken, Task<TResult>> call, VariableReferenceGeneric[] arguments)
    {
        RefuseUnawaitedResult();

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
    public override bool DoesReturn => true;

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
    public override Step<MockCallResult<TResult>> Clone()
    {
        return new MockHostCallTrigger<TService, TResult>(this._call, this._arguments).WithClonedOptions(this);
    }

    /// <summary>
    /// Creates a runtime instance for this trigger.
    /// </summary>
    public override StepInstance<Step<MockCallResult<TResult>>, MockCallResult<TResult>> GetInstance() => new(this);

    /// <summary>
    /// Declares the variable inputs the call's arguments are bound from.
    /// </summary>
    public override void DeclareIO(StepIOContract contract)
    {
        MockHost.DeclareArguments(contract, this._arguments);
    }

    /// <summary>
    /// Resolves the service from a scope opened for this call, calls it with the step's cancellation,
    /// and awaits the call. The scope - and every scoped service in it - is disposed when the call ends.
    /// </summary>
    /// <param name="context">What this step is given.</param>
    /// <returns>The step's result.</returns>
    public override async Task<MockCallResult<TResult>?> Execute(RunContext context)
    {
        await using AsyncServiceScope scope = MockHost.StateOf(context).CreateCallScope();
        TService service = MockHost.Resolve<TService>(scope.ServiceProvider);
        return new MockCallResult<TResult>(await this._call(context.Variables, service, context.Deadline.Token));
    }

    /// <summary>
    /// Host awaits a Task or Task&lt;T&gt;. Any other awaitable reaching the result — a ValueTask,
    /// a task of a task — would be handed back unawaited, and the step would pass while the call it
    /// made is still running. Refused where the trigger is declared, naming the fix.
    /// </summary>
    private static void RefuseUnawaitedResult()
    {
        if (typeof(TResult).GetMethod(nameof(Task.GetAwaiter), BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes) is null)
        {
            return;
        }

        throw new FrameworkConfigurationException(
            $"The hosted call yields {MockValueText.DescribeType(typeof(TResult))}, which Host would not await; the step would pass while the call is still running.",
            recoverySteps: ["Return a Task or Task<T> from the call so Host awaits it — for a ValueTask, call .AsTask()."]);
    }
}
