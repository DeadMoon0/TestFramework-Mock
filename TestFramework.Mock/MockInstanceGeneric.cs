using System;

namespace TestFramework.Mock;

/// <summary>
/// The untyped view of one run's live double, so the host can hold doubles for different services
/// in one map. The typed surface — the proxy, the log, the verification reads — lives on
/// <see cref="MockInstance{TService}"/>.
/// </summary>
public abstract class MockInstanceGeneric
{
    private protected MockInstanceGeneric()
    {
    }

    /// <summary>
    /// The service this double stands in for.
    /// </summary>
    internal abstract Type ServiceType { get; }

    /// <summary>
    /// The proxy, as the object the service registration needs.
    /// </summary>
    internal abstract object ProxyObject { get; }

    /// <summary>
    /// What was last published under an identity — what a finder finds and a captured artifact's
    /// reference re-resolves through.
    /// </summary>
    internal abstract bool TryGetLatestPublish(string identity, out object? payload);

    /// <summary>
    /// Closes the double with its run.
    /// </summary>
    internal abstract void FreezeForRunEnd();
}
