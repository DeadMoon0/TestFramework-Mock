namespace TestFramework.Mock;

/// <summary>
/// The resource kind strings this package defines. The mock host publishes no resource values —
/// it has no coordinates and nothing connects to it — so the kind exists purely so a step can
/// require it and the environment knows to start the host.
/// </summary>
public static class MockResourceKinds
{
    /// <summary>
    /// The in-process host that composes the system under test with its packs applied.
    /// </summary>
    public const string Host = "mock.host";

    /// <summary>
    /// The one host's identifier; a run hosts one composition.
    /// </summary>
    public const string HostIdentifier = "host";
}
