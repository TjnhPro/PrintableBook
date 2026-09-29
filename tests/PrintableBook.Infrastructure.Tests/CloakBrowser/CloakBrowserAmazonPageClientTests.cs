using PrintableBook.Core.Application.AmazonCrawl;
using PrintableBook.Core.Application.Diagnostics;
using PrintableBook.Infrastructure.CloakBrowser;

namespace PrintableBook.Infrastructure.Tests.CloakBrowser;

public sealed class CloakBrowserAmazonPageClientTests
{
    [Fact]
    public void Fetch_payload_json_deserializes_with_the_complete_html()
    {
        var client = Client(new RecordingDiagnostics());

        var payload = client.DeserializeFetchPayload("""
            {"tooLarge":false,"status":200,"ok":true,"redirected":false,"finalUrl":"https://www.amazon.com/s?k=coloring","contentType":"text/html; charset=UTF-8","html":"<html>complete response</html>"}
            """);

        Assert.Equal(200, payload.Status);
        Assert.Equal("<html>complete response</html>", payload.Html);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{not-json}")]
    [InlineData("{}")]
    public void Invalid_fetch_payload_reports_the_parse_reason(string json)
    {
        var diagnostics = new RecordingDiagnostics();
        var client = Client(diagnostics);

        var exception = Assert.Throws<AmazonSearchPageException>(() => client.DeserializeFetchPayload(json));

        Assert.Equal("amazon_fetch_payload_invalid", exception.Code);
        Assert.Contains("could not be parsed", exception.Message, StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(diagnostics.Entries);
        Assert.Equal("amazon.fetch.payload.invalid", entry.Operation);
        Assert.False(string.IsNullOrWhiteSpace(entry.Detail));
    }

    private static CloakBrowserAmazonPageClient Client(IOperationDiagnostics diagnostics) => new(
        new CloakBrowserStorageLayout(Path.Combine(Path.GetTempPath(), "printable-book-fetch-payload-tests")),
        diagnostics);

    private sealed class RecordingDiagnostics : IOperationDiagnostics
    {
        public List<(string Operation, string? Detail)> Entries { get; } = [];

        public IDisposable Begin(string operation, string? subject = null) => Scope.Instance;

        public void Record(string operation, string? subject = null, string? detail = null) => Entries.Add((operation, detail));

        private sealed class Scope : IDisposable
        {
            public static Scope Instance { get; } = new();
            public void Dispose() { }
        }
    }
}
