namespace PrintableBook.Core.Application.BackgroundTasks;

public readonly record struct BackgroundTaskId
{
    public BackgroundTaskId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A background task id is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static BackgroundTaskId New() => new($"task-{Guid.NewGuid():N}");

    public override string ToString() => Value;
}
