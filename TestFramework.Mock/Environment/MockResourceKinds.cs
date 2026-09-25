using TestFramework.Core.Environment.Graph;

namespace TestFramework.Mock;

/// <summary>
/// The resource kind this package defines: one resource per service the mock host can hand a
/// <c>Host</c> step, named by the service's full type name. It carries no values — nothing connects to an
/// in-process service — so it exists to be required, which is what lets the engine check before the run
/// starts that the service a step calls is registered at all.
/// </summary>
public static class MockResourceKinds
{
    /// <summary>
    /// A service registered in the composition a <see cref="MockEnvironment"/> hosts.
    /// </summary>
    public const string Host = "mock.host";

    /// <summary>
    /// The kind itself, for declaring and requiring hosted services.
    /// </summary>
    public static readonly ResourceKind HostKind = ResourceKind.Named(Host).Build();
}
