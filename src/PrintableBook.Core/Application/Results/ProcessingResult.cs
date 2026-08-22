namespace PrintableBook.Core.Application.Results;

public sealed record ProcessingResult(ProcessingStatus Status, IReadOnlyList<ProcessingIssue> Issues)
{
    public static ProcessingResult Succeeded() => new(ProcessingStatus.Success, []);

    public static ProcessingResult WithWarning(ProcessingIssue issue) => new(ProcessingStatus.Warning, [issue]);

    public static ProcessingResult Failed(ProcessingIssue issue) => new(ProcessingStatus.Failure, [issue]);

    public static ProcessingResult Cancelled() => new(ProcessingStatus.Cancelled, []);
}
