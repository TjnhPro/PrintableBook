namespace PrintableBook.Core.Application.BackgroundTasks;

public sealed class BackgroundTaskFailureException(string code, string message) : Exception(message)
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code)
        ? throw new ArgumentException("A background task failure code is required.", nameof(code))
        : code;
}
