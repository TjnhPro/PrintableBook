namespace PrintableBook.Desktop.Updates;

public interface IUpdateApplicationLifetime
{
    void RequestGracefulShutdown();
    void RequestForceExit();
}
