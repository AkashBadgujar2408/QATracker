namespace QaTracker.Application.Tests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Application_ReferencesOnlyDomainLayer()
    {
        var references = typeof(AssemblyReference).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(name => name!.StartsWith("QaTracker.", StringComparison.Ordinal));

        references.ShouldAllBe(name => name == "QaTracker.Domain");
    }
}
