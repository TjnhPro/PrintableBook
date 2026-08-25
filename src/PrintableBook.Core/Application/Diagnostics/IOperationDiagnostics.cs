namespace PrintableBook.Core.Application.Diagnostics;

public interface IOperationDiagnostics
{
    IDisposable Begin(string operation, string? subject = null);

    void Record(string operation, string? subject = null, string? detail = null);
}

public sealed class NoOpOperationDiagnostics : IOperationDiagnostics
{
    private sealed class Scope : IDisposable
    {
        public static readonly Scope Instance = new();
        public void Dispose() { }
    }

    public IDisposable Begin(string operation, string? subject = null) => Scope.Instance;

    public void Record(string operation, string? subject = null, string? detail = null) { }
}
