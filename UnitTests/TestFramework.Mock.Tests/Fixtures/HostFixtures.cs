using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Mock;

namespace TestFramework.Mock.Tests.Fixtures;

/// <summary>
/// A neutral system under test: one service with one injected dependency, the shape a user's
/// composition registers in production.
/// </summary>
public sealed class ReportService(IFileStore files)
{
    public bool Save(string name)
    {
        return files.CreateFile(name);
    }

    public void Remove(string name)
    {
        files.Delete(name);
    }
}

/// <summary>
/// The same system under test, the way most services are written: every call reaches its
/// dependency only after the method has already yielded, so a trigger that does not await it
/// finishes before the dependency is called at all.
/// </summary>
public sealed class AsyncReportService(IFileStore files)
{
    public async Task<bool> SaveAsync(string name)
    {
        await Task.Delay(50);
        return files.CreateFile(name);
    }

    public async Task TouchAsync(string name)
    {
        await Task.Delay(50);
        files.CreateFile(name);
    }

    public async Task FailAsync()
    {
        await Task.Delay(50);
        throw new InvalidOperationException("failed after the first await");
    }

    public ValueTask<bool> SaveValueAsync(string name)
    {
        return new ValueTask<bool>(files.CreateFile(name));
    }
}

/// <summary>
/// The composition's "real" dependency. Every member throws, so a test that reaches it proves the
/// pack's double did not stand where it should.
/// </summary>
public sealed class UnreachableFileStore : IFileStore
{
    public bool CreateFile(string path)
    {
        throw Unreachable();
    }

    public string ReadText(string path)
    {
        throw Unreachable();
    }

    public int Copy(string from, string to)
    {
        throw Unreachable();
    }

    public int Move(string from, string to, bool overwrite)
    {
        throw Unreachable();
    }

    public void Delete(string path)
    {
        throw Unreachable();
    }

    private static InvalidOperationException Unreachable()
    {
        return new InvalidOperationException("The real file store must never be reached; the pack's double stands here.");
    }
}

/// <summary>
/// The pack under test: answers CreateFile and captures the path as an artifact.
/// </summary>
public sealed class FileStorePack : MockDefinition<IFileStore>
{
    protected override void Configure(MockBuilder<IFileStore> mock)
    {
        mock.Call(f => f.CreateFile(MockArg.Any<string>()))
            .Returns(true)
            .ProducesArtifact((string path) => new("createdFile", path));
        mock.Call(f => f.Delete(MockArg.Any<string>()));
    }
}

/// <summary>
/// A second pack for the same service, so the duplicate refusal has something to refuse.
/// </summary>
public sealed class OtherFileStorePack : MockDefinition<IFileStore>
{
    protected override void Configure(MockBuilder<IFileStore> mock)
    {
        mock.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(false);
    }
}

/// <summary>
/// A system under test that takes its dependency by key - the registration a plain replacement used
/// to leave standing.
/// </summary>
public sealed class KeyedReportService([FromKeyedServices("archive")] IFileStore archive)
{
    public bool Archive(string name)
    {
        return archive.CreateFile(name);
    }
}

/// <summary>
/// A system under test that still talks to its dependency while it is being disposed, as anything
/// that flushes on dispose does.
/// </summary>
public sealed class FlushingReportService(IFileStore files) : IDisposable
{
    public bool Save(string name)
    {
        return files.CreateFile(name);
    }

    public void Dispose()
    {
        files.CreateFile("flushed-on-dispose.txt");
    }
}

/// <summary>
/// A pack that keeps state in a field: it answers true only the first time its Configure runs. Shared
/// between runs, the second run would be answered false.
/// </summary>
public sealed class CountingPack : MockDefinition<IFileStore>
{
    private int _configured;

    protected override void Configure(MockBuilder<IFileStore> mock)
    {
        this._configured++;
        bool firstConfigure = this._configured == 1;
        mock.Call(f => f.CreateFile(MockArg.Any<string>())).Returns(firstConfigure);
    }
}

/// <summary>
/// A pack for a second service, for refusals that need a pack other than the one already included.
/// </summary>
public sealed class AuditLogPack : MockDefinition<IAuditLog>
{
    protected override void Configure(MockBuilder<IAuditLog> mock)
    {
        mock.Call(a => a.Write(MockArg.Any<string>()));
    }
}

/// <summary>
/// A system under test whose calls take a cancellation token, as well-behaved async services do.
/// </summary>
public sealed class CancellableReportService
{
    public Task<bool> TokenCanBeCancelledAsync(CancellationToken cancellation)
    {
        return Task.FromResult(cancellation.CanBeCanceled);
    }

    public async Task WaitUntilCancelledAsync(CancellationToken cancellation)
    {
        await Task.Delay(Timeout.Infinite, cancellation);
    }
}

/// <summary>
/// Counts how many scoped workers have been disposed - a singleton, so it outlives every scope.
/// </summary>
public sealed class DisposalLog
{
    private int _disposed;

    public int Disposed => Volatile.Read(ref this._disposed);

    public void Record()
    {
        Interlocked.Increment(ref this._disposed);
    }
}

/// <summary>
/// A scoped service, the way most of an application's services are registered.
/// </summary>
public sealed class ScopedWorker(DisposalLog log) : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();

    public void Dispose()
    {
        log.Record();
    }
}
