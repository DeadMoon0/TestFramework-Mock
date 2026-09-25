using System;
using System.Threading.Tasks;

using TestFramework.Mock;

namespace TestFramework.Mock.Tests.Fixtures;

/// <summary>
/// A neutral dependency shape for the engine tests — deliberately no customer context.
/// </summary>
public interface IFileStore
{
    bool CreateFile(string path);

    string ReadText(string path);

    int Copy(string from, string to);

    int Move(string from, string to, bool overwrite);

    void Delete(string path);
}

/// <summary>
/// Parameter shapes the matchers have to get right: a parameter wider than the matcher's type, a
/// collection, and a nullable value type.
/// </summary>
public interface IValueSink
{
    int Put(object? value);

    int Bytes(byte[] data);

    int Maybe(int? value);
}

/// <summary>
/// The four task shapes an async dependency returns.
/// </summary>
public interface IAsyncSink
{
    Task<int> GetAsync();

    ValueTask<int> GetValueAsync();

    Task SendAsync(string what);

    ValueTask SendValueAsync();
}

/// <summary>
/// A second dependency, so two doubles can publish under one identity.
/// </summary>
public interface IAuditLog
{
    void Write(string entry);
}

/// <summary>
/// The non-interface case for the refusal test.
/// </summary>
public abstract class FileStoreBase
{
    public abstract bool CreateFile(string path);
}

/// <summary>
/// Lets a test state a pack inline; real packs are one named class per definition.
/// </summary>
public sealed class InlinePack<TService> : MockDefinition<TService>
    where TService : class
{
    private readonly Action<MockBuilder<TService>> _configure;

    public InlinePack(Action<MockBuilder<TService>> configure)
    {
        this._configure = configure;
    }

    public MockInstance<TService> Create()
    {
        return this.CreateInstance();
    }

    protected override void Configure(MockBuilder<TService> mock)
    {
        this._configure(mock);
    }
}
