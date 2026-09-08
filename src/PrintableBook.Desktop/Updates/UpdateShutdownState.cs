using System.Threading;

namespace PrintableBook.Desktop.Updates;

public sealed class UpdateShutdownState
{
    private int requested;

    public bool IsRequested => Volatile.Read(ref requested) != 0;

    public void MarkRequested() => Interlocked.Exchange(ref requested, 1);
}
