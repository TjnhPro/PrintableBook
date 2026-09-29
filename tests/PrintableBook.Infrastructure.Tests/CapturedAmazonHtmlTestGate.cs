namespace PrintableBook.Infrastructure.Tests;

internal static class CapturedAmazonHtmlTestGate
{
    public const string EnvironmentVariable = "PRINTABLEBOOK_RUN_CAPTURED_AMAZON_HTML_TESTS";
    public const string DisabledMessage =
        "Captured Amazon HTML tests are opt-in. Set PRINTABLEBOOK_RUN_CAPTURED_AMAZON_HTML_TESTS=true and run TestScope=CapturedAmazonHtml.";

    public static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
