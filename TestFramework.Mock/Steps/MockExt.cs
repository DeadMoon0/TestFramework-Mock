namespace TestFramework.Mock;

/// <summary>
/// The package's timeline verbs. <c>Host</c> calls the hosted system under test — deliberately not
/// named <c>Call</c>, which on a pack's builder means the opposite: setting up a double.
/// </summary>
/// <remarks>
/// Every <c>Host</c> overload is generated per argument count (see <c>MockExt.Arities.tt</c>): a
/// synchronous result, an awaited <c>Task&lt;T&gt;</c>, an awaited <c>Task</c>, a synchronous void call,
/// and the two awaited shapes with the step's cancellation token. Each call runs in a scope of its own,
/// the way production opens one per request.
/// </remarks>
public static partial class MockExt
{
}
