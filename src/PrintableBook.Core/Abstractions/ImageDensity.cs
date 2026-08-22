namespace PrintableBook.Core.Abstractions;

public readonly record struct ImageDensity
{
    public ImageDensity(double horizontal, double vertical)
    {
        if (horizontal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(horizontal));
        }

        if (vertical <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vertical));
        }

        Horizontal = horizontal;
        Vertical = vertical;
    }

    public double Horizontal { get; }

    public double Vertical { get; }
}
