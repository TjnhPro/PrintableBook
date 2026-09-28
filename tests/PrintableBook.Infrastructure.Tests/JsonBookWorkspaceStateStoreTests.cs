using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Domain.Processing;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class JsonBookWorkspaceStateStoreTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"PrintableBook.StateStore.{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_uses_current_defaults_when_legacy_json_omits_new_fields()
    {
        var workspace = await CreateWorkspaceAsync();
        await File.WriteAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "state", "book-state.json"), "{\"bookId\":{\"value\":\"book\"},\"status\":\"notStarted\",\"updatedAt\":\"0001-01-01T00:00:00+00:00\",\"mayResume\":false,\"inactiveInteriorSourceKeys\":[\"B.PNG\",\"b.png\",\" \",\"a.png\"]}");
        var state = await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadAsync(workspace);
        Assert.True(state!.HasBackground);
        Assert.Equal(["a.png", "B.PNG"], state.InactiveInteriorSourceKeys);
        Assert.Null(state.PublishedInteriorPreviews);
    }

    [Fact]
    public async Task SaveAsync_round_trips_background_and_inactive_sources()
    {
        var workspace = await CreateWorkspaceAsync();
        var store = new JsonBookWorkspaceStateStore(new PhysicalFileSystem());
        var state = BookProcessingState.NotStarted(new BookId("book")).SetHasBackground(true).SetInteriorActive("Book interior/b.png", false);
        await store.SaveAsync(workspace, state);
        var restored = await store.LoadAsync(workspace);
        Assert.True(restored!.HasBackground);
        Assert.False(restored.IsInteriorActive("BOOK INTERIOR/B.PNG"));
    }

    [Fact]
    public async Task SaveAsync_round_trips_normalized_metadata_and_assignment()
    {
        var workspace = await CreateWorkspaceAsync();
        var store = new JsonBookWorkspaceStateStore(new PhysicalFileSystem());
        var state = BookProcessingState.NotStarted(new BookId("book")) with
        {
            Metadata = BookProductionMetadata.Create(" Title ", " Subtitle ", " ABCD ", " Line one\nLine two ", " Jane Doe "),
            AssignedBrand = " Demo Brand "
        };

        await store.SaveAsync(workspace, state);
        var restored = await store.LoadAsync(workspace);

        Assert.Equal("Title", restored!.Metadata!.Title);
        Assert.Equal("ABCD", restored.Metadata.Subcover);
        Assert.Equal("Line one\nLine two", restored.Metadata.Description);
        Assert.Equal("Jane Doe", restored.Metadata.Author);
        Assert.Equal("Demo Brand", restored.AssignedBrand);
    }

    [Fact]
    public async Task Legacy_metadata_that_fails_new_save_rules_still_loads_and_survives_unrelated_state_save()
    {
        var workspace = await CreateWorkspaceAsync();
        var store = new JsonBookWorkspaceStateStore(new PhysicalFileSystem());
        var state = BookProcessingState.NotStarted(new BookId("book")) with
        {
            Metadata = BookProductionMetadata.Create("Legacy", "and and", "only four terms here", null, "Jane Doe"),
            AssignedBrand = "Brand A"
        };

        await store.SaveAsync(workspace, state);
        var restored = await store.LoadAsync(workspace);
        await store.SaveAsync(workspace, restored! with { AssignedBrand = null });
        var savedAgain = await store.LoadAsync(workspace);

        Assert.Equal("Legacy", savedAgain!.Metadata!.Title);
        Assert.Equal("and and", savedAgain.Metadata.Subtitle);
        Assert.Equal("only four terms here", savedAgain.Metadata.Subcover);
        Assert.Null(savedAgain.AssignedBrand);
    }

    [Fact]
    public async Task SaveAsync_round_trips_keyword_builder_with_explicit_underscore_property_names()
    {
        var workspace = await CreateWorkspaceAsync();
        var store = new JsonBookWorkspaceStateStore(new PhysicalFileSystem());
        var builder = new BookKeywordBuilderState(
            ["coloring books", "coloring book"],
            "books coloring book", null, null, null, null, null, null,
            "coloring books, coloring book",
            "B0123\nB0456",
            "build-1",
            DateTimeOffset.UnixEpoch,
            2,
            ["generic coloring"],
            3);

        await store.SaveAsync(workspace, BookProcessingState.NotStarted(new BookId("book")) with { KeywordBuilder = builder });
        var json = await File.ReadAllTextAsync(StatePath(workspace));
        var restored = await store.LoadAsync(workspace);

        Assert.Contains("\"keyword_1\": \"books coloring book\"", json, StringComparison.Ordinal);
        Assert.Contains("\"keyword_7\": null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"keyword1\"", json, StringComparison.Ordinal);
        Assert.Equal(builder.SourceKeywords, restored!.KeywordBuilder!.SourceKeywords);
        Assert.Equal(builder.Keywords, restored.KeywordBuilder.Keywords);
        Assert.Equal(builder.AdsKeyword, restored.KeywordBuilder.AdsKeyword);
        Assert.Equal(builder.AdsAsin, restored.KeywordBuilder.AdsAsin);
        Assert.Equal(builder.BuildId, restored.KeywordBuilder.BuildId);
        Assert.Equal(builder.GenericKeywords, restored.KeywordBuilder.GenericKeywords);
        Assert.Equal(3, restored.KeywordBuilder.OmittedWordCount);
    }

    [Fact]
    public async Task Legacy_state_without_keyword_builder_loads_with_null_builder()
    {
        var workspace = await CreateWorkspaceAsync();
        await WriteStateAsync(workspace, """{"bookId":{"value":"book"},"status":"notStarted","updatedAt":"0001-01-01T00:00:00+00:00","mayResume":false,"frameModeContractVersion":2}""");

        var restored = await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadAsync(workspace);

        Assert.Null(restored!.KeywordBuilder);
    }

    [Fact]
    public async Task LoadWithMetadataAsync_maps_an_unversioned_missing_override_to_no_frame()
    {
        var workspace = await CreateWorkspaceAsync();
        await WriteStateAsync(workspace, """{"bookId":{"value":"book"},"status":"notStarted","updatedAt":"0001-01-01T00:00:00+00:00","mayResume":false}""");

        var result = await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadWithMetadataAsync(workspace);

        Assert.True(result.LegacyFrameContractDetected);
        Assert.Equal(1, result.SourceFrameModeContractVersion);
        Assert.Equal(FrameMode.Disabled, result.State!.GetInteriorFrameMode("Book interior/page.png"));
    }

    [Theory]
    [InlineData("\"auto\"", FrameMode.Disabled, true)]
    [InlineData("\"enabled\"", FrameMode.Enabled, false)]
    [InlineData("\"disabled\"", FrameMode.Disabled, false)]
    [InlineData("0", FrameMode.Disabled, true)]
    [InlineData("1", FrameMode.Enabled, false)]
    [InlineData("2", FrameMode.Disabled, false)]
    public async Task LoadWithMetadataAsync_maps_the_legacy_frame_contract(string rawMode, FrameMode expected, bool wasAuto)
    {
        var workspace = await CreateWorkspaceAsync();
        await WriteStateAsync(workspace, """{"bookId":{"value":"book"},"status":"notStarted","updatedAt":"0001-01-01T00:00:00+00:00","mayResume":false,"interiorFrameOverrides":{"Book interior/page.png":""" + rawMode + "}}");

        var result = await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadWithMetadataAsync(workspace);

        Assert.Equal(expected, result.State!.GetInteriorFrameMode("book INTERIOR/PAGE.PNG"));
        Assert.Equal(wasAuto, result.ExplicitLegacyAutoSourceKeys?.Count == 1);
        Assert.Single(result.ExplicitLegacyFrameSourceKeys!);
    }

    [Fact]
    public async Task SaveAsync_writes_v2_sparse_enabled_only_state()
    {
        var workspace = await CreateWorkspaceAsync();
        var store = new JsonBookWorkspaceStateStore(new PhysicalFileSystem());
        var state = BookProcessingState.NotStarted(new BookId("book"))
            .SetInteriorFrameMode("Book interior/frame.png", FrameMode.Enabled)
            .SetInteriorFrameMode("Book interior/no-frame.png", FrameMode.Disabled);

        await store.SaveAsync(workspace, state);
        var json = await File.ReadAllTextAsync(StatePath(workspace));

        Assert.Contains("\"frameModeContractVersion\": 2", json, StringComparison.Ordinal);
        Assert.Contains("\"Book interior/frame.png\": \"enabled\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("no-frame.png", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"auto\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("\"auto\"")]
    [InlineData("0")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("1.5")]
    [InlineData("{}")]
    public async Task LoadWithMetadataAsync_rejects_noncanonical_v2_frame_values(string rawMode)
    {
        var workspace = await CreateWorkspaceAsync();
        await WriteStateAsync(workspace, """{"bookId":{"value":"book"},"status":"notStarted","updatedAt":"0001-01-01T00:00:00+00:00","mayResume":false,"frameModeContractVersion":2,"interiorFrameOverrides":{"Book interior/page.png":""" + rawMode + "}}");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadAsync(workspace));

        Assert.Contains("Book 'book'", exception.Message, StringComparison.Ordinal);
        Assert.Contains(StatePath(workspace), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadWithMetadataAsync_rejects_conflicting_case_insensitive_keys()
    {
        var workspace = await CreateWorkspaceAsync();
        await WriteStateAsync(workspace, """{"bookId":{"value":"book"},"status":"notStarted","updatedAt":"0001-01-01T00:00:00+00:00","mayResume":false,"interiorFrameOverrides":{"Book interior/page.png":"enabled","book INTERIOR/PAGE.PNG":"disabled"}}""");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await new JsonBookWorkspaceStateStore(new PhysicalFileSystem()).LoadAsync(workspace));

        Assert.Contains("differs only by case", exception.Message, StringComparison.Ordinal);
    }

    private static string StatePath(BookWorkspace workspace) => Path.Combine(workspace.WorkingDirectory.Value, "state", "book-state.json");
    private static Task WriteStateAsync(BookWorkspace workspace, string json) => File.WriteAllTextAsync(StatePath(workspace), json);

    private async Task<BookWorkspace> CreateWorkspaceAsync() => await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("book"), new DirectoryReference(Path.Combine(root, "book")));
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() { if (Directory.Exists(root)) Directory.Delete(root, true); return Task.CompletedTask; }
}
