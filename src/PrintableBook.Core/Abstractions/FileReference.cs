namespace PrintableBook.Core.Abstractions;

public sealed record FileReference
{
    public FileReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A file reference is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}
