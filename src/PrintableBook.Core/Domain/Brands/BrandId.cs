namespace PrintableBook.Core.Domain.Brands;

public sealed record BrandId
{
    public BrandId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A brand identifier is required.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
