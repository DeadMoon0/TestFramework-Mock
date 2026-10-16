using System;
using TestFramework.Core.Steps;

namespace TestFramework.Mock;

/// <summary>
/// The untyped view of a pack, so the environment can hold packs for different services in
/// one list. Nobody derives this directly — the constructor is reachable only from
/// <see cref="MockPack{TService}"/>, which is the authoring surface.
/// </summary>
public abstract class MockPackGeneric
{
    private protected MockPackGeneric()
    {
    }

    /// <summary>
    /// The service this pack replaces.
    /// </summary>
    internal abstract Type ServiceType { get; }

    /// <summary>
    /// One run's live double, untyped.
    /// </summary>
    internal abstract MockInstanceGeneric CreateInstanceGeneric(RunContext context);

    /// <summary>
    /// Refuses a pack that could never become a double, while it is included.
    /// </summary>
    internal abstract void ValidateShape();
}
