using System.Text.Json;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Processing;

/// <summary>
/// Orchestrates the classified interior workflow through disk-backed, independently readable stages.
/// </summary>
public sealed class DiskBackedInteriorPagePipeline(
    IArtworkSourceNormalizer artworkSourceNormalizer,
    IArtworkClassifier artworkClassifier,
    IArtworkPreparationService artworkPreparationService,
    IFrameProcessor frameProcessor,
    IWorkingPageProcessor workingPageProcessor,
    IFinalInteriorPageProcessor finalPageProcessor,
    IImageInspector imageInspector) : IInteriorPagePipeline
{
    private const string CacheStampSchemaVersion = "interior-page-cache-v4";
    private const string LegacyCacheStampSchemaVersion = "interior-page-cache-v3";
    private const string ClassificationCacheSchemaVersion = "artwork-classification-cache-v2";
    private const string DetectedPolicy = "detected-v1";
    private const string ForcedNoFramePolicy = "forced-no-frame-v1";
    private const string ForcedIntroPolicy = "forced-intro-v1";

    public DiskBackedInteriorPagePipeline(
        IArtworkClassifier artworkClassifier,
        IArtworkPreparationService artworkPreparationService,
        IFrameProcessor frameProcessor,
        IWorkingPageProcessor workingPageProcessor,
        IFinalInteriorPageProcessor finalPageProcessor,
        IImageInspector imageInspector)
        : this(new Imaging.MagickArtworkSourceNormalizer(), artworkClassifier, artworkPreparationService, frameProcessor, workingPageProcessor, finalPageProcessor, imageInspector)
    {
    }

    public async ValueTask<InteriorPageProcessingResult> ProcessAsync(
        InteriorPagePipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PageId))
        {
            throw new ArgumentException("Page identity is required.", nameof(request));
        }

        request.ValidateGeometry();
        var pageCache = Path.Combine(request.Workspace.WorkingDirectory.Value, "cache", request.PageId);
        var isIntroTemplate = request.ProcessingKind is InteriorPageProcessingKind.IntroTemplate or InteriorPageProcessingKind.BrandIntroTemplate;
        var processedInteriorDirectory = Path.Combine(request.Workspace.ProcessedDirectory.Value, isIntroTemplate ? "intro" : "interior");
        var classificationFile = Path.Combine(pageCache, "classification.json");
        var normalized = new FileReference(Path.Combine(pageCache, "normalized-source.png"));
        var prepared = new FileReference(Path.Combine(pageCache, "prepared.png"));
        var framed = new FileReference(Path.Combine(pageCache, "framed.png"));
        var working = new FileReference(Path.Combine(pageCache, "working-page.png"));
        var finalPage = new FileReference(Path.Combine(processedInteriorDirectory, $"{request.PageId}.png"));
        var cacheStampFile = Path.Combine(pageCache, "input-stamp.json");
        var legacyCacheStampFile = Path.Combine(processedInteriorDirectory, $"{request.PageId}.input-stamp.json");
        var currentStep = "classification";

        try
        {
            if (request.ProcessingKind == InteriorPageProcessingKind.BrandIntroTemplate &&
                (await imageInspector.GetInfoAsync(request.Source, cancellationToken)).Size == request.FinalPageSize)
            {
                currentStep = "final-artwork";
                return new InteriorPageProcessingResult(request.PageId, request.Source, request.Source);
            }

            Directory.CreateDirectory(processedInteriorDirectory);
            Directory.CreateDirectory(pageCache);
            MigrateLegacyCacheStamp(legacyCacheStampFile, cacheStampFile);
            var classificationPolicy = ResolveClassificationPolicy(request);
            var currentStamp = CacheInputStamp.Create(request, classificationPolicy);
            var previousStamp = await TryReadCacheStampAsync(cacheStampFile, cancellationToken);
            var invalidation = previousStamp is null
                ? CacheInvalidationStage.Classification
                : DetermineInvalidationStage(previousStamp, currentStamp);
            if (invalidation is not CacheInvalidationStage.None)
            {
                ApplyInvalidation(invalidation, normalized, classificationFile, prepared, framed, working, finalPage);
            }

            if (!await IsReadableAsync(normalized, new ImageSize(request.ArtworkSourceNormalization.NormalizedSourceSize, request.ArtworkSourceNormalization.NormalizedSourceSize), cancellationToken))
            {
                DeleteDownstream(prepared, framed, working, finalPage);
                currentStep = "normalization";
                await ValidateRawIntroTemplateSizeAsync(request, cancellationToken);
                await artworkSourceNormalizer.NormalizeAsync(new ArtworkSourceNormalizationRequest(
                    request.Source, normalized, new ImageSize(request.ArtworkSourceNormalization.NormalizedSourceSize, request.ArtworkSourceNormalization.NormalizedSourceSize)), cancellationToken);
                await EnsureSizeAsync(normalized, new ImageSize(request.ArtworkSourceNormalization.NormalizedSourceSize, request.ArtworkSourceNormalization.NormalizedSourceSize), "Normalized source", cancellationToken);
            }

            var classification = invalidation is CacheInvalidationStage.Classification
                ? null
                : await TryReadClassificationAsync(
                    classificationFile,
                    previousStamp?.SchemaVersion,
                    previousStamp is null ? classificationPolicy : ResolveClassificationPolicy(previousStamp),
                    cancellationToken);
            if (isIntroTemplate && classification is not { Type: ArtworkType.CropArt, Origin: ArtworkClassificationOrigin.ForcedIntro })
            {
                classification = null;
            }

            if (classification is not null && previousStamp?.SchemaVersion == LegacyCacheStampSchemaVersion)
            {
                await WriteClassificationAsync(classificationFile, classification, cancellationToken);
            }

            if (classification is not null && await IsReadableAsync(finalPage, request.FinalPageSize, cancellationToken))
            {
                if (previousStamp?.SchemaVersion == LegacyCacheStampSchemaVersion)
                {
                    await WriteJsonAtomicallyAsync(cacheStampFile, currentStamp, cancellationToken);
                }

                return new InteriorPageProcessingResult(request.PageId, request.Source, finalPage);
            }

            if (classification is null)
            {
                DeleteDownstream(prepared, framed, working, finalPage);
                currentStep = "classification";
                classification = classificationPolicy switch
                {
                    ForcedIntroPolicy => EffectiveArtworkClassification.ForcedIntro(),
                    ForcedNoFramePolicy => EffectiveArtworkClassification.ForcedNoFrame(),
                    DetectedPolicy => EffectiveArtworkClassification.FromDetection(
                        await artworkClassifier.ClassifyAsync(
                            new ArtworkClassificationRequest(normalized, request.ArtworkDetectionThreshold, request.BorderLineDetection), cancellationToken)),
                    _ => throw new InvalidOperationException("The classification policy is not supported.")
                };
                await WriteClassificationAsync(classificationFile, classification, cancellationToken);
            }

            PreparedArtwork preparedArtwork;
            if (!await IsReadableAsync(prepared, request.PreparedArtworkSize, cancellationToken))
            {
                DeleteDownstream(framed, working, finalPage);
                currentStep = "preparation";
                preparedArtwork = await artworkPreparationService.PrepareAsync(new ArtworkPreparationRequest(
                    normalized,
                    prepared,
                    classification,
                    request.ArtworkDetectionThreshold,
                    request.PreparedArtworkSize,
                    request.TargetDensity), cancellationToken);
                await EnsureSizeAsync(prepared, request.PreparedArtworkSize, "Prepared artwork", cancellationToken);
            }
            else
            {
                preparedArtwork = PreparedArtwork.FromCached(prepared, classification.Type);
            }

            var frame = isIntroTemplate ? null : request.Frame;
            var shouldApplyFrame = !isIntroTemplate && ShouldApplyFrame(
                frame is not null && File.Exists(frame.Value),
                request.FrameMode,
                preparedArtwork.AutoFrameRecommended);
            if (!await IsReadableAsync(framed, request.PreparedArtworkSize, cancellationToken) ||
                (!shouldApplyFrame && !FilesMatch(prepared, framed)))
            {
                DeleteDownstream(working, finalPage);
                currentStep = "frame";
                await frameProcessor.ApplyAsync(new FrameOverlayRequest(prepared, framed, frame, shouldApplyFrame), cancellationToken);
                await EnsureSizeAsync(framed, request.PreparedArtworkSize, "Framed artwork", cancellationToken);
            }

            if (!await IsReadableAsync(working, request.WorkingPageSize, cancellationToken))
            {
                DeleteDownstream(finalPage);
                currentStep = "working-page";
                await workingPageProcessor.CenterAsync(
                    new WorkingPageRequest(framed, working, request.WorkingPageSize), cancellationToken);
                await EnsureSizeAsync(working, request.WorkingPageSize, "Working page", cancellationToken);
            }

            if (!await IsReadableAsync(finalPage, request.FinalPageSize, cancellationToken))
            {
                currentStep = "final-page";
                await finalPageProcessor.ProduceAsync(
                    new FinalInteriorPageRequest(working, finalPage, request.FinalPageSize, request.TargetDensity), cancellationToken);
                await EnsureSizeAsync(finalPage, request.FinalPageSize, "Final page", cancellationToken);
            }

            if (previousStamp is null ||
                previousStamp.SchemaVersion == LegacyCacheStampSchemaVersion ||
                invalidation is not CacheInvalidationStage.None)
            {
                await WriteJsonAtomicallyAsync(cacheStampFile, currentStamp, cancellationToken);
            }

            return new InteriorPageProcessingResult(request.PageId, request.Source, finalPage);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InteriorPageProcessingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InteriorPageProcessingException(request.PageId, currentStep, exception, request.ProcessingKind);
        }
    }

    private async ValueTask EnsureSizeAsync(FileReference image, ImageSize expectedSize, string stage, CancellationToken cancellationToken)
    {
        if (!await IsReadableAsync(image, expectedSize, cancellationToken))
        {
            throw new InvalidDataException($"{stage} must be a readable {expectedSize.Width}x{expectedSize.Height} raster.");
        }
    }

    private async ValueTask ValidateRawIntroTemplateSizeAsync(InteriorPagePipelineRequest request, CancellationToken cancellationToken)
    {
        if (request.ProcessingKind is not (InteriorPageProcessingKind.IntroTemplate or InteriorPageProcessingKind.BrandIntroTemplate)) return;
        var size = (await imageInspector.GetInfoAsync(request.Source, cancellationToken)).Size;
        if (size.Width != size.Height || size.Width is not 1024 and not 2048)
        {
            throw new InvalidDataException("IntroTemplate artwork must be a readable 1024×1024 or 2048×2048 raster.");
        }
    }

    private static string ResolveClassificationPolicy(InteriorPagePipelineRequest request) => request.ProcessingKind switch
    {
        InteriorPageProcessingKind.IntroTemplate or InteriorPageProcessingKind.BrandIntroTemplate => ForcedIntroPolicy,
        InteriorPageProcessingKind.Interior when request.FrameMode == FrameMode.Disabled => ForcedNoFramePolicy,
        InteriorPageProcessingKind.Interior => DetectedPolicy,
        _ => throw new ArgumentOutOfRangeException(nameof(request), request.ProcessingKind, "Unsupported page processing kind.")
    };

    private async ValueTask<bool> IsReadableAsync(FileReference image, ImageSize expectedSize, CancellationToken cancellationToken)
    {
        if (!File.Exists(image.Value))
        {
            return false;
        }

        try
        {
            return (await imageInspector.GetInfoAsync(image, cancellationToken)).Size == expectedSize;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask<CacheInputStamp?> TryReadCacheStampAsync(string cacheStampFile, CancellationToken cancellationToken)
    {
        if (!File.Exists(cacheStampFile))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cacheStampFile, cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object ||
                !CacheInputStamp.HasRequiredProperties(document.RootElement))
            {
                return null;
            }

            var stamp = JsonSerializer.Deserialize<CacheInputStamp>(json);
            return stamp?.SchemaVersion switch
            {
                CacheStampSchemaVersion when !string.IsNullOrWhiteSpace(stamp.ClassificationPolicy) => stamp,
                LegacyCacheStampSchemaVersion => stamp,
                _ => null
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or NotSupportedException)
        {
            return null;
        }
    }

    private static void MigrateLegacyCacheStamp(string legacyStampFile, string cacheStampFile)
    {
        if (!File.Exists(legacyStampFile))
        {
            return;
        }

        if (File.Exists(cacheStampFile))
        {
            File.Delete(legacyStampFile);
            return;
        }

        var cacheDirectory = Path.GetDirectoryName(cacheStampFile)
            ?? throw new InvalidOperationException("The cache stamp must have a parent directory.");
        Directory.CreateDirectory(cacheDirectory);
        File.Move(legacyStampFile, cacheStampFile);
    }

    private static CacheInvalidationStage DetermineInvalidationStage(CacheInputStamp previous, CacheInputStamp current)
    {
        if (!NormalizedSourceCompatible(previous, current)) return CacheInvalidationStage.NormalizedSource;
        if (!ClassificationCompatible(previous, current)) return CacheInvalidationStage.Classification;
        if (!PreparationCompatible(previous, current)) return CacheInvalidationStage.Preparation;
        if (!FrameCompatible(previous, current)) return CacheInvalidationStage.Frame;
        if (!WorkingCompatible(previous, current)) return CacheInvalidationStage.Working;
        if (!FinalCompatible(previous, current)) return CacheInvalidationStage.Final;
        return CacheInvalidationStage.None;
    }

    private static bool NormalizedSourceCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        string.Equals(previous.SourcePath, current.SourcePath, StringComparison.OrdinalIgnoreCase) &&
        previous.SourceLength == current.SourceLength &&
        previous.SourceLastWriteUtcTicks == current.SourceLastWriteUtcTicks &&
        previous.NormalizedSourceSize == current.NormalizedSourceSize &&
        string.Equals(previous.NormalizationAlgorithmVersion, current.NormalizationAlgorithmVersion, StringComparison.Ordinal);

    private static bool ClassificationCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        string.Equals(previous.ProcessingKind, current.ProcessingKind, StringComparison.Ordinal) &&
        string.Equals(ResolveClassificationPolicy(previous), ResolveClassificationPolicy(current), StringComparison.Ordinal) &&
        (!string.Equals(ResolveClassificationPolicy(current), DetectedPolicy, StringComparison.Ordinal) ||
         (previous.ArtworkDetectionThreshold == current.ArtworkDetectionThreshold &&
          string.Equals(previous.BorderLineAlgorithmVersion, current.BorderLineAlgorithmVersion, StringComparison.Ordinal) &&
          string.Equals(previous.BorderLineSettingsFingerprint, current.BorderLineSettingsFingerprint, StringComparison.Ordinal) &&
          string.Equals(previous.ClassificationAlgorithmVersion, current.ClassificationAlgorithmVersion, StringComparison.Ordinal)));

    private static bool PreparationCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        string.Equals(previous.ArtworkPreparationAlgorithmVersion, current.ArtworkPreparationAlgorithmVersion, StringComparison.Ordinal) &&
        previous.ArtworkDetectionThreshold == current.ArtworkDetectionThreshold &&
        previous.PreparedArtworkWidth == current.PreparedArtworkWidth &&
        previous.PreparedArtworkHeight == current.PreparedArtworkHeight &&
        previous.TargetDensityHorizontal == current.TargetDensityHorizontal &&
        previous.TargetDensityVertical == current.TargetDensityVertical;

    private static bool FrameCompatible(CacheInputStamp previous, CacheInputStamp current)
    {
        var currentPolicy = ResolveClassificationPolicy(current);
        if (currentPolicy is ForcedNoFramePolicy or ForcedIntroPolicy)
        {
            return true;
        }

        return string.Equals(previous.FramePath, current.FramePath, StringComparison.OrdinalIgnoreCase) &&
               previous.FrameLength == current.FrameLength &&
               previous.FrameLastWriteUtcTicks == current.FrameLastWriteUtcTicks &&
               previous.FrameMode == current.FrameMode;
    }

    private static bool WorkingCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        previous.WorkingPageWidth == current.WorkingPageWidth &&
        previous.WorkingPageHeight == current.WorkingPageHeight;

    private static bool FinalCompatible(CacheInputStamp previous, CacheInputStamp current) =>
        previous.FinalPageWidth == current.FinalPageWidth &&
        previous.FinalPageHeight == current.FinalPageHeight;

    private static void ApplyInvalidation(
        CacheInvalidationStage stage,
        FileReference normalized,
        string classificationFile,
        FileReference prepared,
        FileReference framed,
        FileReference working,
        FileReference finalPage)
    {
        switch (stage)
        {
            case CacheInvalidationStage.None:
                break;
            case CacheInvalidationStage.Final:
                DeleteIfPresent(finalPage);
                break;
            case CacheInvalidationStage.Working:
                DeleteDownstream(working, finalPage);
                break;
            case CacheInvalidationStage.Frame:
                DeleteDownstream(framed, working, finalPage);
                break;
            case CacheInvalidationStage.Preparation:
                DeleteDownstream(prepared, framed, working, finalPage);
                break;
            case CacheInvalidationStage.Classification:
                DeleteIfPresent(classificationFile);
                DeleteDownstream(prepared, framed, working, finalPage);
                break;
            case CacheInvalidationStage.NormalizedSource:
                DeleteIfPresent(normalized);
                DeleteIfPresent(classificationFile);
                DeleteDownstream(prepared, framed, working, finalPage);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
        }
    }

    private static async ValueTask WriteClassificationAsync(string file, EffectiveArtworkClassification result, CancellationToken cancellationToken) =>
        await WriteJsonAtomicallyAsync(file, ClassificationCacheEntry.From(result), cancellationToken);

    private static async ValueTask<EffectiveArtworkClassification?> TryReadClassificationAsync(
        string file,
        string? stampSchemaVersion,
        string classificationPolicy,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(file, cancellationToken);
            if (stampSchemaVersion == LegacyCacheStampSchemaVersion)
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.TryGetProperty(nameof(ClassificationCacheEntry.SchemaVersion), out var schema) &&
                    string.Equals(schema.GetString(), ClassificationCacheSchemaVersion, StringComparison.Ordinal))
                {
                    var partiallyMigrated = JsonSerializer.Deserialize<ClassificationCacheEntry>(json)?.ToDecision();
                    return DecisionMatchesPolicy(partiallyMigrated, classificationPolicy) ? partiallyMigrated : null;
                }

                var legacy = JsonSerializer.Deserialize<LegacyClassificationCacheEntry>(json);
                return legacy?.ToDecision(classificationPolicy);
            }

            var entry = JsonSerializer.Deserialize<ClassificationCacheEntry>(json);
            return entry?.ToDecision();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or IOException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool DecisionMatchesPolicy(EffectiveArtworkClassification? decision, string policy) =>
        (decision?.Origin, policy) switch
        {
            (ArtworkClassificationOrigin.Detected, DetectedPolicy) => true,
            (ArtworkClassificationOrigin.ForcedNoFrame, ForcedNoFramePolicy) => true,
            (ArtworkClassificationOrigin.ForcedIntro, ForcedIntroPolicy) => true,
            _ => false
        };

    private static void DeleteDownstream(params FileReference[] files)
    {
        foreach (var file in files)
        {
            DeleteIfPresent(file);
        }
    }

    private static void DeleteIfPresent(FileReference file)
    {
        if (File.Exists(file.Value))
        {
            File.Delete(file.Value);
        }
    }

    private static void DeleteIfPresent(string file)
    {
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    private static bool ShouldApplyFrame(bool frameAvailable, FrameMode mode, bool autoFrameRecommended) =>
        frameAvailable && (mode switch
        {
            FrameMode.Auto => autoFrameRecommended,
            FrameMode.Enabled => true,
            FrameMode.Disabled => false,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported frame mode.")
        });

    private static bool FilesMatch(FileReference first, FileReference second)
    {
        const int bufferSize = 64 * 1024;
        using var left = new FileStream(first.Value, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        using var right = new FileStream(second.Value, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan);
        if (left.Length != right.Length)
        {
            return false;
        }

        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];
        while (true)
        {
            var leftRead = left.Read(leftBuffer);
            var rightRead = right.Read(rightBuffer);
            if (leftRead != rightRead || !leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }
        }
    }

    private static async ValueTask WriteJsonAtomicallyAsync<T>(string file, T value, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(file)
            ?? throw new ArgumentException("The cache metadata path must include a directory.", nameof(file));
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(file)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value), cancellationToken);
            File.Move(temporary, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private enum CacheInvalidationStage
    {
        None,
        Final,
        Working,
        Frame,
        Preparation,
        Classification,
        NormalizedSource
    }

    private sealed record CacheInputStamp(
        string SourcePath,
        long SourceLength,
        long SourceLastWriteUtcTicks,
        byte ArtworkDetectionThreshold,
        string ClassificationAlgorithmVersion,
        string ArtworkPreparationAlgorithmVersion,
        int PreparedArtworkWidth,
        int PreparedArtworkHeight,
        int WorkingPageWidth,
        int WorkingPageHeight,
        int FinalPageWidth,
        int FinalPageHeight,
        double TargetDensityHorizontal,
        double TargetDensityVertical,
        string? FramePath,
        long FrameLength,
        long FrameLastWriteUtcTicks,
        string FrameMode,
        int NormalizedSourceSize,
        string NormalizationAlgorithmVersion,
        string BorderLineAlgorithmVersion,
        string BorderLineSettingsFingerprint,
        string ProcessingKind,
        string? ClassificationPolicy,
        string SchemaVersion)
    {
        private static readonly string[] requiredProperties =
        [
            nameof(SourcePath),
            nameof(SourceLength),
            nameof(SourceLastWriteUtcTicks),
            nameof(ArtworkDetectionThreshold),
            nameof(ClassificationAlgorithmVersion),
            nameof(ArtworkPreparationAlgorithmVersion),
            nameof(PreparedArtworkWidth),
            nameof(PreparedArtworkHeight),
            nameof(WorkingPageWidth),
            nameof(WorkingPageHeight),
            nameof(FinalPageWidth),
            nameof(FinalPageHeight),
            nameof(TargetDensityHorizontal),
            nameof(TargetDensityVertical),
            nameof(FramePath),
            nameof(FrameLength),
            nameof(FrameLastWriteUtcTicks),
            nameof(FrameMode),
            nameof(NormalizedSourceSize),
            nameof(NormalizationAlgorithmVersion),
            nameof(BorderLineAlgorithmVersion),
            nameof(BorderLineSettingsFingerprint),
            nameof(ProcessingKind),
            nameof(SchemaVersion)
        ];

        public static bool HasRequiredProperties(JsonElement stamp) =>
            requiredProperties.All(property => stamp.TryGetProperty(property, out _));

        public static CacheInputStamp Create(InteriorPagePipelineRequest request, string classificationPolicy)
        {
            var source = new FileInfo(request.Source.Value);
            if (!source.Exists)
            {
                throw new FileNotFoundException("The interior source image does not exist.", request.Source.Value);
            }

            var frame = request.Frame is null ? null : new FileInfo(request.Frame.Value);
            return new CacheInputStamp(
                source.FullName,
                source.Length,
                source.LastWriteTimeUtc.Ticks,
                request.ArtworkDetectionThreshold.Value,
                global::PrintableBook.Core.Application.Processing.ClassificationAlgorithmVersion.Current,
                global::PrintableBook.Core.Application.Processing.ArtworkPreparationAlgorithmVersion.Current,
                request.PreparedArtworkSize.Width,
                request.PreparedArtworkSize.Height,
                request.WorkingPageSize.Width,
                request.WorkingPageSize.Height,
                request.FinalPageSize.Width,
                request.FinalPageSize.Height,
                request.TargetDensity.Horizontal,
                request.TargetDensity.Vertical,
                request.Frame?.Value,
                frame?.Exists == true ? frame.Length : 0,
                frame?.Exists == true ? frame.LastWriteTimeUtc.Ticks : 0,
                ToCanonicalFrameMode(request.FrameMode),
                request.ArtworkSourceNormalization.NormalizedSourceSize,
                ArtworkSourceNormalizationAlgorithmVersion.Current,
                global::PrintableBook.Core.Application.Processing.BorderLineAlgorithmVersion.Current,
                JsonSerializer.Serialize(request.BorderLineDetection ?? BorderLineDetectionSettings.Default),
                request.ProcessingKind switch
                {
                    InteriorPageProcessingKind.Interior => "interior",
                    InteriorPageProcessingKind.IntroTemplate => "intro-template",
                    InteriorPageProcessingKind.BrandIntroTemplate => "intro-template",
                    _ => throw new ArgumentOutOfRangeException(nameof(request), request.ProcessingKind, "Unsupported page processing kind.")
                },
                classificationPolicy,
                CacheStampSchemaVersion);
        }

        private static string ToCanonicalFrameMode(FrameMode mode) => mode switch
        {
            global::PrintableBook.Core.Application.Processing.FrameMode.Auto => "auto",
            global::PrintableBook.Core.Application.Processing.FrameMode.Enabled => "enabled",
            global::PrintableBook.Core.Application.Processing.FrameMode.Disabled => "disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported frame mode.")
        };
    }

    private static string ResolveClassificationPolicy(CacheInputStamp stamp)
    {
        if (stamp.SchemaVersion == LegacyCacheStampSchemaVersion)
        {
            return string.Equals(stamp.ProcessingKind, "intro-template", StringComparison.Ordinal)
                ? ForcedIntroPolicy
                : DetectedPolicy;
        }

        return stamp.ClassificationPolicy switch
        {
            DetectedPolicy => DetectedPolicy,
            ForcedNoFramePolicy => ForcedNoFramePolicy,
            ForcedIntroPolicy => ForcedIntroPolicy,
            _ => "invalid-policy"
        };
    }

    private sealed record ClassificationCacheEntry(
        string SchemaVersion,
        string EffectiveType,
        string Origin,
        string DetectionStatus,
        string PolicyVersion,
        string? DetectorAlgorithmVersion,
        BorderLineCacheEntry? BorderLine,
        BorderPixelCacheEntry? BorderPixel)
    {
        public static ClassificationCacheEntry From(EffectiveArtworkClassification result) => new(
            ClassificationCacheSchemaVersion,
            ToCanonicalType(result.Type),
            ToCanonicalOrigin(result.Origin),
            result.DetectionStatus == ArtworkDetectionStatus.Completed ? "completed" : "not-run",
            result.Origin switch
            {
                ArtworkClassificationOrigin.Detected => DetectedPolicy,
                ArtworkClassificationOrigin.ForcedNoFrame => ForcedNoFramePolicy,
                ArtworkClassificationOrigin.ForcedIntro => ForcedIntroPolicy,
                _ => throw new ArgumentOutOfRangeException(nameof(result), result.Origin, "Unsupported classification origin.")
            },
            result.Detection is null ? null : ClassificationAlgorithmVersion.Current,
            result.Detection is null ? null : BorderLineCacheEntry.From(result.Detection.BorderLine),
            result.Detection?.BorderPixel is null ? null : BorderPixelCacheEntry.From(result.Detection.BorderPixel));

        public EffectiveArtworkClassification ToDecision()
        {
            if (!string.Equals(SchemaVersion, ClassificationCacheSchemaVersion, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cached classification uses an incompatible schema version.");
            }

            var type = FromCanonicalType(EffectiveType);
            return Origin switch
            {
                "detected" when DetectionStatus == "completed" &&
                                PolicyVersion == DetectedPolicy &&
                                DetectorAlgorithmVersion == ClassificationAlgorithmVersion.Current &&
                                BorderLine is not null =>
                    EffectiveArtworkClassification.FromDetection(
                        new ArtworkClassificationResult(type, BorderLine.ToResult(), BorderPixel?.ToResult())),
                "forced-no-frame" when type == ArtworkType.CropArt &&
                                            DetectionStatus == "not-run" &&
                                            PolicyVersion == ForcedNoFramePolicy &&
                                            DetectorAlgorithmVersion is null &&
                                            BorderLine is null &&
                                            BorderPixel is null => EffectiveArtworkClassification.ForcedNoFrame(),
                "forced-intro" when type == ArtworkType.CropArt &&
                                         DetectionStatus == "not-run" &&
                                         PolicyVersion == ForcedIntroPolicy &&
                                         DetectorAlgorithmVersion is null &&
                                         BorderLine is null &&
                                         BorderPixel is null => EffectiveArtworkClassification.ForcedIntro(),
                _ => throw new InvalidOperationException("Cached classification metadata is contradictory.")
            };
        }

        private static string ToCanonicalOrigin(ArtworkClassificationOrigin origin) => origin switch
        {
            ArtworkClassificationOrigin.Detected => "detected",
            ArtworkClassificationOrigin.ForcedNoFrame => "forced-no-frame",
            ArtworkClassificationOrigin.ForcedIntro => "forced-intro",
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unsupported classification origin.")
        };

        private static string ToCanonicalType(ArtworkType type) => type switch
        {
            ArtworkType.BorderArt => "borderart",
            ArtworkType.FullArt => "fullart",
            ArtworkType.CropArt => "cropart",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported artwork type.")
        };

        public static ArtworkType FromCanonicalType(string type) => type switch
        {
            "borderart" => ArtworkType.BorderArt,
            "fullart" => ArtworkType.FullArt,
            "cropart" => ArtworkType.CropArt,
            _ => throw new InvalidOperationException("Cached classification has an unknown artwork type.")
        };
    }

    private sealed record LegacyClassificationCacheEntry(
        string Version,
        string Type,
        BorderLineCacheEntry? BorderLine,
        BorderPixelCacheEntry? BorderPixel)
    {
        public EffectiveArtworkClassification ToDecision(string classificationPolicy)
        {
            if (!string.Equals(Version, ClassificationAlgorithmVersion.Current, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cached classification uses an incompatible algorithm version.");
            }

            var type = ClassificationCacheEntry.FromCanonicalType(Type);
            if (classificationPolicy == ForcedIntroPolicy)
            {
                if (type != ArtworkType.CropArt)
                {
                    throw new InvalidOperationException("Legacy Intro classification must be CropArt.");
                }

                return EffectiveArtworkClassification.ForcedIntro();
            }

            if (classificationPolicy != DetectedPolicy)
            {
                throw new InvalidOperationException("Legacy classification policy is not supported.");
            }

            if (BorderLine is null)
            {
                throw new InvalidOperationException("Legacy detector metadata is missing BorderLine evidence.");
            }

            return EffectiveArtworkClassification.FromDetection(
                new ArtworkClassificationResult(type, BorderLine.ToResult(), BorderPixel?.ToResult()));
        }
    }

    private sealed record BorderLineCacheEntry(bool HasBorder, int? Left, int? Right, int? Top, int? Bottom)
    {
        public static BorderLineCacheEntry From(BorderLineDetectionResult result) => new(
            result.HasBorder,
            result.Left.Position,
            result.Right.Position,
            result.Top.Position,
            result.Bottom.Position);

        public BorderLineDetectionResult ToResult()
        {
            if (!HasBorder)
            {
                return BorderLineDetectionResult.NoBorder();
            }

            if (Left is null || Right is null || Top is null || Bottom is null || Right < Left || Bottom < Top)
            {
                throw new InvalidOperationException("Cached BorderArt evidence has invalid bounds.");
            }

            return BorderLineDetectionResult.Detected(
                BorderLineSideResult.Detected(Left.Value),
                BorderLineSideResult.Detected(Right.Value),
                BorderLineSideResult.Detected(Top.Value),
                BorderLineSideResult.Detected(Bottom.Value),
                new ImageRectangle(new ImagePoint(Left.Value, Top.Value), new ImageSize(Right.Value - Left.Value + 1, Bottom.Value - Top.Value + 1)));
        }
    }

    private sealed record BorderPixelCacheEntry(bool HasBorderPixel, bool LeftHit, bool RightHit, bool TopHit, bool BottomHit)
    {
        public static BorderPixelCacheEntry From(BorderPixelDetectionResult result) => new(
            result.HasBorderPixel,
            result.LeftHit,
            result.RightHit,
            result.TopHit,
            result.BottomHit);

        public BorderPixelDetectionResult ToResult()
        {
            var result = BorderPixelDetectionResult.Detected(LeftHit, RightHit, TopHit, BottomHit);
            if (result.HasBorderPixel != HasBorderPixel)
            {
                throw new InvalidOperationException("Cached BorderPixel evidence is inconsistent.");
            }

            return result;
        }
    }
}
