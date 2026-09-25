using System.Threading.Tasks;

using TestFramework.Core.Artifacts;
using TestFramework.Core.Logging;
using TestFramework.Core.Steps;

namespace TestFramework.Mock.Artifacts;

/// <summary>
/// Finds what the run's doubles published under one identity, for Core's <c>FindArtifact</c> verb.
/// The same role a folder finder plays for files a system wrote: the environment holds the payload,
/// the finding step brings it into the run under its own context, and <c>CaptureArtifactVersion</c>
/// takes a later look at the same identity.
/// </summary>
/// <remarks>
/// Finding reads; it consumes nothing. A retried find, or a second look, sees what the environment
/// holds at that moment.
/// </remarks>
/// <param name="identity">The identity a pack's <c>ProducesArtifact</c> or <c>Publish</c> stated.</param>
public sealed class MockArtifactFinder(string identity)
    : ArtifactFinder<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference>
{
    /// <summary>
    /// Returns the published identity, or <see langword="null"/> when no double has published it yet.
    /// </summary>
    public override Task<ArtifactFinderResult?> FindAsync(RunContext context)
    {
        if (!MockHost.StateOf(context).TryGetLatestPublish(identity, out _))
        {
            context.Logger.LogWarning($"No double has published '{identity}' yet.");
            return Task.FromResult<ArtifactFinderResult?>(null);
        }

        return Task.FromResult<ArtifactFinderResult?>(new ArtifactFinderResult(new MockCapturedArtifactReference(identity)));
    }

    /// <summary>
    /// The published identity as a one-element list, or an empty one when nothing published it.
    /// </summary>
    public override async Task<ArtifactFinderResultMulti> FindMultiAsync(RunContext context)
    {
        ArtifactFinderResult? found = await this.FindAsync(context);
        return new ArtifactFinderResultMulti(found is null ? [] : [found]);
    }
}
