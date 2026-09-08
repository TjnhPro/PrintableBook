namespace PrintableBook.Updater;

public interface IUpdaterPayloadInstaller
{
    void Install(string payloadDirectory, string appRoot);
}
