using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Desktop.Bridge;

namespace PrintableBook.Desktop.Tests;

public sealed class S3StorageBridgeContractTests
{
    [Fact]
    public async Task Storage_get_returns_only_the_safe_configuration_snapshot()
    {
        var service = new StubStorageService();
        var response = await new WebViewBridgeRouter(s3StorageService: service).HandleAsync(
            """{"version":1,"id":"storage-get","command":"storage.get"}""");

        Assert.True(response.Ok);
        Assert.Equal("storage.snapshot", response.Command);
        var overview = Assert.IsType<S3StorageOverview>(response.Payload);
        Assert.True(overview.Configuration.HasCredentials);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(response.Payload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Storage_settings_save_accepts_credentials_without_reflecting_them()
    {
        var service = new StubStorageService();
        var response = await new WebViewBridgeRouter(s3StorageService: service).HandleAsync(
            """{"version":1,"id":"storage-save","command":"storage.settings.save","payload":{"accessKey":"access-value","secretKey":"secret-value","bucket":"valid-bucket","region":"us-east-1","publicBaseUrl":null}}""");

        Assert.True(response.Ok);
        Assert.Equal("access-value", service.SavedInput!.AccessKey);
        Assert.Equal("secret-value", service.SavedInput.SecretKey);
        var serialized = System.Text.Json.JsonSerializer.Serialize(response.Payload);
        Assert.DoesNotContain("access-value", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("storage.book.check", S3StorageAction.Check)]
    [InlineData("storage.book.upload", S3StorageAction.Upload)]
    public async Task Storage_book_commands_start_the_requested_action(string command, S3StorageAction expected)
    {
        var service = new StubStorageService();
        var request = System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            id = "storage-action",
            command,
            payload = new { bookId = "Book One" }
        });
        var response = await new WebViewBridgeRouter(s3StorageService: service).HandleAsync(request);

        Assert.True(response.Ok);
        Assert.Equal("storage.book.session", response.Command);
        Assert.Equal(("Book One", expected), service.Started);
    }

    private sealed class StubStorageService : IS3StorageService
    {
        private static readonly S3StorageOverview Overview = new(new("valid-bucket", "us-east-1", null, true), []);
        public S3StorageSettingsInput? SavedInput { get; private set; }
        public (string BookId, S3StorageAction Action)? Started { get; private set; }

        public ValueTask<S3StorageOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Overview);

        public ValueTask<S3StorageOverview> SaveSettingsAsync(S3StorageSettingsInput input, CancellationToken cancellationToken = default)
        {
            SavedInput = input;
            return ValueTask.FromResult(Overview);
        }

        public ValueTask<S3StorageSessionSnapshot> StartAsync(string bookId, S3StorageAction action, CancellationToken cancellationToken = default)
        {
            Started = (bookId, action);
            return ValueTask.FromResult(new S3StorageSessionSnapshot("task-storage", bookId, true, false, null));
        }

        public ValueTask<S3StorageSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new S3StorageSessionSnapshot(null, bookId, false, false, null));

        public ValueTask<S3StorageSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new S3StorageSessionSnapshot("task-storage", bookId, false, false, null));
    }
}
