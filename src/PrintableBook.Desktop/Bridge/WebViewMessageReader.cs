namespace PrintableBook.Desktop.Bridge;

internal static class WebViewMessageReader
{
    public static string? ReadOrNull(Func<string> readMessage)
    {
        ArgumentNullException.ThrowIfNull(readMessage);

        try
        {
            return readMessage();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
