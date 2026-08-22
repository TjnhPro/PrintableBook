namespace PrintableBook.Core.Abstractions;

public sealed record DirectoryReference
{
    public DirectoryReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A directory reference is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}
