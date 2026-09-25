using System.Linq;

using TestFramework.Core.Conventions;

using Xunit.Abstractions;

namespace TestFramework.Mock.Tests;

/// <summary>
/// The family's rules, checked against this package rather than trusted to have been followed —
/// from the spike on, because Simple showed what shipping without such a suite quietly costs.
/// </summary>
public class ConventionTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryStepInThisPackageClonesItself()
    {
        // A step that inherits a concrete base class's Clone() runs as that base class and
        // silently loses whatever it added.
        ConventionReport report = StepConventions.AssertEveryStepClonesItself(typeof(MockArg).Assembly);

        output.WriteLine(report.ToString());
        Assert.True(report.Checked > 0, "the check found no steps at all, so it proved nothing");
    }

    [Fact]
    public void FreezingCascadesThroughThisPackagesParts()
    {
        ConventionReport report = StepConventions.AssertFreezingCascades(typeof(MockArg).Assembly);

        output.WriteLine(report.ToString());
        foreach (string skipped in report.Skipped)
        {
            output.WriteLine($"  skipped {skipped}");
        }
    }

    [Fact]
    public void ThisPackageSerialisesWithOneJsonLibrary()
    {
        // The family picked Newtonsoft.Json. Checked against the compiled assembly, because a
        // stray using is invisible in a diff.
        Assert.DoesNotContain(
            "System.Text.Json",
            typeof(MockArg).Assembly.GetReferencedAssemblies().Select(static reference => reference.Name));
    }

    [Fact]
    public void ThisPackageKeepsItsInternalsToItself()
    {
        // Every package is a stranger to every other; the one legal grant is this package's own
        // suite, and the check derives that name rather than pattern-matching it.
        ConventionReport report = StepConventions.AssertNoPackageSeesAnothersInternals(typeof(MockArg).Assembly);

        output.WriteLine(report.ToString());
    }
}
