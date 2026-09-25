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
    private Func<object?[], MockArtifacts, object?>? _response;
    private volatile bool _sealed;

    private protected MockCallSetupBase(CallPattern pattern)
    {
        this.Pattern = pattern;
    }

    internal CallPattern Pattern { get; }

    internal bool HasResponse => this._response is not null;

    internal bool DeclaresUnreachableArtifacts => this.AlwaysFails && this._artifactProducers.Count > 0;

    /// <summary>
    /// Whether every call fails - by throwing, or by handing back a task that has already failed.
    /// </summary>
    private bool AlwaysFails { get; set; }

    /// <summary>
    /// Closes the declaration once the double is built. A setup object can outlive <c>Configure</c> -
    /// a pack may keep the reference <c>mock.Call</c> returned - and changing it afterwards would
    /// change a double that is already answering calls, racing the calls it is answering.
    /// </summary>
    internal void Seal()
    {
        this._sealed = true;
    }

    internal object? Invoke(object?[] arguments, MockArtifacts artifacts)
    {
        // The result runs first: a declared artifact is what a completed call produced, so a
        // call that throws is never assumed to have produced it. What a body published by hand
        // before throwing stands — those publishes happened.
        object? result = this._response is null ? null : this._response(arguments, artifacts);
        if (this._artifactProducers.Count > 0)
        {
            this.ProduceWhenCompleted(result, () => this.RunProducers(arguments, artifacts));
        }

        return result;
    }

    private protected void SetThrows(Exception exception)
    {
        this.SetResponse((_, _) => throw exception);
        this.AlwaysFails = true;
    }

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
    private void ProduceWhenCompleted(object? result, Action produce)
    {
        switch (result)
        {
            case Task { IsCompleted: true } finished:
                if (finished.IsCompletedSuccessfully)
                {
                    produce();
                }

                break;
            case Task pending:
                pending.ContinueWith(
                    _ => produce(),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                break;
            case ValueTask { IsCompleted: true } finished:
                if (finished.IsCompletedSuccessfully)
                {
                    produce();
                }

                break;
            default:
                if (result is null || !IsFailedValueTaskOfT(result))
                {
                    produce();
                }

                break;
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
                $"'{this.Pattern.Describe()}' already states its result; a setup states it once.",
                recoverySteps: ["Keep one of Returns, Compute, Throws or their async forms on this setup — or add a second mock.Call(...) if two behaviours are meant."]);
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
                $"'{this.Pattern.Describe()}' was changed after its double was built; a setup is declared once, in Configure.",
                recoverySteps: ["State every Returns, Throws, Compute and ProducesArtifact inside Configure, and do not keep the setup object beyond it."]);
        }
    }

    /// <summary>
    /// Refuses a typed lambda whose parameters do not fit the mocked method — at declaration
    /// time, so the mistake surfaces where it was written and not at the first call.
    /// </summary>
    private protected void ValidateArgumentTypes(params Type[] lambdaArgumentTypes)
    {
        ParameterInfo[] parameters = this.Pattern.Method.GetParameters();
        bool fits = parameters.Length == lambdaArgumentTypes.Length
            && parameters.Zip(lambdaArgumentTypes).All(pair => pair.Second.IsAssignableFrom(pair.First.ParameterType));
        if (!fits)
        {
            throw new FrameworkConfigurationException(
                $"The lambda on '{this.Pattern.Describe()}' takes ({string.Join(", ", lambdaArgumentTypes.Select(t => t.Name))}), which does not fit the method.",
                recoverySteps: ["Match the lambda's parameters to the mocked method's signature."],
                availableOptions: [.. parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")]);
        }
    }

    private protected static T Argument<T>(object?[] arguments, int index)
    {
        return (T)arguments[index]!;
    }
}
