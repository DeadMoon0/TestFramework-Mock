using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using TestFramework.Core.Exceptions;
using TestFramework.Core.Timelines;
using TestFramework.Core.Variables;
using TestFramework.Mock.Tests.Fixtures;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// The generated arities: every verb exists for every argument count up to the template's cap, and
/// behaves at a higher arity exactly as the hand-written low ones did.
/// </summary>
public class AritiesTests(ITestOutputHelper output)
{
    private const int MaxArity = 8;

    [Fact]
    public void EveryVerb_ExistsForEveryArity_UpToTheCap()
    {
        for (int arity = 1; arity <= MaxArity; arity++)
        {
            AssertSingleGenericOverload(typeof(MockCallSetup<,>), "Returns", arity);
            AssertSingleGenericOverload(typeof(MockCallSetup<,>), "Compute", arity);
            AssertSingleGenericOverload(typeof(MockCallSetup<,>), "ProducesArtifact", arity);
            AssertSingleGenericOverload(typeof(MockCallSetup<>), "Callback", arity);
            AssertSingleGenericOverload(typeof(MockCallSetup<>), "Compute", arity);
            AssertSingleGenericOverload(typeof(MockCallSetup<>), "ProducesArtifact", arity);
        }
    }

    [Fact]
    public void Host_HasEveryShape_ForEveryArgumentCount_FromNoneToTheCap()
    {
        // Six shapes per count, so a call always binds to an overload that treats it correctly: a
        // missing shape silently hands the call to another one - a task returned unawaited, a void
        // call that does not compile, a token the call never gets.
        string[] expected =
        [
            "Func -> TResult",
            "Func -> Task<TResult>",
            "Func -> Task",
            "Action",
            "Func + CancellationToken -> Task<TResult>",
            "Func + CancellationToken -> Task",
        ];

        for (int arity = 0; arity <= MaxArity; arity++)
        {
            string[] shapes = [.. typeof(MockExt).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "Host" && method.GetParameters().Length == arity + 1)
                .Select(method => ShapeOf(method.GetParameters()[^1].ParameterType))
                .OrderBy(shape => shape)];

            Assert.Equal(expected.OrderBy(shape => shape).ToArray(), shapes);
        }
    }

    [Fact]
    public void AThreeArgumentSetup_ReturnsAndPublishes_LikeTheLowArities()
    {
        MockInstance<IFileStore> mock = new InlinePack<IFileStore>(m =>
            m.Call(f => f.Move(MockArg.Any<string>(), MockArg.Any<string>(), MockArg.Any<bool>()))
                .Returns((string from, string to, bool overwrite) => overwrite ? 1 : 0)
                .ProducesArtifact((string from, string to, bool overwrite) => new("movedFile", $"{from} -> {to}"))).Create();

        Assert.Equal(1, mock.Object.Move("a.txt", "b.txt", true));
        Assert.Equal(0, mock.Object.Move("c.txt", "d.txt", false));

        Assert.True(mock.TryGetLatestPublish("movedFile", out object? moved));
        Assert.Equal("c.txt -> d.txt", moved);
    }

    [Fact]
    public void AMistypedThreeArgumentLambda_IsStillRefusedWhereItWasDeclared()
    {
        InlinePack<IFileStore> pack = new(m =>
            m.Call(f => f.Move(MockArg.Any<string>(), MockArg.Any<string>(), MockArg.Any<bool>()))
                .Returns((string from, string to, int wrong) => wrong));

        FrameworkConfigurationException refusal = Assert.Throws<FrameworkConfigurationException>(() => pack.Create());
        Assert.Contains("does not fit the method", refusal.Message);
    }

    [Fact]
    public async Task HostArguments_BindFromRunVariables_AndTheIOContractCarriesThem()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("fileName", Var.Const("from-variable.txt"))
            .Trigger(MockExt.Host(
                Var.Ref<string>("fileName"),
                (string name, ReportService service) => service.Save(name)))
                .Name("save")
            .Build();
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<ReportService>();
        }).Include<FileStorePack>();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.MockResult<bool>("save"));
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("from-variable.txt")));
    }

    [Fact]
    public async Task HostArguments_BindFromRunVariables_ForAnAwaitedCallToo()
    {
        Timeline timeline = Timeline.Create()
            .SetVariable("fileName", Var.Const("awaited.txt"))
            .Trigger(MockExt.Host(
                Var.Ref<string>("fileName"),
                (string name, AsyncReportService service) => service.SaveAsync(name)))
                .Name("save")
            .Build();
        MockEnvironment environment = MockEnvironment.For(services =>
        {
            services.AddSingleton<IFileStore, UnreachableFileStore>();
            services.AddSingleton<AsyncReportService>();
        }).Include<FileStorePack>();

        TimelineRun run = await timeline.SetupRun(null, output).SetEnv(environment).RunAsync();

        run.EnsureRanToCompletion();
        Assert.True(run.MockResult<bool>("save"));
        Assert.Equal(1, run.Mock<IFileStore>().CountCalls(f => f.CreateFile("awaited.txt")));
    }

    private static string ShapeOf(Type call)
    {
        Type[] parts = call.GetGenericArguments();
        if (call.Name.StartsWith("Action", StringComparison.Ordinal))
        {
            return "Action";
        }

        bool takesToken = parts.Length >= 2 && parts[^2] == typeof(System.Threading.CancellationToken);
        Type result = parts[^1];
        string resultName = result.IsGenericType ? $"{result.Name[..result.Name.IndexOf('`')]}<{result.GetGenericArguments()[0].Name}>" : result.Name;
        return takesToken ? $"Func + CancellationToken -> {resultName}" : $"Func -> {resultName}";
    }

    private static void AssertSingleGenericOverload(Type type, string methodName, int arity)
    {
        int found = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Count(method => method.Name == methodName
                && method.IsGenericMethodDefinition
                && method.GetGenericArguments().Length == arity);
        Assert.True(found == 1, $"{type.Name}.{methodName} with {arity} type argument(s): expected exactly one overload, found {found}.");
    }
}
