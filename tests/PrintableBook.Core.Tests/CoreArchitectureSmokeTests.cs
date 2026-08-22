using PrintableBook.Core.Application.Services;

namespace PrintableBook.Core.Tests;

public sealed class CoreArchitectureSmokeTests
{
    [Fact]
    public void CoreApplicationBoundaryHasNoPlatformSpecificAssemblyReferences()
    {
        var referencedAssemblies = typeof(IPrintableBookApplication).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name);

        Assert.DoesNotContain(referencedAssemblies, name =>
            name is not null &&
            (name.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
             name.Contains("WebView2", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("PresentationCore", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("PresentationFramework", StringComparison.OrdinalIgnoreCase) ||
             name.Equals("WindowsBase", StringComparison.OrdinalIgnoreCase)));
    }
}
