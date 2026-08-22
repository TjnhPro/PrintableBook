namespace PrintableBook.Core.Application.Results;

public sealed record ProcessingIssue
{
    public ProcessingIssue(string code, string message, string? stage = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("An issue code is required.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("An issue message is required.", nameof(message));
        }

        Code = code;
        Message = message;
        Stage = stage;
    }

    public string Code { get; }

    public string Message { get; }

    public string? Stage { get; }
}
