using System.Collections.Concurrent;

using TestFramework.Core.Exceptions;

namespace TestFramework.Mock.Artifacts;

/// <summary>
/// What one mock instance's calls have left behind, by identity — the environment's own record,
/// the way a folder holds the files a system wrote into it. A later publish under an identity
/// replaces the earlier one, as a second write to a file does.
///
/// Nothing here reaches the run's stores. The double is called on a thread of the system under
/// test, inside no step, so it has nothing to write to the run with; a timeline brings what is
/// recorded here into the run the way it brings in anything an outside system produced — through
/// Core's finder and version verbs, from a step, under that step's own context.
///
/// Thread-safe, because the system under test calls its dependencies from whatever thread it likes.
/// </summary>
internal sealed class MockArtifactRecord
{
    private readonly ConcurrentDictionary<string, object?> _latestByIdentity = new();
    private volatile bool _frozen;

    public void Publish(MockArtifact artifact)
    {
        if (this._frozen)
        {
            throw new FrameworkStateException(
                $"The run is finished; artifact '{artifact.Identity}' can no longer be published.");
        }

        this._latestByIdentity[artifact.Identity] = artifact.Payload;
    }

    public bool TryGetLatest(string identity, out object? payload)
    {
        return this._latestByIdentity.TryGetValue(identity, out payload);
    }

    public void FreezeForRunEnd()
    {
        this._frozen = true;
    }
}
