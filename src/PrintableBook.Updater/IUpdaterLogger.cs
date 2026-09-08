namespace PrintableBook.Updater;

public interface IUpdaterLogger
{
    void Info(string message);
    void Error(string message, Exception exception);
}
