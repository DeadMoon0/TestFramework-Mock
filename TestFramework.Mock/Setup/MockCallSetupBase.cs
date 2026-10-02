using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using TestFramework.Core.Exceptions;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// The declaration half every setup shares: one call pattern, at most one result, any number of
/// artifact producers. Instances of the derived types are handed out by
/// <see cref="MockBuilder{TService}.Call"/> and cannot be built directly.
/// </summary>
public abstract class MockCallSetupBase
{
    private readonly List<Action<object?[], MockArtifacts>> _artifactProducers = [];
    private readonly SortedDictionary<int, Func<object?[], object?>> _assignments = [];
    private Func<object?[], MockArtifacts, object?>? _response;
    private volatile bool _sealed;

    // How a still-running task is followed by the producers, decided once from the method's return type.
    private readonly Func<Task, Action, Task> _thenProduce;

    // The positions of the parameters a call brings a value for: every one but the out parameters. These are
    // "the call's arguments" every typed lambda receives - an out position has nothing to hand it.
    private readonly int[] _incoming;

    private protected MockCallSetupBase(CallPattern pattern)
    {
        this.Pattern = pattern;
        this._thenProduce = ThenProduceFor(pattern.Method.ReturnType);
        this._incoming = [.. pattern.Method.GetParameters().Where(static parameter => !parameter.IsOut).Select(static parameter => parameter.Position)];
    }

    internal CallPattern Pattern { get; }

    internal bool HasResponse => this._response is not null;

    internal bool DeclaresUnreachableArtifacts => this.AlwaysFails && this._artifactProducers.Count > 0;

    /// <summary>
    /// Whether the setup states a write-back that no call can reach, because every call throws.
    /// </summary>
    internal bool DeclaresUnreachableAssignments => this.ThrowsAtCall && this._assignments.Count > 0;

    /// <summary>
    /// The out parameters a call that returns would leave unset - a value nobody stated. A call that throws
    /// returns nothing, so it owes nothing.
    /// </summary>
    internal IEnumerable<ParameterInfo> UnassignedOutParameters => this.ThrowsAtCall
        ? []
        : this.Pattern.Method.GetParameters().Where(parameter => parameter.IsOut && !this._assignments.ContainsKey(parameter.Position));

    /// <summary>
    /// Whether every call fails - by throwing, or by handing back a task that has already failed.
    /// </summary>
    private bool AlwaysFails { get; set; }

    /// <summary>
    /// Whether every call throws at the call itself, so it never returns - unlike a failed task, which a call
    /// returns, and which still owes its out parameters.
    /// </summary>
    private bool ThrowsAtCall { get; set; }

    /// <summary>
    /// Closes the declaration once the double is built. A setup object can outlive <c>Configure</c> -
    /// a pack may keep the reference <c>mock.Call</c> returned - and changing it afterwards would
    /// change a double that is already answering calls, racing the calls it is answering.
    /// </summary>
    internal void Seal()
    {
        this._sealed = true;
    }

    /// <summary>
    /// Answers one matching call.
    /// </summary>
    /// <param name="arguments">The call's arguments by position, as the interceptor received them.</param>
    /// <param name="artifacts">The double's artifact channel.</param>
    /// <param name="writeBack">Writes a value into an out or ref position of the caller's call.</param>
    /// <returns>What the call returns.</returns>
    internal object? Invoke(object?[] arguments, MockArtifacts artifacts, Action<int, object?> writeBack)
    {
        object?[] incoming = [.. this._incoming.Select(position => arguments[position])];

        // The result runs first: a declared artifact is what a completed call produced, so a
        // call that throws is never assumed to have produced it. What a body published by hand
        // before throwing stands — those publishes happened. The write-backs are computed before any
        // of them land, so one that throws leaves the caller's variables as they were.
        object? result = this._response is null ? null : this._response(incoming, artifacts);

        List<KeyValuePair<int, object?>> values = [.. this._assignments.Select(assignment => KeyValuePair.Create(assignment.Key, assignment.Value(incoming)))];
        foreach (KeyValuePair<int, object?> value in values)
        {
            writeBack(value.Key, value.Value);
        }

        if (this._artifactProducers.Count > 0)
        {
            result = this.ProduceWhenCompleted(result, () => this.RunProducers(incoming, artifacts));
        }

        return result;
    }

    private protected void SetThrows(Exception exception)
    {
        this.SetResponse((_, _) => throw exception);
        this.AlwaysFails = true;
        this.ThrowsAtCall = true;
    }

