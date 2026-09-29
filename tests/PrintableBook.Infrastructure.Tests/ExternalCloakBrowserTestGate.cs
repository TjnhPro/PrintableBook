namespace PrintableBook.Infrastructure.Tests;

internal static class ExternalCloakBrowserTestGate
{
    public const string EnvironmentVariable = "PRINTABLEBOOK_RUN_CLOAKBROWSER_TESTS";
    public const string DisabledMessage =
        "CloakBrowser integration tests are opt-in. Set PRINTABLEBOOK_RUN_CLOAKBROWSER_TESTS=true and run TestScope=ExternalCloakBrowser.";

    public static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
