namespace PrintableBook.Infrastructure.Tests;

public sealed class InfrastructureArchitectureTests
{
    [Fact]
    public void InfrastructureDoesNotReferenceTheDesktopHost()
    {
        var referencedAssemblies = typeof(PrintableBook.Infrastructure.DependencyInjection.ServiceCollectionExtensions)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name);

        Assert.DoesNotContain(referencedAssemblies, name =>
            string.Equals(name, "PrintableBook.Desktop", StringComparison.OrdinalIgnoreCase));
    }
}
