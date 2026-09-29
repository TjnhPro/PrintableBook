namespace PrintableBook.Infrastructure.Tests;

/// <summary>
/// Test scopes used to keep reproducible repository tests separate from user-supplied local corpora.
/// </summary>
internal static class InfrastructureTestScopes
{
    public const string TraitName = "TestScope";
    public const string LocalCorpus = "LocalCorpus";
    public const string ExternalCloakBrowser = "ExternalCloakBrowser";
    public const string CapturedAmazonHtml = "CapturedAmazonHtml";
}
