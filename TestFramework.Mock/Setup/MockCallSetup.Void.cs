using System;

using TestFramework.Mock.Artifacts;
using TestFramework.Mock.Matching;

namespace TestFramework.Mock;

/// <summary>
/// A setup for a void call. A body is optional — doing nothing is a legitimate void behaviour —
/// but at most one is stated, and artifact producers stack. This file holds the arity-free verbs;
/// the typed overloads per argument count are generated (see <c>MockCallSetup.Arities.tt</c>).
/// </summary>
/// <typeparam name="TService">The mocked service.</typeparam>
public sealed partial class MockCallSetup<TService> : MockCallSetupBase
    where TService : class
{
    internal MockCallSetup(CallPattern pattern)
        : base(pattern)
    {
    }

    /// <summary>
    /// Runs on every matching call.
    /// </summary>
    public MockCallSetup<TService> Callback(Action action)
    {
        this.ValidateArgumentTypes();
        this.SetResponse((_, _) =>
        {
            action();
            return null;
        });
        return this;
    }

    /// <summary>
    /// The primitive form: the artifact channel, for a call without arguments.
    /// </summary>
    public MockCallSetup<TService> Compute(Action<MockArtifacts> compute)
    {
        this.ValidateArgumentTypes();
        this.SetResponse((_, artifacts) =>
        {
            compute(artifacts);
            return null;
        });
        return this;
    }

    /// <summary>
    /// Every matching call throws this exception. Combining it with ProducesArtifact on the same
    /// setup is refused at instance creation — the artifact could never be published.
    /// </summary>
    public MockCallSetup<TService> Throws(Exception exception)
    {
        this.SetThrows(exception);
        return this;
    }

    /// <summary>
    /// Every matching call that completes publishes this artifact; a call that throws is never
    /// assumed to have produced it. Stackable. Bringing it into the run is the timeline's, through
    /// Core's FindArtifact and CaptureArtifactVersion.
    /// </summary>
    public MockCallSetup<TService> ProducesArtifact(Func<MockArtifact> produce)
    {
        this.ValidateArgumentTypes();
        this.AddArtifactProducer((_, artifacts) => artifacts.Publish(produce()));
        return this;
    }

}
