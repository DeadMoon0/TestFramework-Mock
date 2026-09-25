using System;

using TestFramework.Core.Artifacts;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// Reads over a finished run: the doubles it ran against, what an in-process call returned, and
/// what a double's publish became once the timeline found it.
/// </summary>
public static class MockRunExtensions
{
    /// <summary>
    /// The double one service ran against — the verification read for a finished run.
    /// </summary>
    /// <typeparam name="TService">The replaced service.</typeparam>
    /// <param name="run">The run.</param>
    /// <returns>The double, frozen with its run.</returns>
    public static MockInstance<TService> Mock<TService>(this TimelineRun run)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.EnvironmentContext.TryGetState(MockHostEnvComponent.ComponentId, out MockHostState? state) && state is not null)
        {
            return state.Mock<TService>();
        }

        throw new FrameworkConfigurationException(
            "This run had no mock host, so it ran against no doubles.",
            recoverySteps: ["Set the environment on the run: SetupRun(...).SetEnv(MockEnvironment.For(...))."]);
    }

    /// <summary>
    /// What an in-process call step returned.
    /// </summary>
    /// <typeparam name="TResult">The call's return type.</typeparam>
    /// <param name="run">The run.</param>
    /// <param name="label">The step label.</param>
    /// <returns>The returned value.</returns>
    public static TResult MockResult<TResult>(this TimelineRun run, string label)
    {
        ArgumentNullException.ThrowIfNull(run);

        object? last = run.Step(label).LastResult.Result;
        if (last is MockCallResult<TResult> result)
        {
            return result.Value;
        }

        throw new FrameworkConfigurationException(
            last is null
                ? $"Step '{label}' returned no value to read as {typeof(TResult).Name}."
                : $"Step '{label}' returned {MockValueText.DescribeType(last.GetType())}, not {nameof(MockCallResult<TResult>)}<{typeof(TResult).Name}>.",
            recoverySteps: [
                "Read it with the call's own return type — for an awaited call, what its task yields.",
                "A Host step for a void method or a plain Task returns no value to read.",
            ]);
    }

    /// <summary>
    /// A found mock artifact, typed — what <c>FindArtifact(identity, new MockArtifactFinder(identity))</c>
    /// brought into the run, one version per look the timeline took.
    /// </summary>
    /// <param name="store">The run's artifact store.</param>
    /// <param name="identifier">The identifier the artifact was found as.</param>
    /// <returns>The artifact instance.</returns>
    public static ArtifactInstance<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference> GetMockArtifact(
        this ArtifactStore store,
        ArtifactIdentifier identifier)
    {
        ArgumentNullException.ThrowIfNull(store);

        return store.GetArtifact<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference>(identifier);
    }
}
