using TestFramework.Core.Conventions;

namespace TestFramework.Mock.Artifacts;

/// <summary>
/// What a mocked call leaves behind in the environment: an identity and the payload captured at
/// call time. The identity is how a timeline finds it — <c>FindArtifact(identity, new MockArtifactFinder(identity))</c> —
/// and a later publish of the same identity is what a later <c>CaptureArtifactVersion(identity)</c> sees.
/// </summary>
/// <param name="Identity">The name the payload is recorded, and found, under.</param>
/// <param name="Payload">The data captured when the mocked call happened.</param>
[ClosedShape("What a double produced is its identity and its payload; anything more about it belongs in the payload.")]
public sealed record MockArtifact(string Identity, object? Payload);
