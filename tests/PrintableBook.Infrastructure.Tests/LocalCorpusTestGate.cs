namespace PrintableBook.Infrastructure.Tests;

internal static class LocalCorpusTestGate
{
    public const string EnvironmentVariable = "PRINTABLEBOOK_RUN_LOCAL_CORPUS";
    public const string DisabledMessage =
        "Local corpus test is opt-in. Set PRINTABLEBOOK_RUN_LOCAL_CORPUS=true before running TestScope=LocalCorpus tests.";

    public static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
