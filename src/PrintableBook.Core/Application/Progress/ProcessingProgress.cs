namespace PrintableBook.Core.Application.Progress;

public sealed record ProcessingProgress
{
    public ProcessingProgress(string stage, int? completed = null, int? total = null)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("A processing stage is required.", nameof(stage));
        }

        if (completed is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }

        if (total is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        if (completed is not null && total is not null && completed > total)
        {
            throw new ArgumentOutOfRangeException(nameof(completed));
        }

        Stage = stage;
        Completed = completed;
        Total = total;
    }

    public string Stage { get; }

    public int? Completed { get; }

    public int? Total { get; }
}
