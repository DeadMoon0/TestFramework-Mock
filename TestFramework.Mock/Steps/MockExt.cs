using System;
using System.Threading.Tasks;

using TestFramework.Core.Variables;

namespace TestFramework.Mock;

/// <summary>
/// The package's timeline verbs. <c>Host</c> calls the hosted system under test — deliberately
/// not named <c>Call</c>, which on a pack's builder means the opposite: setting up a double.
/// </summary>
public static partial class MockExt
{
    /// <summary>
    /// A trigger that resolves the service from the run's mock host and calls it in-process.
    /// Overloads with arguments bound from run variables are generated per arity — the hosted
    /// service always arrives behind the call's own arguments. A call returning a task binds to
    /// the awaiting overloads instead; any other awaitable result is refused where it is declared.
    /// </summary>
    /// <typeparam name="TService">The hosted service — the system under test, not a double.</typeparam>
    /// <typeparam name="TResult">The call's return type.</typeparam>
    /// <param name="call">The call to make, with the hosted service handed in.</param>
    /// <returns>The trigger, for <c>.Trigger(...)</c>.</returns>
    public static MockHostCallTrigger<TService, TResult> Host<TService, TResult>(Func<TService, TResult> call)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(call);

        return new MockHostCallTrigger<TService, TResult>((_, service) => Task.FromResult(call(service)), []);
    }

    /// <summary>
    /// A trigger that calls the hosted service and awaits the task it returns; the step's result
    /// is what the task yielded.
    /// </summary>
    /// <typeparam name="TService">The hosted service — the system under test, not a double.</typeparam>
    /// <typeparam name="TResult">What the call's task yields.</typeparam>
    /// <param name="call">The call to make, with the hosted service handed in.</param>
    /// <returns>The trigger, for <c>.Trigger(...)</c>.</returns>
    public static MockHostCallTrigger<TService, TResult> Host<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(call);

        return new MockHostCallTrigger<TService, TResult>((_, service) => call(service), []);
    }

    /// <summary>
    /// A trigger that calls the hosted service and awaits the task it returns, for a call that
    /// yields no value.
    /// </summary>
    /// <typeparam name="TService">The hosted service — the system under test, not a double.</typeparam>
    /// <param name="call">The call to make, with the hosted service handed in.</param>
    /// <returns>The trigger, for <c>.Trigger(...)</c>.</returns>
    public static MockHostCallTrigger<TService> Host<TService>(Func<TService, Task> call)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(call);

        return new MockHostCallTrigger<TService>((_, service) => call(service), []);
    }
}
