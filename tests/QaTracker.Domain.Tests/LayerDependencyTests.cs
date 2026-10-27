namespace QaTracker.Domain.Tests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_DoesNotReferenceOtherLayersOrEfCore()
    {
        var references = typeof(AssemblyReference).Assembly.GetReferencedAssemblies().Select(a => a.Name);

        references.ShouldNotContain(name => name!.StartsWith("QaTracker.", StringComparison.Ordinal));
        references.ShouldNotContain(name => name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }
}
