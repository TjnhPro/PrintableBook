namespace PrintableBook.Desktop.Updates;

public interface IUpdateRuntimeInfo
{
    int ProcessId { get; }
    string AppRoot { get; }
}
