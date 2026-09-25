using TestFramework.Core.Exceptions;

namespace TestFramework.Mock.Artifacts;

/// <summary>
/// The artifact channel a setup lambda receives behind the call's own arguments.
///
/// Publishing records, in the mock host, what the call left behind — the double's part of the
/// environment, exactly as a real dependency would leave a file or a row. It writes nothing into
/// the run's artifact store: the call happens on a thread of the system under test, inside no step.
/// A timeline brings a publish into the run with <c>FindArtifact(identity, new MockArtifactFinder(identity))</c>
/// and takes a later look with <c>CaptureArtifactVersion(identity)</c>, the same verbs every other
/// outside artifact uses — so the pack never states whether something is a new artifact or a
/// version, and cannot state it wrongly.
/// </summary>
public sealed class MockArtifacts
{
    private readonly MockArtifactRecord _record;

    internal MockArtifacts(MockArtifactRecord record)
    {
        this._record = record;
    }

    /// <summary>
    /// Records what the call left behind under the artifact's identity, replacing an earlier
    /// publish of the same identity. Refused after the run has finished.
    /// </summary>
    public void Publish(MockArtifact artifact)
    {
        if (artifact is null)
        {
            throw new FrameworkConfigurationException(
                "A setup published a null artifact.",
                recoverySteps: ["Return a MockArtifact with an identity and the payload to capture."]);
        }

        this._record.Publish(artifact);
    }

    /// <summary>
    /// Convenience for <see cref="Publish(MockArtifact)"/>.
    /// </summary>
    public void Publish(string identity, object? payload)
    {
        this.Publish(new MockArtifact(identity, payload));
    }
}
