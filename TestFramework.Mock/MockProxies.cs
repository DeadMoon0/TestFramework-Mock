using Castle.DynamicProxy;

namespace TestFramework.Mock;

/// <summary>
/// The one proxy generator every double is made with.
/// </summary>
/// <remarks>
/// Castle caches the proxy types it generates per generator. A static field on the generic
/// <see cref="MockInstance{TService}"/> would have been one generator per mocked service - a cache that is
/// never shared - so the generator lives here, where there is exactly one.
/// </remarks>
internal static class MockProxies
{
    internal static readonly ProxyGenerator Generator = new();
}
