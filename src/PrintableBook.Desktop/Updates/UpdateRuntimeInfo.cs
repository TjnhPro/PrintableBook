using System.IO;

namespace PrintableBook.Desktop.Updates;

public sealed class UpdateRuntimeInfo : IUpdateRuntimeInfo
{
    public int ProcessId => Environment.ProcessId;
    public string AppRoot => Path.GetFullPath(AppContext.BaseDirectory);
}
