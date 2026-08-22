namespace PrintableBook.Core.Domain.Processing;

public enum BookProcessingStatus
{
    NotStarted,
    Running,
    Failed,
    Cancelled,
    Completed
}
