using System;
using System.Threading.Tasks;

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
        mock.Call(f => f.CreateFile(Arg.Any<string>()))
            .Returns(true)
            .ProducesArtifact((string path) => new("createdFile", path));
    }
}

/// <summary>
/// A second pack for the same service, so the duplicate refusal has something to refuse.
/// </summary>
public sealed class OtherFileStorePack : MockDefinition<IFileStore>
{
    protected override void Configure(MockBuilder<IFileStore> mock)
    {
        mock.Call(f => f.CreateFile(Arg.Any<string>())).Returns(false);
    }
}
