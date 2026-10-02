using System.Collections.Generic;

using TestFramework.Core.Runner;
using TestFramework.Core.Steps;

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
/// It is kept as a <see cref="RunRecord{T}"/>, so it follows the run's own rules: it finishes when the run
/// does, and a publish after that is dropped and reported rather than thrown into the system under test.
/// </summary>
internal sealed class MockArtifactRecord
{
    private readonly RunRecord<MockArtifact> _published;

    public MockArtifactRecord(RunContext context)
    {
        this._published = RunRecord<MockArtifact>.Open(context, "mock artifacts");
    }

    public void Publish(MockArtifact artifact)
    {
        this._published.Append(artifact);
    }

    public bool TryGetLatest(string identity, out object? payload)
    {
        IReadOnlyList<MockArtifact> published = this._published.Snapshot();

        for (int index = published.Count - 1; index >= 0; index--)
        {
            if (string.Equals(published[index].Identity, identity, System.StringComparison.Ordinal))
            {
                payload = published[index].Payload;
                return true;
            }
        }

        payload = null;
        return false;
    }
}
