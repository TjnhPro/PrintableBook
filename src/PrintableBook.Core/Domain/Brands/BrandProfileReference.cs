namespace PrintableBook.Core.Domain.Brands;

/// <summary>
/// Identifies a profile source without making Core aware of its file system representation.
/// </summary>
public sealed record BrandProfileReference
{
    public BrandProfileReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A brand profile reference is required.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}
