namespace PrintableBook.Updater;

public interface IUpdaterBackupService
{
    void CreateBackup(string appRoot, string backupDirectory);
    void RestoreBackup(string backupDirectory, string appRoot);
}
