using System;

using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// A setup for a call that returns <typeparamref name="TResult"/>. The result must be stated —
/// a mocked call never invents a return value — and artifact producers stack.
///
/// This file holds the arity-free verbs; the typed overloads per argument count are generated
/// (see <c>MockCallSetup.Arities.tt</c>). Their parameter types are checked against the mocked
/// method when the setup is declared.
/// </summary>
/// <typeparam name="TService">The mocked service.</typeparam>
/// <typeparam name="TResult">The call's return type.</typeparam>
public sealed partial class MockCallSetup<TService, TResult> : MockCallSetupBase
    where TService : class
{
    internal MockCallSetup(CallPattern pattern)
        : base(pattern)
    {
    }

    /// <summary>
    /// Every matching call returns this value.
    /// </summary>
    public MockCallSetup<TService, TResult> Returns(TResult value)
    {
        this.SetResponse((_, _) => value);
        return this;
    }

    /// <summary>
    /// The primitive form: result and artifact channel in one body, the channel behind the
    /// call's own arguments. <c>Returns</c> and <c>ProducesArtifact</c> are the declarative
    /// spellings of what this can express by hand.
    /// </summary>
    public MockCallSetup<TService, TResult> Compute(Func<MockArtifacts, TResult> compute)
    {
        this.ValidateArgumentTypes();
        this.SetResponse((_, artifacts) => compute(artifacts));
        return this;
    }

    /// <summary>
    /// Every matching call throws this exception. Combining it with ProducesArtifact on the same
    /// setup is refused at instance creation — the artifact could never be published. A body
    /// that wants to publish and then fail states both by hand in Compute.
    /// </summary>
    public MockCallSetup<TService, TResult> Throws(Exception exception)
    {
        this.SetThrows(exception);
        return this;
    }

    /// <summary>
    /// Every matching call hands back an already-failed task - the async form of <see cref="Throws"/>,
    /// reached through the async verbs so it only exists where the result is a task.
    /// </summary>
    internal MockCallSetup<TService, TResult> FailsWith(Func<TResult> failedTask)
    {
        this.SetFailedTask(() => failedTask()!);
        return this;
    }

    /// <summary>
    /// Every matching call that completes publishes this artifact; a call that throws is never
    /// assumed to have produced it. Stackable — declare it again for a second artifact per call.
    /// The pack states what the call leaves behind; bringing it into the run, and when to look
    /// again, is the timeline's, through Core's FindArtifact and CaptureArtifactVersion.
    /// </summary>
    public MockCallSetup<TService, TResult> ProducesArtifact(Func<MockArtifact> produce)
    {
        this.ValidateArgumentTypes();
        this.AddArtifactProducer((_, artifacts) => artifacts.Publish(produce()));
        return this;
    }

}
