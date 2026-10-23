using PrintableBook.Core.Abstractions;
namespace PrintableBook.Core.Application.Production;

public static class AppPlusSplit
{
    public static int[] Parse(string? value)
    {
        var parts = value?.Split('|');
        if (parts is null || parts.Length != 4) throw new ArgumentException("Save four App Plus Y positions first.");
        var values = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out values[i]) || values[i] < 0 || values[i] > 10000 || (i > 0 && values[i] <= values[i - 1]))
                throw new ArgumentException("App Plus Y positions must be increasing integers from 0 to 10000.");
        return values;
    }
}

public interface IAppPlusProcessingService
{
    ValueTask ProcessAsync(BookWorkspace workspace, DirectoryReference output, string split, CancellationToken cancellationToken = default);
}
