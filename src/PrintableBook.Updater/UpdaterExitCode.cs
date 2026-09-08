namespace PrintableBook.Updater;

public enum UpdaterExitCode
{
    Success = 0,
    InvalidArguments = 2,
    PreflightFailed = 3,
    WaitTimeout = 4,
    BackupFailed = 5,
    InstallFailedRolledBack = 6,
    RollbackFailed = 7,
    RestartFailed = 8,
    UnexpectedFailure = 9
}
