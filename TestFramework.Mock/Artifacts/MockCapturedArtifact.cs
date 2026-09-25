using System;
using System.Threading.Tasks;

using TestFramework.Core.Artifacts;
using TestFramework.Core.Exceptions;
using TestFramework.Core.Steps;
using TestFramework.Core.Steps.Options;

namespace TestFramework.Mock.Artifacts;

/// <summary>
/// One captured payload: what the setup body handed to <see cref="MockArtifacts.Publish(MockArtifact)"/>,
/// as it was at call time. Repeated publishes under one identity are this artifact's versions.
/// </summary>
public class MockCapturedArtifactData(object? payload)
    : ArtifactData<MockCapturedArtifactData, MockCapturedArtifactDescriber, MockCapturedArtifactReference>
{
    /// <summary>
    /// Gets the payload as it was when the mocked call happened.
    /// </summary>
    public object? Payload => payload;

    /// <inheritdoc />
    public override string ToString() => payload switch
    {
        null => "Captured (null)",
        string text => $"Captured \"{text}\"",
        _ => $"Captured {payload}",
    };
}

/// <summary>
/// The lifecycle of a captured mock artifact: it is never set up — it comes into being when the
/// double is called — and its cleanup is deliberately void, because nothing outside the run
/// exists to remove.
/// </summary>
public class MockCapturedArtifactDescriber
    : ArtifactDescriber<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference>
{
    /// <inheritdoc />
    public override Task Setup(RunContext context, MockCapturedArtifactData data, MockCapturedArtifactReference reference)
    {
        throw new FrameworkConfigurationException(
            "A captured mock artifact is not set up by the run; it comes into being when the double is called.",
            recoverySteps: ["Publish it from a setup body or ProducesArtifact, then bring it into the run with FindArtifact(identity, new MockArtifactFinder(identity))."]);
    }

    /// <inheritdoc />
    public override Task Deconstruct(RunContext context, MockCapturedArtifactReference reference)
    {
        // Void by design: the payload is the run's own capture, so teardown has nothing to remove.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override string ToString() => "Mock Capture";
}

/// <summary>
/// Where a captured mock artifact lives: in the run's own mock host, under its identity. Resolving
/// re-reads the latest payload published under that identity, which is what lets the timeline's
/// version verb work against it like against any other artifact.
/// </summary>
public class MockCapturedArtifactReference
    : ArtifactReference<MockCapturedArtifactReference, MockCapturedArtifactDescriber, MockCapturedArtifactData>
{
    private readonly string _identity;

    /// <summary>
    /// Creates the reference for one published identity.
    /// </summary>
    public MockCapturedArtifactReference(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        this._identity = identity;

        // Deconstruction is possible — it just removes nothing; see the describer.
        this.CanDeconstruct = true;
    }

    /// <summary>
    /// Gets the identity the payloads were published under.
    /// </summary>
    public string Identity => this._identity;

    /// <inheritdoc />
    public override void OnPinReference(RunContext context)
    {
        // Nothing variable-backed to resolve: the identity is a literal the pack stated.
    }

    /// <inheritdoc />
    public override void DeclareIO(StepIOContract contract)
    {
    }

    /// <inheritdoc />
    public override Task<ArtifactResolveResult<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference>> ResolveToDataAsync(
        RunContext context,
        ArtifactVersionIdentifier versionIdentifier)
    {
        ArtifactResolveResult<MockCapturedArtifactDescriber, MockCapturedArtifactData, MockCapturedArtifactReference> result;
        if (context.State.TryGet(out MockHostState? host)
            && host is not null
            && host.TryGetLatestPublish(this._identity, out object? payload))
        {
            result = new() { Found = true, Data = new MockCapturedArtifactData(payload) { Identifier = versionIdentifier } };
        }
        else
        {
            result = new() { Found = false, Data = null };
        }

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public override string ToString() => $"Mock capture \"{this._identity}\"";
}
