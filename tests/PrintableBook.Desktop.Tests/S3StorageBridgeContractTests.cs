using PrintableBook.Core.Application.BackgroundTasks;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Desktop.Bridge;

namespace PrintableBook.Desktop.Tests;

public sealed class S3StorageBridgeContractTests
{
    [Fact]
    public async Task S3_get_returns_only_the_safe_configuration_snapshot()
    {
        var service = new StubStorageService();
        var response = await new WebViewBridgeRouter(s3StorageService: service).HandleAsync(
            """{"version":1,"id":"storage-get","command":"s3.get"}""");

        Assert.True(response.Ok);
        Assert.Equal("s3.snapshot", response.Command);
        var overview = Assert.IsType<S3StorageOverview>(response.Payload);
        Assert.Equal(S3CredentialStatus.Configured, overview.Configuration.CredentialStatus);
        Assert.DoesNotContain("secret", System.Text.Json.JsonSerializer.Serialize(response.Payload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Credentials_replace_accepts_both_values_without_reflecting_them()
    {
        var service = new StubStorageService();
        var response = await new WebViewBridgeRouter(s3StorageService: service).HandleAsync(
            """{"version":1,"id":"storage-save","command":"s3.credentials.replace","payload":{"accessKey":"access-value","secretKey":"secret-value"}}""");

        Assert.True(response.Ok);
        Assert.Equal("s3.credentials.status", response.Command);
        Assert.Equal("access-value", service.SavedInput!.AccessKey);
        Assert.Equal("secret-value", service.SavedInput.SecretKey);
        var serialized = System.Text.Json.JsonSerializer.Serialize(response.Payload);
        Assert.DoesNotContain("access-value", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("book.s3.check", S3StorageAction.Check)]
    [InlineData("book.s3.upload", S3StorageAction.Upload)]
    public async Task Book_s3_commands_start_the_requested_action(string command, S3StorageAction expected)
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
        Assert.Equal("book.s3.session", response.Command);
        Assert.Equal(("Book One", expected), service.Started);
    }

    [Fact]
    public async Task A_second_book_operation_returns_the_stable_active_operation_error()
    {
        var service = new StubStorageService { RejectStart = true };
        var response = await new WebViewBridgeRouter(s3StorageService: service).HandleAsync(
            """{"version":1,"id":"storage-conflict","command":"book.s3.upload","payload":{"bookId":"Book Two"}}""");

        Assert.False(response.Ok);
        Assert.Equal("s3_operation_active", response.Error);
    }

    private sealed class StubStorageService : IS3StorageService
    {
        private static readonly S3StorageConfiguration Configuration = new("us-east-1", "valid-bucket", "coloring");
        private static readonly S3StorageConfigurationStatus Status = new(Configuration, S3CredentialStatus.Configured, "AKIA••••TEST");
        private static readonly S3StorageOverview Overview = new(Status, []);
        public S3StorageCredentialInput? SavedInput { get; private set; }
        public (string BookId, S3StorageAction Action)? Started { get; private set; }
        public bool RejectStart { get; init; }

        public ValueTask<S3StorageOverview> GetOverviewAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Overview);

        public ValueTask<S3StorageConfigurationStatus> ReplaceCredentialsAsync(S3StorageCredentialInput input, CancellationToken cancellationToken = default)
        {
            SavedInput = input;
            return ValueTask.FromResult(Status);
        }

        public ValueTask<S3StorageSessionSnapshot> StartAsync(string bookId, S3StorageAction action, CancellationToken cancellationToken = default)
        {
            if (RejectStart) throw new BackgroundTaskConflictException(BackgroundTaskKind.S3Storage, BackgroundTaskKind.S3Storage);
            Started = (bookId, action);
            return ValueTask.FromResult(new S3StorageSessionSnapshot("task-storage", bookId, true, false, null));
        }

        public ValueTask<S3StorageSessionSnapshot> GetAsync(string bookId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new S3StorageSessionSnapshot(null, bookId, false, false, null));

        public ValueTask<S3StorageSessionSnapshot> CancelAsync(string bookId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new S3StorageSessionSnapshot("task-storage", bookId, false, false, null));
    }
}