    /// <summary>
    /// States what the call writes back into one of its out or ref parameters, checked against the mocked
    /// method here, where the setup is declared.
    /// </summary>
    /// <param name="parameter">The parameter's name, as the method declares it.</param>
    /// <param name="valueType">The type of the value stated for it.</param>
    /// <param name="compute">The value, from the call's arguments.</param>
    private protected void AddAssignment(string parameter, Type valueType, Func<object?[], object?> compute)
    {
        this.EnsureOpen();
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);

        ParameterInfo[] parameters = this.Pattern.Method.GetParameters();
        ParameterInfo? target = parameters.FirstOrDefault(candidate => string.Equals(candidate.Name, parameter, StringComparison.Ordinal));
        string[] writable = [.. parameters.Where(static candidate => candidate.ParameterType.IsByRef && !candidate.IsIn).Select(Describe)];

        if (target is null || !target.ParameterType.IsByRef || target.IsIn)
        {
            throw new FrameworkConfigurationException(
                target is null
                    ? $"'{this.Pattern.Describe()}' has no parameter named '{parameter}' to write back into."
                    : $"'{parameter}' on '{this.Pattern.Describe()}' is not an out or ref parameter, so a call cannot write anything back into it.")
            {
                RecoverySteps = ["Name one of the method's out or ref parameters, exactly as the method declares it."],
                AvailableOptions = writable,
            };
        }

        Type element = target.ParameterType.GetElementType()!;
        if (!element.IsAssignableFrom(valueType))
        {
            throw new FrameworkConfigurationException(
                $"'{parameter}' on '{this.Pattern.Describe()}' is {element.Name}, and the value stated for it is {valueType.Name}.")
            {
                RecoverySteps = [$"State a {element.Name} for '{parameter}'."],
            };
        }

