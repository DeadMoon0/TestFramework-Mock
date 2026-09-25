using System;
using System.Threading.Tasks;

namespace TestFramework.Mock;

/// <summary>
/// The async verbs, one set per task type. Extensions on the setup of an async method, so they exist
/// exactly where they make sense: <c>ReturnsAsync</c> on a method returning <c>int</c> does not compile.
/// A failing async call hands back a failed task instead of throwing at the call, the way a real async
/// method does - whoever awaits it gets the exception.
/// </summary>
public static class MockCallSetupAsync
{
    /// <summary>
    /// Every matching call returns a task that has already completed with this value.
    /// </summary>
    public static MockCallSetup<TService, Task<TResult>> ReturnsAsync<TService, TResult>(this MockCallSetup<TService, Task<TResult>> setup, TResult value)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);

        return setup.Returns(Task.FromResult(value));
    }

    /// <summary>
    /// Every matching call returns a value task that has already completed with this value.
    /// </summary>
    public static MockCallSetup<TService, ValueTask<TResult>> ReturnsAsync<TService, TResult>(this MockCallSetup<TService, ValueTask<TResult>> setup, TResult value)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);

        return setup.Returns(new ValueTask<TResult>(value));
    }

    /// <summary>
    /// Every matching call returns a task that has already completed - the answer for an async method
    /// that yields nothing.
    /// </summary>
    public static MockCallSetup<TService, Task> Completes<TService>(this MockCallSetup<TService, Task> setup)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);

        return setup.Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Every matching call returns a value task that has already completed.
    /// </summary>
    public static MockCallSetup<TService, ValueTask> Completes<TService>(this MockCallSetup<TService, ValueTask> setup)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);

        return setup.Returns(ValueTask.CompletedTask);
    }

    /// <summary>
    /// Every matching call returns a task that has already failed with this exception. Combining it with
    /// ProducesArtifact is refused, as with Throws: the artifact could never be published.
    /// </summary>
    public static MockCallSetup<TService, Task> ThrowsAsync<TService>(this MockCallSetup<TService, Task> setup, Exception exception)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(exception);

        return setup.FailsWith(() => Task.FromException(exception));
    }

    /// <summary>
    /// Every matching call returns a task that has already failed with this exception.
    /// </summary>
    public static MockCallSetup<TService, Task<TResult>> ThrowsAsync<TService, TResult>(this MockCallSetup<TService, Task<TResult>> setup, Exception exception)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(exception);

        return setup.FailsWith(() => Task.FromException<TResult>(exception));
    }

    /// <summary>
    /// Every matching call returns a value task that has already failed with this exception.
    /// </summary>
    public static MockCallSetup<TService, ValueTask> ThrowsAsync<TService>(this MockCallSetup<TService, ValueTask> setup, Exception exception)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(exception);

        return setup.FailsWith(() => ValueTask.FromException(exception));
    }

    /// <summary>
    /// Every matching call returns a value task that has already failed with this exception.
    /// </summary>
    public static MockCallSetup<TService, ValueTask<TResult>> ThrowsAsync<TService, TResult>(this MockCallSetup<TService, ValueTask<TResult>> setup, Exception exception)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(exception);

        return setup.FailsWith(() => ValueTask.FromException<TResult>(exception));
    }
}
