using System;

namespace TestFramework.Mock;

/// <summary>
/// The untyped view of a Mock-Pack, so the environment can hold packs for different services in
/// one list. Nobody derives this directly — the constructor is reachable only from
/// <see cref="MockDefinition{TService}"/>, which is the authoring surface.
/// </summary>
public abstract class MockDefinitionGeneric
{
    private protected MockDefinitionGeneric()
    {
    }

    /// <summary>
    /// The service this pack replaces.
    /// </summary>
    internal abstract Type ServiceType { get; }

    /// <summary>
    /// One run's live double, untyped.
    /// </summary>
    internal abstract MockInstanceGeneric CreateInstanceGeneric();
}
