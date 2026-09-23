using ImageMagick;
using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Infrastructure.FileSystem;
using PrintableBook.Infrastructure.Imaging;
using PrintableBook.Infrastructure.Processing;
using PrintableBook.Infrastructure.Workspaces;

namespace PrintableBook.Infrastructure.Tests;

public sealed class DiskBackedInteriorPagePipelineTests : IAsyncLifetime
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), $"PrintableBook.PagePipelineTests.{Guid.NewGuid():N}");

    [Fact]
    public async Task ProcessAsync_writes_each_real_stage_to_cache_then_reopens_the_final_png()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "source.png");
        using (var image = new MagickImage(MagickColors.White, 80, 100))
        {
            image.GetPixels().SetPixel(20, 20, [0, 0, 0]);
            image.GetPixels().SetPixel(59, 79, [0, 0, 0]);
            image.Write(source);
        }

        var fileSystem = new PhysicalFileSystem();
        var bookDirectory = new DirectoryReference(Path.Combine(rootPath, "Book"));
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(new BookId("book-one"), bookDirectory);
        var pipeline = CreatePipeline();

        var result = await pipeline.ProcessAsync(new InteriorPagePipelineRequest(
            workspace,
            new FileReference(source),
            "page-01",
            new ArtworkDetectionThreshold(20),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageDensity(300, 300),
            null,
            FrameMode.Disabled));

        Assert.Equal("page-01", result.PageId);
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "classification.json")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "framed.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "working-page.png")));
        var stamp = await File.ReadAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "input-stamp.json"));
        Assert.Contains(ArtworkPreparationAlgorithmVersion.Current, stamp, StringComparison.Ordinal);
        Assert.Contains(ClassificationAlgorithmVersion.Current, stamp, StringComparison.Ordinal);
        Assert.Contains("\"FrameMode\":\"disabled\"", stamp, StringComparison.Ordinal);
        Assert.StartsWith(Path.Combine(workspace.WorkingDirectory.Value, "processed", "interior"), result.FinalPage.Value, StringComparison.OrdinalIgnoreCase);
        var finalInfo = await new MagickImageInspector().GetInfoAsync(result.FinalPage);
        Assert.Equal(new ImageSize(200, 200), finalInfo.Size);
        Assert.Equal(300, finalInfo.Density!.Value.Horizontal, precision: 2);
    }

    [Fact]
    public async Task ProcessAsync_forces_intro_templates_to_crop_art_and_writes_them_under_processed_intro()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "intro.png");
        using (var image = new MagickImage(MagickColors.White, 1024, 1024))
        {
            image.GetPixels().SetPixel(100, 100, [0, 0, 0]);
            image.Write(source);
        }
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("intro-book"), new DirectoryReference(Path.Combine(rootPath, "IntroBook")));

        var result = await CreatePipeline(new ThrowingArtworkClassifier()).ProcessAsync(new InteriorPagePipelineRequest(
            workspace, new FileReference(source), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(200, 200), new ImageSize(200, 200), new ImageSize(200, 200), new ImageDensity(300, 300), null, FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.IntroTemplate));

        Assert.StartsWith(Path.Combine(workspace.ProcessedDirectory.Value, "intro"), result.FinalPage.Value, StringComparison.OrdinalIgnoreCase);
        var classification = await File.ReadAllTextAsync(Path.Combine(workspace.WorkingDirectory.Value, "cache", "intro-0001", "classification.json"));
        Assert.Contains("cropart", classification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forced-intro", classification, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_forces_production_pages_to_crop_art_and_isolates_cache_and_output()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("production-cover.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("production-book"), new DirectoryReference(Path.Combine(rootPath, "ProductionBook")));
        var request = new InteriorPagePipelineRequest(
            workspace,
            new FileReference(source),
            "production-interior-cover",
            new ArtworkDetectionThreshold(20),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageDensity(300, 300),
            null,
            FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.ProductionInterior,
            outputFileName: "interior-cover.png");

        var result = await CreatePipeline(new ThrowingArtworkClassifier()).ProcessAsync(request);

        Assert.StartsWith(Path.Combine(workspace.ProcessedDirectory.Value, "production"), result.FinalPage.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(workspace.ProcessedDirectory.Value, "production", "interior-cover.png"), result.FinalPage.Value);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "production-interior-cover");
        Assert.True(File.Exists(Path.Combine(cache, "input-stamp.json")));
        var classification = await File.ReadAllTextAsync(Path.Combine(cache, "classification.json"));
        Assert.Contains("cropart", classification, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forced-no-frame", classification, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01")));
    }

    [Fact]
    public async Task ProcessAsync_rejects_intro_templates_that_are_not_1024_or_2048_square()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "non-square-intro.png");
        using (var image = new MagickImage(MagickColors.White, 1024, 2048))
        {
            image.Write(source);
        }
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("small-intro"), new DirectoryReference(Path.Combine(rootPath, "SmallIntro")));

        var failure = await Assert.ThrowsAsync<InteriorPageProcessingException>(() => CreatePipeline().ProcessAsync(new InteriorPagePipelineRequest(
            workspace, new FileReference(source), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(200, 200), new ImageSize(200, 200), new ImageSize(200, 200), new ImageDensity(300, 300), null, FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.IntroTemplate)).AsTask());

        Assert.Equal("normalization", failure.Step);
    }

    [Fact]
    public async Task ProcessAsync_accepts_2048_square_intro_templates()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "large-intro.png");
        using (var image = new MagickImage(MagickColors.White, 2048, 2048))
        {
            image.GetPixels().SetPixel(100, 100, [0, 0, 0]);
            image.Write(source);
        }
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("large-intro"), new DirectoryReference(Path.Combine(rootPath, "LargeIntro")));

        var result = await CreatePipeline().ProcessAsync(new InteriorPagePipelineRequest(
            workspace, new FileReference(source), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(200, 200), new ImageSize(200, 200), new ImageSize(200, 200), new ImageDensity(300, 300), null, FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.IntroTemplate));

        Assert.Equal(new ImageSize(200, 200), (await new MagickImageInspector().GetInfoAsync(result.FinalPage)).Size);
    }

    [Fact]
    public async Task ProcessAsync_uses_final_sized_brand_intro_artwork_directly_without_creating_intermediate_artifacts()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "final-intro.png");
        using (var image = new MagickImage(MagickColors.White, 2588, 2625))
        {
            image.GetPixels().SetPixel(100, 100, [0, 0, 0]);
            image.Write(source);
        }
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("final-intro-book"), new DirectoryReference(Path.Combine(rootPath, "FinalIntroBook")));

        var result = await CreatePipeline().ProcessAsync(new InteriorPagePipelineRequest(
            workspace, new FileReference(source), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(2270, 2270), new ImageSize(2550, 2550), new ImageSize(2588, 2625), new ImageDensity(300, 300), null, FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.BrandIntroTemplate));

        Assert.Equal(new FileReference(source), result.FinalPage);
        Assert.Equal(new ImageSize(2588, 2625), (await new MagickImageInspector().GetInfoAsync(result.FinalPage)).Size);
        Assert.False(Directory.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "intro-0001")));
    }

    [Fact]
    public async Task ProcessAsync_defensively_ignores_a_frame_in_a_mutated_intro_request()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "framed-intro.png");
        var frame = await CreateRedFrameAsync("intro-frame.png", new ImageSize(200, 200));
        using (var image = new MagickImage(MagickColors.White, 1024, 1024))
        {
            image.GetPixels().SetPixel(100, 100, [0, 0, 0]);
            image.Write(source);
        }
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(new BookId("framed-intro"), new DirectoryReference(Path.Combine(rootPath, "FramedIntro")));
        var legalIntroRequest = new InteriorPagePipelineRequest(
            workspace, new FileReference(source), "intro-0001", new ArtworkDetectionThreshold(20), new ImageSize(200, 200), new ImageSize(200, 200), new ImageSize(200, 200), new ImageDensity(300, 300), null, FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.IntroTemplate);

        await CreatePipeline().ProcessAsync(legalIntroRequest with { Frame = new FileReference(frame), FrameMode = FrameMode.Enabled });

        using var framed = new MagickImage(Path.Combine(workspace.WorkingDirectory.Value, "cache", "intro-0001", "framed.png"));
        var corner = framed.GetPixels().GetPixel(0, 0);
        Assert.False(corner[0] == 255 && corner[1] == 0 && corner[2] == 0, "IntroTemplate output must not use the supplied red frame.");
    }

    [Fact]
    public async Task ProcessAsync_defensively_ignores_a_frame_in_a_mutated_production_request()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("production-no-frame.png");
        var frame = await CreateRedFrameAsync("production-frame.png", new ImageSize(200, 200));
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("production-no-frame"),
            new DirectoryReference(Path.Combine(rootPath, "ProductionNoFrame")));
        var legalRequest = new InteriorPagePipelineRequest(
            workspace,
            new FileReference(source),
            "production-interior-cover",
            new ArtworkDetectionThreshold(20),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageDensity(300, 300),
            null,
            FrameMode.Disabled,
            processingKind: InteriorPageProcessingKind.ProductionInterior,
            outputFileName: "interior-cover.png");

        await CreatePipeline().ProcessAsync(legalRequest with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        });

        using var framed = new MagickImage(Path.Combine(
            workspace.WorkingDirectory.Value,
            "cache",
            "production-interior-cover",
            "framed.png"));
        var corner = framed.GetPixels().GetPixel(0, 0);
        Assert.False(corner[0] == 255 && corner[1] == 0 && corner[2] == 0, "Production output must force No Frame.");
    }

    [Fact]
    public async Task ProcessAsync_migrates_a_legacy_processed_input_stamp_into_page_cache()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("legacy-stamp-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("legacy-stamp-book"), new DirectoryReference(Path.Combine(rootPath, "LegacyStampBook")));
        var pipeline = CreatePipeline();
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));

        await pipeline.ProcessAsync(request);

        var pageCache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var newStamp = Path.Combine(pageCache, "input-stamp.json");
        var legacyStamp = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-01.input-stamp.json");
        File.Move(newStamp, legacyStamp, overwrite: true);

        var prepared = Path.Combine(pageCache, "prepared.png");
        var retainedTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, retainedTime);

        await pipeline.ProcessAsync(request);

        Assert.True(File.Exists(newStamp));
        Assert.False(File.Exists(legacyStamp));
        Assert.Equal(retainedTime, File.GetLastWriteTimeUtc(prepared));
    }

    [Fact]
    public async Task ProcessAsync_reuses_valid_upstream_cache_when_a_downstream_stage_is_missing()
    {
        Directory.CreateDirectory(rootPath);
        var source = Path.Combine(rootPath, "resume-source.png");
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.GetPixels().SetPixel(20, 20, [0, 0, 0]);
            image.GetPixels().SetPixel(79, 79, [0, 0, 0]);
            image.Write(source);
        }

        var fileSystem = new PhysicalFileSystem();
        var workspace = await new PhysicalBookWorkspaceFactory(fileSystem).CreateAsync(
            new BookId("resume-book"), new DirectoryReference(Path.Combine(rootPath, "ResumeBook")));
        var pipeline = CreatePipeline();
        var request = new InteriorPagePipelineRequest(
            workspace,
            new FileReference(source),
            "page-01",
            new ArtworkDetectionThreshold(20),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageSize(200, 200),
            new ImageDensity(300, 300),
            null,
            FrameMode.Disabled);

        await pipeline.ProcessAsync(request);
        var prepared = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png");
        var retainedTimestamp = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, retainedTimestamp);
        File.Delete(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "working-page.png"));

        var retried = await pipeline.ProcessAsync(request);

        Assert.Equal(retainedTimestamp, File.GetLastWriteTimeUtc(prepared));
        Assert.True(File.Exists(retried.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_regenerates_the_persistent_page_when_processing_configuration_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("configuration-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("configuration-book"), new DirectoryReference(Path.Combine(rootPath, "ConfigurationBook")));
        var pipeline = CreatePipeline();
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));

        await pipeline.ProcessAsync(request);
        var regenerated = await pipeline.ProcessAsync(request with { FinalPageSize = new ImageSize(220, 220) });

        var info = await new MagickImageInspector().GetInfoAsync(regenerated.FinalPage);
        Assert.Equal(new ImageSize(220, 220), info.Size);
    }

    [Fact]
    public async Task ProcessAsync_retains_cache_and_prior_processed_pages_when_another_page_fails()
    {
        Directory.CreateDirectory(rootPath);
        var goodSource = await CreateArtworkSourceAsync("good-source.png");
        var failedSource = Path.Combine(rootPath, "blank-source.png");
        using (var blank = new MagickImage(MagickColors.White, 100, 100))
        {
            blank.Write(failedSource);
        }

        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("failure-book"), new DirectoryReference(Path.Combine(rootPath, "FailureBook")));
        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(CreateRequest(workspace, goodSource, "page-01", new ImageSize(200, 200)));

        var failure = await Assert.ThrowsAsync<InteriorPageProcessingException>(() => pipeline.ProcessAsync(
            CreateRequest(workspace, failedSource, "page-02", new ImageSize(200, 200))).AsTask());

        Assert.Equal("preparation", failure.Step);
        Assert.True(File.Exists(completed.FinalPage.Value));
        Assert.True(Directory.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-02")));
    }

    [Fact]
    public async Task ProcessAsync_propagates_cancellation_without_removing_the_page_workspace()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("cancelled-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("cancelled-book"), new DirectoryReference(Path.Combine(rootPath, "CancelledBook")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePipeline().ProcessAsync(
            CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)), cancellation.Token).AsTask());

        Assert.True(Directory.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01")));
    }

    [Fact]
    public async Task ProcessAsync_reclassifies_when_cached_classification_metadata_is_corrupt()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("metadata-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("metadata-book"), new DirectoryReference(Path.Combine(rootPath, "MetadataBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();

        var completed = await pipeline.ProcessAsync(request);
        var classification = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "classification.json");
        await File.WriteAllTextAsync(classification, "{ not valid json");
        File.Delete(completed.FinalPage.Value);

        await pipeline.ProcessAsync(request);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(classification));
        Assert.Equal("artwork-classification-cache-v2", document.RootElement.GetProperty("SchemaVersion").GetString());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData(2)]
    public async Task ProcessAsync_reclassifies_when_cached_classification_type_is_not_canonical(object staleType)
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("stale-type-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("stale-type-book"), new DirectoryReference(Path.Combine(rootPath, "StaleTypeBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();

        var completed = await pipeline.ProcessAsync(request);
        var classification = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "classification.json");
        var original = await File.ReadAllTextAsync(classification);
        var replacement = staleType is string text ? $"\"{text}\"" : staleType.ToString();
        await File.WriteAllTextAsync(classification, original.Replace("\"EffectiveType\":\"cropart\"", $"\"EffectiveType\":{replacement}", StringComparison.Ordinal));
        File.Delete(completed.FinalPage.Value);

        await pipeline.ProcessAsync(request);

        using var regenerated = JsonDocument.Parse(await File.ReadAllTextAsync(classification));
        Assert.Equal("cropart", regenerated.RootElement.GetProperty("EffectiveType").GetString());
    }

    [Fact]
    public async Task ProcessAsync_regenerates_a_corrupt_prepared_artwork_and_downstream_pages()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("prepared-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("prepared-book"), new DirectoryReference(Path.Combine(rootPath, "PreparedBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();

        var completed = await pipeline.ProcessAsync(request);
        var prepared = new FileReference(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png"));
        await File.WriteAllTextAsync(prepared.Value, "not a PNG");
        File.Delete(completed.FinalPage.Value);

        await pipeline.ProcessAsync(request);

        Assert.Equal(new ImageSize(200, 200), (await new MagickImageInspector().GetInfoAsync(prepared)).Size);
        Assert.True(File.Exists(completed.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_applies_an_available_enabled_frame_to_cropart()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("cropart-source.png");
        var frame = Path.Combine(rootPath, "red-frame.png");
        using (var image = new MagickImage(MagickColors.Red, 200, 200)) image.Write(frame);
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("cropart-book"), new DirectoryReference(Path.Combine(rootPath, "CropArtBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        };

        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(request);
        var framedPath = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "framed.png");
        using (var stale = new MagickImage(MagickColors.Red, 200, 200)) stale.Write(framedPath);
        File.Delete(completed.FinalPage.Value);

        await pipeline.ProcessAsync(request);

        var classification = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "classification.json")));
        Assert.Equal("cropart", classification.RootElement.GetProperty("EffectiveType").GetString());
        using var framed = new MagickImage(framedPath);
        Assert.Equal((byte)255, framed.GetPixels().GetPixel(0, 0)[0]);
    }

    [Fact]
    public async Task ProcessAsync_no_frame_forces_cropart_without_calling_the_classifier()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("forced-no-frame.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("forced-no-frame"), new DirectoryReference(Path.Combine(rootPath, "ForcedNoFrameBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            FrameMode = FrameMode.Disabled
        };

        var result = await CreatePipeline(new ThrowingArtworkClassifier()).ProcessAsync(request);

        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        using var classification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "classification.json")));
        Assert.Equal("cropart", classification.RootElement.GetProperty("EffectiveType").GetString());
        Assert.Equal("forced-no-frame", classification.RootElement.GetProperty("Origin").GetString());
        Assert.Equal("not-run", classification.RootElement.GetProperty("DetectionStatus").GetString());
        Assert.Equal(JsonValueKind.Null, classification.RootElement.GetProperty("BorderLine").ValueKind);
        Assert.Equal(JsonValueKind.Null, classification.RootElement.GetProperty("BorderPixel").ValueKind);
        Assert.True(File.Exists(result.FinalPage.Value));
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(cache, "prepared.png")),
            await File.ReadAllBytesAsync(Path.Combine(cache, "framed.png")));
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_v3_auto_metadata_as_forced_no_frame()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("legacy-auto.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("legacy-auto"), new DirectoryReference(Path.Combine(rootPath, "LegacyAutoBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        await SeedLegacyV3CropArtMetadataAsync(cache, "auto");
        var prepared = Path.Combine(cache, "prepared.png");
        var retainedTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, retainedTime);

        await pipeline.ProcessAsync(request);

        Assert.NotEqual(retainedTime, File.GetLastWriteTimeUtc(prepared));
        using var classification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "classification.json")));
        Assert.Equal("forced-no-frame", classification.RootElement.GetProperty("Origin").GetString());
        Assert.Equal("interior-page-cache-v5", JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "input-stamp.json"))).RootElement.GetProperty("SchemaVersion").GetString());
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_legacy_v3_disabled_metadata_as_forced_no_frame()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("legacy-disabled.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("legacy-disabled"), new DirectoryReference(Path.Combine(rootPath, "LegacyDisabledBook")));
        var auto = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(auto);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        await SeedLegacyV3CropArtMetadataAsync(cache, "disabled");
        var prepared = Path.Combine(cache, "prepared.png");
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, staleTime);

        await pipeline.ProcessAsync(auto with { FrameMode = FrameMode.Disabled });

        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(prepared));
        using var classification = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(cache, "classification.json")));
        Assert.Equal("forced-no-frame", classification.RootElement.GetProperty("Origin").GetString());
        Assert.Equal("not-run", classification.RootElement.GetProperty("DetectionStatus").GetString());
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_when_source_threshold_or_algorithm_stamp_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("stamp-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("stamp-book"), new DirectoryReference(Path.Combine(rootPath, "StampBook")));
        var pipeline = CreatePipeline();
        var frame = await CreateRedFrameAsync("stamp-frame.png", new ImageSize(200, 200));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        };
        await pipeline.ProcessAsync(request);

        var prepared = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png");
        var stamp = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "input-stamp.json");

        await AssertRebuildsPreparedAsync(pipeline, request, prepared, async () =>
        {
            using var image = new MagickImage(source);
            image.GetPixels().SetPixel(30, 30, [0, 0, 0]);
            image.Write(source);
            await Task.CompletedTask;
        });
        await AssertRebuildsPreparedAsync(pipeline, request with { ArtworkDetectionThreshold = new ArtworkDetectionThreshold(21) }, prepared, () => Task.CompletedTask);
        await AssertRebuildsPreparedAsync(pipeline, request, prepared, () => ReplaceStampValueAsync(stamp, ClassificationAlgorithmVersion.Current, "artwork-classification-stale"));
        await AssertRebuildsPreparedAsync(pipeline, request, prepared, () => ReplaceStampValueAsync(stamp, ArtworkPreparationAlgorithmVersion.Current, "artwork-preparation-stale"));
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_for_geometry_and_frame_inputs_and_repairs_wrong_size_working_page()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("geometry-source.png");
        var frame = Path.Combine(rootPath, "frame.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("geometry-book"), new DirectoryReference(Path.Combine(rootPath, "GeometryBook")));
        var pipeline = CreatePipeline();
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        await pipeline.ProcessAsync(request);
        var prepared = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png");

        var smallerPrepared = request with { PreparedArtworkSize = new ImageSize(180, 180) };
        await AssertRebuildsPreparedAsync(pipeline, smallerPrepared, prepared, () => Task.CompletedTask);
        Assert.Equal(new ImageSize(180, 180), (await new MagickImageInspector().GetInfoAsync(new FileReference(prepared))).Size);

        var smallerWorking = smallerPrepared with { WorkingPageSize = new ImageSize(190, 190) };
        var working = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "working-page.png");
        await pipeline.ProcessAsync(smallerWorking);
        Assert.Equal(new ImageSize(190, 190), (await new MagickImageInspector().GetInfoAsync(new FileReference(working))).Size);

        var framedRequest = request with { Frame = new FileReference(frame), FrameMode = FrameMode.Enabled };
        await AssertRebuildsPreparedAsync(pipeline, framedRequest, prepared, async () =>
        {
            using var image = new MagickImage(MagickColors.Red, 200, 200);
            image.Write(frame);
            await Task.CompletedTask;
        });
        var retainedPreparedTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, retainedPreparedTime);
        await pipeline.ProcessAsync(framedRequest with { FrameMode = FrameMode.Disabled });
        Assert.NotEqual(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));

        var completed = await pipeline.ProcessAsync(request);
        using (var wrongSize = new MagickImage(MagickColors.White, 10, 10)) wrongSize.Write(working);
        File.Delete(completed.FinalPage.Value);
        await pipeline.ProcessAsync(request);
        Assert.Equal(new ImageSize(200, 200), (await new MagickImageInspector().GetInfoAsync(new FileReference(working))).Size);
    }

    [Fact]
    public async Task ProcessAsync_enabled_frame_fails_before_cache_hit_when_frame_disappears()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("required-frame-source.png");
        var frame = await CreateRedFrameAsync("required-frame.png", new ImageSize(200, 200));
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("required-frame"), new DirectoryReference(Path.Combine(rootPath, "RequiredFrameBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        };
        var pipeline = CreatePipeline();
        await pipeline.ProcessAsync(request);
        File.Delete(frame);

        var exception = await Assert.ThrowsAsync<InteriorPageProcessingException>(() => pipeline.ProcessAsync(request).AsTask());

        Assert.Equal("INTERIOR_FRAME_REQUIRED", exception.FailureCode);
        Assert.Equal("frame-validation", exception.Step);
    }

    [Fact]
    public async Task ProcessAsync_enabled_frame_reports_wrong_geometry_as_invalid()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("wrong-frame-source.png");
        var frame = await CreateRedFrameAsync("wrong-frame.png", new ImageSize(100, 100));
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("wrong-frame"), new DirectoryReference(Path.Combine(rootPath, "WrongFrameBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        };

        var exception = await Assert.ThrowsAsync<InteriorPageProcessingException>(() => CreatePipeline().ProcessAsync(request).AsTask());

        Assert.Equal("INTERIOR_FRAME_INVALID", exception.FailureCode);
        Assert.Contains("requires a 200x200 Brand frame", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_enabled_frame_reports_unreadable_input_as_invalid()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("unreadable-frame-source.png");
        var frame = Path.Combine(rootPath, "unreadable-frame.png");
        await File.WriteAllTextAsync(frame, "not an image");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("unreadable-frame"), new DirectoryReference(Path.Combine(rootPath, "UnreadableFrameBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        };

        var exception = await Assert.ThrowsAsync<InteriorPageProcessingException>(() => CreatePipeline().ProcessAsync(request).AsTask());

        Assert.Equal("INTERIOR_FRAME_INVALID", exception.FailureCode);
        Assert.Contains("unreadable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_enabled_frame_uses_content_digest_when_metadata_is_unchanged()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("digest-frame-source.png");
        var frame = await CreateRedFrameAsync("digest-frame.png", new ImageSize(200, 200));
        var originalInfo = new FileInfo(frame);
        var originalLength = originalInfo.Length;
        var originalWriteTime = originalInfo.LastWriteTimeUtc;
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("digest-frame"), new DirectoryReference(Path.Combine(rootPath, "DigestFrameBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = FrameMode.Enabled
        };
        var pipeline = CreatePipeline();
        var result = await pipeline.ProcessAsync(request);
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(result.FinalPage.Value, staleTime);

        using (var replacement = new MagickImage(MagickColors.Blue, 200, 200)) replacement.Write(frame);
        Assert.Equal(originalLength, new FileInfo(frame).Length);
        File.SetLastWriteTimeUtc(frame, originalWriteTime);

        await pipeline.ProcessAsync(request);

        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(result.FinalPage.Value));
        using var stamp = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "input-stamp.json")));
        Assert.Equal(64, stamp.RootElement.GetProperty("FrameContentSha256").GetString()!.Length);
    }

    [Theory]
    [InlineData(FrameMode.Enabled, FrameMode.Disabled, false)]
    [InlineData(FrameMode.Disabled, FrameMode.Enabled, false)]
    public async Task ProcessAsync_invalidates_the_expected_stage_for_each_frame_mode_transition(
        FrameMode initialMode,
        FrameMode changedMode,
        bool retainsDetectedPreparation)
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync($"frame-mode-{initialMode}-{changedMode}.png");
        var frame = await CreateRedFrameAsync("frame.png", new ImageSize(200, 200));
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId($"frame-mode-{initialMode}-{changedMode}"), new DirectoryReference(Path.Combine(rootPath, "FrameModeBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            Frame = new FileReference(frame),
            FrameMode = initialMode
        };
        var pipeline = CreatePipeline();

        var completed = await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var classification = Path.Combine(cache, "classification.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var classificationBefore = await File.ReadAllTextAsync(classification);
        var retainedPreparedTime = DateTime.UtcNow.AddHours(-1);
        var staleDownstreamTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(prepared, retainedPreparedTime);
        File.SetLastWriteTimeUtc(framed, staleDownstreamTime);
        File.SetLastWriteTimeUtc(working, staleDownstreamTime);
        File.SetLastWriteTimeUtc(completed.FinalPage.Value, staleDownstreamTime);

        await pipeline.ProcessAsync(request with { FrameMode = changedMode });

        if (retainsDetectedPreparation)
        {
            Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
            Assert.Equal(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));
        }
        else
        {
            Assert.NotEqual(classificationBefore, await File.ReadAllTextAsync(classification));
            Assert.NotEqual(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));
        }
        Assert.NotEqual(staleDownstreamTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleDownstreamTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleDownstreamTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_no_frame_reuses_classification_but_rebuilds_preparation_when_trim_threshold_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("classification-invalidation.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("classification-invalidation"), new DirectoryReference(Path.Combine(rootPath, "ClassificationBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var classification = Path.Combine(cache, "classification.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(classification, staleTime);
        File.SetLastWriteTimeUtc(prepared, staleTime);
        File.SetLastWriteTimeUtc(framed, staleTime);
        File.SetLastWriteTimeUtc(working, staleTime);
        File.SetLastWriteTimeUtc(completed.FinalPage.Value, staleTime);

        await pipeline.ProcessAsync(request with { ArtworkDetectionThreshold = new ArtworkDetectionThreshold(21) });

        Assert.Equal(staleTime, File.GetLastWriteTimeUtc(classification));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(prepared));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_no_frame_uses_policy_specific_cache_dependencies()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("forced-dependencies.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("forced-dependencies"), new DirectoryReference(Path.Combine(rootPath, "ForcedDependenciesBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200)) with
        {
            FrameMode = FrameMode.Disabled
        };
        var pipeline = CreatePipeline(new ThrowingArtworkClassifier());
        await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var classification = Path.Combine(cache, "classification.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var classificationBefore = await File.ReadAllTextAsync(classification);
        var retainedTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(prepared, retainedTime);

        await pipeline.ProcessAsync(request with
        {
            BorderLineDetection = BorderLineDetectionSettings.Default with { Pass2SearchDepth = 500 }
        });

        Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
        Assert.Equal(retainedTime, File.GetLastWriteTimeUtc(prepared));

        await pipeline.ProcessAsync(request with { ArtworkDetectionThreshold = new ArtworkDetectionThreshold(21) });

        Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
        Assert.NotEqual(retainedTime, File.GetLastWriteTimeUtc(prepared));
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_from_preparation_when_prepared_size_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("preparation-invalidation.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("preparation-invalidation"), new DirectoryReference(Path.Combine(rootPath, "PreparationBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var classification = Path.Combine(cache, "classification.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var classificationBefore = await File.ReadAllTextAsync(classification);
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, staleTime);
        File.SetLastWriteTimeUtc(framed, staleTime);
        File.SetLastWriteTimeUtc(working, staleTime);
        File.SetLastWriteTimeUtc(completed.FinalPage.Value, staleTime);

        await pipeline.ProcessAsync(request with { PreparedArtworkSize = new ImageSize(180, 180) });

        Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(prepared));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_from_working_when_working_size_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("working-invalidation.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("working-invalidation"), new DirectoryReference(Path.Combine(rootPath, "WorkingBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var classification = Path.Combine(cache, "classification.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var classificationBefore = await File.ReadAllTextAsync(classification);
        var retainedPreparedTime = DateTime.UtcNow.AddHours(-1);
        var retainedFramedTime = DateTime.UtcNow.AddHours(-2);
        var staleTime = DateTime.UtcNow.AddHours(-3);
        File.SetLastWriteTimeUtc(prepared, retainedPreparedTime);
        File.SetLastWriteTimeUtc(framed, retainedFramedTime);
        File.SetLastWriteTimeUtc(working, staleTime);
        File.SetLastWriteTimeUtc(completed.FinalPage.Value, staleTime);

        await pipeline.ProcessAsync(request with { WorkingPageSize = new ImageSize(220, 220), FinalPageSize = new ImageSize(220, 220) });

        Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
        Assert.Equal(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));
        Assert.Equal(retainedFramedTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_only_final_when_final_size_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("final-invalidation.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("final-invalidation"), new DirectoryReference(Path.Combine(rootPath, "FinalBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(request);
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var classification = Path.Combine(cache, "classification.json");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var classificationBefore = await File.ReadAllTextAsync(classification);
        var retainedPreparedTime = DateTime.UtcNow.AddHours(-1);
        var retainedFramedTime = DateTime.UtcNow.AddHours(-2);
        var retainedWorkingTime = DateTime.UtcNow.AddHours(-3);
        var staleFinalTime = DateTime.UtcNow.AddHours(-4);
        File.SetLastWriteTimeUtc(prepared, retainedPreparedTime);
        File.SetLastWriteTimeUtc(framed, retainedFramedTime);
        File.SetLastWriteTimeUtc(working, retainedWorkingTime);
        File.SetLastWriteTimeUtc(completed.FinalPage.Value, staleFinalTime);

        await pipeline.ProcessAsync(request with { FinalPageSize = new ImageSize(220, 220) });

        Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
        Assert.Equal(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));
        Assert.Equal(retainedFramedTime, File.GetLastWriteTimeUtc(framed));
        Assert.Equal(retainedWorkingTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleFinalTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
    }

    [Theory]
    [InlineData("{ malformed")]
    [InlineData("incompatible-schema")]
    [InlineData("numeric-frame-mode")]
    [InlineData(null)]
    public async Task ProcessAsync_rebuilds_from_classification_when_stamp_is_unusable(string? invalidStamp)
    {
        Directory.CreateDirectory(rootPath);
        var stampKind = invalidStamp is null ? "missing" : "corrupt";
        var source = await CreateArtworkSourceAsync($"stamp-{stampKind}.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId($"stamp-{stampKind}"), new DirectoryReference(Path.Combine(rootPath, "StampBook")));
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        var pipeline = CreatePipeline();
        var completed = await pipeline.ProcessAsync(request);
        var classification = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "classification.json");
        var cache = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01");
        var prepared = Path.Combine(cache, "prepared.png");
        var framed = Path.Combine(cache, "framed.png");
        var working = Path.Combine(cache, "working-page.png");
        var stamp = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "input-stamp.json");
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(classification, staleTime);
        File.SetLastWriteTimeUtc(prepared, staleTime);
        File.SetLastWriteTimeUtc(framed, staleTime);
        File.SetLastWriteTimeUtc(working, staleTime);
        File.SetLastWriteTimeUtc(completed.FinalPage.Value, staleTime);
        if (invalidStamp is null)
        {
            File.Delete(stamp);
        }
        else if (string.Equals(invalidStamp, "incompatible-schema", StringComparison.Ordinal))
        {
            var contents = await File.ReadAllTextAsync(stamp);
            await File.WriteAllTextAsync(stamp, contents.Replace("interior-page-cache-v5", "incompatible-schema", StringComparison.Ordinal));
        }
        else if (string.Equals(invalidStamp, "numeric-frame-mode", StringComparison.Ordinal))
        {
            var contents = await File.ReadAllTextAsync(stamp);
            await File.WriteAllTextAsync(stamp, contents.Replace("\"FrameMode\":\"disabled\"", "\"FrameMode\":0", StringComparison.Ordinal));
        }
        else
        {
            await File.WriteAllTextAsync(stamp, invalidStamp);
        }

        var retried = await pipeline.ProcessAsync(request);

        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(classification));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(prepared));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(retried.FinalPage.Value));
        Assert.True(File.Exists(retried.FinalPage.Value));
        Assert.Equal(new ImageSize(200, 200), (await new MagickImageInspector().GetInfoAsync(retried.FinalPage)).Size);
    }

    private static async Task AssertRebuildsPreparedAsync(
        DiskBackedInteriorPagePipeline pipeline,
        InteriorPagePipelineRequest request,
        string prepared,
        Func<Task> invalidate)
    {
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(prepared, staleTime);
        await invalidate();
        await pipeline.ProcessAsync(request);
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(prepared));
    }

    private static async Task ReplaceStampValueAsync(string stamp, string expected, string replacement)
    {
        var contents = await File.ReadAllTextAsync(stamp);
        Assert.Contains(expected, contents, StringComparison.Ordinal);
        await File.WriteAllTextAsync(stamp, contents.Replace(expected, replacement, StringComparison.Ordinal));
    }

    private DiskBackedInteriorPagePipeline CreatePipeline(IArtworkClassifier? artworkClassifier = null) => new(
        artworkClassifier ?? new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector()),
        CreatePreparationService(),
        new MagickFrameProcessor(),
        new MagickWorkingPageProcessor(),
        new MagickFinalInteriorPageProcessor(),
        new MagickImageInspector());

    private static async Task SeedLegacyV3CropArtMetadataAsync(string cache, string frameMode)
    {
        var stamp = Path.Combine(cache, "input-stamp.json");
        var stampJson = await File.ReadAllTextAsync(stamp);
        using var stampDocument = JsonDocument.Parse(stampJson);
        var legacyStamp = stampDocument.RootElement.EnumerateObject()
            .Where(property => property.Name != "ClassificationPolicy")
            .ToDictionary(
                property => property.Name,
                property => property.Name switch
                {
                    "SchemaVersion" => (object)"interior-page-cache-v3",
                    "FrameMode" => frameMode,
                    _ => property.Value.Clone()
                });
        await File.WriteAllTextAsync(stamp, JsonSerializer.Serialize(legacyStamp));
        await File.WriteAllTextAsync(Path.Combine(cache, "classification.json"), JsonSerializer.Serialize(new
        {
            Version = ClassificationAlgorithmVersion.Current,
            Type = "cropart",
            BorderLine = new { HasBorder = false, Left = (int?)null, Right = (int?)null, Top = (int?)null, Bottom = (int?)null },
            BorderPixel = new { HasBorderPixel = false, LeftHit = false, RightHit = false, TopHit = false, BottomHit = false }
        }));
    }

    private sealed class ThrowingArtworkClassifier : IArtworkClassifier
    {
        public ValueTask<ArtworkClassificationResult> ClassifyAsync(
            ArtworkClassificationRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The classifier must not run for No Frame.");
    }

    private static ArtworkPreparationService CreatePreparationService() => new(
        new BorderArtPreparationProcessor(
            new MagickBorderBoundsCropProcessor(),
            new MagickSquareCropProcessor(),
            new MagickArtworkResizeProcessor()),
        new FullArtPreparationProcessor(
            new MagickArtworkTrimProcessor(),
            new MagickSquareCropProcessor(),
            new MagickArtworkResizeProcessor()),
        new CropArtPreparationProcessor(
            new MagickArtworkTrimProcessor(),
            new MagickSquarePadProcessor(),
            new MagickArtworkResizeProcessor()),
        new MagickImageInspector());

    private static InteriorPagePipelineRequest CreateRequest(
        BookWorkspace workspace,
        string source,
        string pageId,
        ImageSize targetSize) => new(
        workspace,
        new FileReference(source),
        pageId,
        new ArtworkDetectionThreshold(20),
        targetSize,
        targetSize,
        targetSize,
        new ImageDensity(300, 300),
        null,
        FrameMode.Disabled);

    private async Task<string> CreateArtworkSourceAsync(string filename)
    {
        var source = Path.Combine(rootPath, filename);
        using (var image = new MagickImage(MagickColors.White, 100, 100))
        {
            image.GetPixels().SetPixel(20, 20, [0, 0, 0]);
            image.GetPixels().SetPixel(79, 79, [0, 0, 0]);
            image.Write(source);
        }

        await Task.CompletedTask;
        return source;
    }

    private async Task<string> CreateRedFrameAsync(string filename, ImageSize size)
    {
        var path = Path.Combine(rootPath, filename);
        using (var image = new MagickImage(MagickColors.Red, (uint)size.Width, (uint)size.Height))
        {
            image.Write(path);
        }

        await Task.CompletedTask;
        return path;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return Task.CompletedTask;
    }
}
