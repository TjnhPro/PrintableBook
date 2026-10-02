namespace PrintableBook.Core.Application.BackgroundTasks;

public enum BackgroundTaskKind
{
    LibraryRefresh = 0,
    ProcessingSession = 1,
    CacheCleanup = 2,
    ProductionAction = 3,
    AmazonAsinCrawl = 4,
    S3Storage = 5
}
