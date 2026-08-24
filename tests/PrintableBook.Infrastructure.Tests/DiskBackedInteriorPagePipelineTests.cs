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
            FrameMode.Auto));

        Assert.Equal("page-01", result.PageId);
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "classification.json")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "framed.png")));
        Assert.True(File.Exists(Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "working-page.png")));
        var stamp = await File.ReadAllTextAsync(Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-01.input-stamp.json"));
        Assert.Contains(ArtworkPreparationAlgorithmVersion.Current, stamp, StringComparison.Ordinal);
        Assert.Contains(ClassificationAlgorithmVersion.Current, stamp, StringComparison.Ordinal);
        Assert.StartsWith(Path.Combine(workspace.WorkingDirectory.Value, "processed", "interior"), result.FinalPage.Value, StringComparison.OrdinalIgnoreCase);
        var finalInfo = await new MagickImageInspector().GetInfoAsync(result.FinalPage);
        Assert.Equal(new ImageSize(200, 200), finalInfo.Size);
        Assert.Equal(300, finalInfo.Density!.Value.Horizontal, precision: 2);
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
            FrameMode.Auto);

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
        Assert.Equal(ClassificationAlgorithmVersion.Current, document.RootElement.GetProperty("Version").GetString());
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
        Assert.Equal((int)ArtworkType.CropArt, classification.RootElement.GetProperty("Type").GetInt32());
        using var framed = new MagickImage(framedPath);
        Assert.Equal((byte)255, framed.GetPixels().GetPixel(0, 0)[0]);
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_when_source_threshold_or_algorithm_stamp_changes()
    {
        Directory.CreateDirectory(rootPath);
        var source = await CreateArtworkSourceAsync("stamp-source.png");
        var workspace = await new PhysicalBookWorkspaceFactory(new PhysicalFileSystem()).CreateAsync(
            new BookId("stamp-book"), new DirectoryReference(Path.Combine(rootPath, "StampBook")));
        var pipeline = CreatePipeline();
        var request = CreateRequest(workspace, source, "page-01", new ImageSize(200, 200));
        await pipeline.ProcessAsync(request);

        var prepared = Path.Combine(workspace.WorkingDirectory.Value, "cache", "page-01", "prepared.png");
        var stamp = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-01.input-stamp.json");

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
        Assert.Equal(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));

        var completed = await pipeline.ProcessAsync(request);
        using (var wrongSize = new MagickImage(MagickColors.White, 10, 10)) wrongSize.Write(working);
        File.Delete(completed.FinalPage.Value);
        await pipeline.ProcessAsync(request);
        Assert.Equal(new ImageSize(200, 200), (await new MagickImageInspector().GetInfoAsync(new FileReference(working))).Size);
    }

    [Theory]
    [InlineData(FrameMode.Enabled, FrameMode.Disabled)]
    [InlineData(FrameMode.Auto, FrameMode.Enabled)]
    [InlineData(FrameMode.Disabled, FrameMode.Auto)]
    public async Task ProcessAsync_rebuilds_from_frame_for_each_frame_mode_transition(FrameMode initialMode, FrameMode changedMode)
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

        Assert.Equal(classificationBefore, await File.ReadAllTextAsync(classification));
        Assert.Equal(retainedPreparedTime, File.GetLastWriteTimeUtc(prepared));
        Assert.NotEqual(staleDownstreamTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleDownstreamTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleDownstreamTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
    }

    [Fact]
    public async Task ProcessAsync_rebuilds_from_classification_when_detection_threshold_changes()
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

        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(classification));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(prepared));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(framed));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(working));
        Assert.NotEqual(staleTime, File.GetLastWriteTimeUtc(completed.FinalPage.Value));
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
        var stamp = Path.Combine(workspace.ProcessedDirectory.Value, "interior", "page-01.input-stamp.json");
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
            await File.WriteAllTextAsync(stamp, contents.Replace("interior-page-cache-v1", "incompatible-schema", StringComparison.Ordinal));
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

    private DiskBackedInteriorPagePipeline CreatePipeline() => new(
        new ArtworkClassifier(new MagickBorderLineDetector(), new MagickBorderPixelDetector()),
        CreatePreparationService(),
        new MagickFrameProcessor(),
        new MagickWorkingPageProcessor(),
        new MagickFinalInteriorPageProcessor(),
        new MagickImageInspector());

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
        FrameMode.Auto);

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