        if (!this._assignments.TryAdd(target.Position, compute))
        {
            throw new FrameworkConfigurationException(
                $"'{parameter}' on '{this.Pattern.Describe()}' already has its value stated; a setup states it once.")
            {
                RecoverySteps = ["Keep one Assigns for this parameter - or add a second mock.Call(...) if two behaviours are meant."],
            };
        }
    }

    private static string Describe(ParameterInfo parameter)
        => $"{(parameter.IsOut ? "out" : "ref")} {parameter.ParameterType.GetElementType()!.Name} {parameter.Name}";

    /// <summary>
    /// States a result that is always an already-failed task: the async form of <see cref="SetThrows"/>.
    /// The call itself returns, the way a real async method does, and whoever awaits it gets the
    /// exception.
    /// </summary>
    private protected void SetFailedTask(Func<object> failedTask)
    {
        this.SetResponse((_, _) => failedTask());
        this.AlwaysFails = true;
    }

    /// <summary>
    /// For an async call, "completed" means its task finished successfully - a method that returns a
    /// task which later fails has not produced what it was declared to produce. A task that has
    /// already finished decides at once; a pending one decides when it finishes, before whoever
    /// awaits it continues. A pending <see cref="ValueTask"/> cannot be observed without consuming
    /// what the system under test is about to await, so it is treated as completed.
    /// </summary>
    /// <remarks>
    /// A pending task is handed back wrapped: the caller awaits a task that finishes once the producers
    /// have run, with the call's own value, failure or cancellation. A producer that throws then fails the
    /// caller's await, exactly as it fails a synchronous call. It used to run in a detached continuation, so
    /// the same setup failed loudly or not at all depending on whether the dependency's task had finished
    /// by the time it returned.
    /// </remarks>
    /// <returns>What the double hands back: the result itself, or the wrapper around a pending task.</returns>
    private object? ProduceWhenCompleted(object? result, Action produce)
    {
        switch (result)
        {
            case Task { IsCompleted: true } finished:
                if (finished.IsCompletedSuccessfully)
                {
                    produce();
                }

                return result;
            case Task pending:
                return this._thenProduce(pending, produce);
            case ValueTask { IsCompleted: true } finished:
                if (finished.IsCompletedSuccessfully)
                {
                    produce();
                }

                return result;
            default:
                if (result is null || !IsFailedValueTaskOfT(result))
                {
                    produce();
                }

                return result;
        }
    }

    private static Func<Task, Action, Task> ThenProduceFor(Type returnType)
    {
        if (!returnType.IsGenericType || returnType.GetGenericTypeDefinition() != typeof(Task<>))
        {
            return ThenProduce;
        }

        return typeof(MockCallSetupBase)
            .GetMethod(nameof(ThenProduceWithValue), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(returnType.GetGenericArguments()[0])
            .CreateDelegate<Func<Task, Action, Task>>();
    }

    // A continuation that runs on the thread finishing the dependency's task, not an await: an await is not
    // inlined under a synchronisation context - a test runner installs one - so the producers would run after
    // whoever awaits the call has already moved on, instead of before.
    private static Task ThenProduce(Task call, Action produce)
    {
        TaskCompletionSource done = new();
        call.ContinueWith(
            finished => Settle(finished, done.TrySetCanceled, done.TrySetException, () =>
            {
                produce();
                done.TrySetResult();
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return done.Task;
    }

    private static Task ThenProduceWithValue<T>(Task call, Action produce)
    {
        TaskCompletionSource<T> done = new();
        call.ContinueWith(
            finished => Settle(finished, done.TrySetCanceled, done.TrySetException, () =>
            {
                T value = ((Task<T>)finished).Result;
                produce();
                done.TrySetResult(value);
            }),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return done.Task;
    }

    /// <summary>
    /// Passes the call's own outcome on, and runs the producers only for a call that succeeded; a producer that
    /// throws fails the wrapper with its own exception.
    /// </summary>
    private static void Settle(
        Task finished,
        Func<CancellationToken, bool> cancel,
        Func<IEnumerable<Exception>, bool> fail,
        Action succeed)
    {
        if (finished.IsCanceled)
        {
            try
            {
                finished.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException cancelled)
            {
                cancel(cancelled.CancellationToken);
            }

            return;
        }

        if (finished.IsFaulted)
        {
            fail(finished.Exception!.InnerExceptions);
            return;
        }

        try
        {
            succeed();
        }
        catch (Exception exception)
        {
            fail([exception]);
        }
    }

    private static bool IsFailedValueTaskOfT(object result)
    {
        Type type = result.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ValueTask<>))
        {
            return false;
        }

        bool isCompleted = (bool)type.GetProperty(nameof(ValueTask.IsCompleted))!.GetValue(result)!;
        bool succeeded = (bool)type.GetProperty(nameof(ValueTask.IsCompletedSuccessfully))!.GetValue(result)!;
        return isCompleted && !succeeded;
    }

    private void RunProducers(object?[] arguments, MockArtifacts artifacts)
    {
        foreach (Action<object?[], MockArtifacts> producer in this._artifactProducers)
        {
            producer(arguments, artifacts);
        }
    }

    private protected void SetResponse(Func<object?[], MockArtifacts, object?> response)
    {
        this.EnsureOpen();

        if (this._response is not null)
        {
            throw new FrameworkConfigurationException(
                $"'{this.Pattern.Describe()}' already states its result; a setup states it once.")
            {
                RecoverySteps = ["Keep one of Returns, Compute, Throws or their async forms on this setup — or add a second mock.Call(...) if two behaviours are meant."],
            };
        }

        this._response = response;
    }

    private protected void AddArtifactProducer(Action<object?[], MockArtifacts> producer)
    {
        this.EnsureOpen();

        this._artifactProducers.Add(producer);
    }

    private void EnsureOpen()
    {
        if (this._sealed)
        {
            throw new FrameworkStateException(
                $"'{this.Pattern.Describe()}' was changed after its double was built; a setup is declared once, in Configure.")
            {
                RecoverySteps = ["State every Returns, Throws, Compute and ProducesArtifact inside Configure, and do not keep the setup object beyond it."],
            };
        }
    }

    /// <summary>
    /// Refuses a typed lambda whose parameters do not fit the mocked method — at declaration
    /// time, so the mistake surfaces where it was written and not at the first call.
    /// </summary>
    private protected void ValidateArgumentTypes(params Type[] lambdaArgumentTypes)
    {
        // The arguments a call brings: an out parameter brings none, and a ref one brings its value, typed as
        // the parameter without the ref.
        ParameterInfo[] parameters = [.. this.Pattern.Method.GetParameters().Where(static parameter => !parameter.IsOut)];
        bool fits = parameters.Length == lambdaArgumentTypes.Length
            && parameters.Zip(lambdaArgumentTypes).All(pair => pair.Second.IsAssignableFrom(ValueTypeOf(pair.First)));
        if (!fits)
        {
            throw new FrameworkConfigurationException(
                $"The lambda on '{this.Pattern.Describe()}' takes ({string.Join(", ", lambdaArgumentTypes.Select(t => t.Name))}), which does not fit the method.")
            {
                RecoverySteps = ["Match the lambda's parameters to the mocked method's signature."],
                AvailableOptions = [.. parameters.Select(p => $"{ValueTypeOf(p).Name} {p.Name}")],
            };
        }
    }

    private static Type ValueTypeOf(ParameterInfo parameter)
        => parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;

    private protected static T Argument<T>(object?[] arguments, int index)
    {
        return (T)arguments[index]!;
    }
}
