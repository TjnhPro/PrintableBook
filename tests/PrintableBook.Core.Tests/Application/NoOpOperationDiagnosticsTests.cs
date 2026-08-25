using PrintableBook.Core.Application.Diagnostics;

namespace PrintableBook.Core.Tests.Application;

public sealed class NoOpOperationDiagnosticsTests
{
    [Fact]
    public void Begin_returns_a_scope_that_is_safe_to_dispose_repeatedly()
    {
        var scope = new NoOpOperationDiagnostics().Begin("snapshot.refresh", "Book One");

        scope.Dispose();
        scope.Dispose();
    }
}
