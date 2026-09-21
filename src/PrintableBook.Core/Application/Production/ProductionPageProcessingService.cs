using System.Security.Cryptography;
using System.Text;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Core.Application.Production;

public sealed record ProductionPageProcessingResult(
    ProductionAssetKind AssetKind,
    FileReference Source,
    FileReference FinalPage,
    DateTimeOffset CompletedAtUtc);

public interface IProductionPageProcessingService
{
    ValueTask<ProductionPageProcessingResult> ProcessAsync(
        BookWorkspace workspace,
        ProductionAssetKind assetKind,
        GlobalSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class ProductionPageProcessingService(
    IFileSystem fileSystem,
    IInteriorPagePipeline pagePipeline,
    IProductionWorkspaceStateStore stateStore) : IProductionPageProcessingService
{
    public async ValueTask<ProductionPageProcessingResult> ProcessAsync(
        BookWorkspace workspace,
        ProductionAssetKind assetKind,
        GlobalSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(settings);
        var definition = ProductionAssets.Get(assetKind);
        if (definition.ProcessedFileName is null)
        {
            throw new ArgumentException("Only Production Interior assets can use the page pipeline.", nameof(assetKind));
        }

        var source = ProductionWorkspacePaths.SourceFile(workspace, assetKind);
        var sourceMetadata = await fileSystem.GetFileMetadataAsync(source, cancellationToken)
            ?? throw new FileNotFoundException("The Production source asset is missing.", source.Value);
        var completedAt = DateTimeOffset.UtcNow;
        var pipelineResult = await pagePipeline.ProcessAsync(new InteriorPagePipelineRequest(
            workspace,
            source,
            definition.StablePageId,
            new ArtworkDetectionThreshold(settings.ArtworkDetectionThreshold),
            new ImageSize(settings.ArtworkMaximumSide, settings.ArtworkMaximumSide),
            new ImageSize(settings.WorkingPageWidth, settings.WorkingPageHeight),
            new ImageSize(settings.FinalPageWidth, settings.FinalPageHeight),
            new ImageDensity(settings.Dpi, settings.Dpi),
            null,
            FrameMode.Disabled,
            settings.EffectiveArtworkSourceNormalization,
            settings.EffectiveBorderLineDetection,
            InteriorPageProcessingKind.ProductionInterior), cancellationToken);
        var outputMetadata = await fileSystem.GetFileMetadataAsync(pipelineResult.FinalPage, cancellationToken)
            ?? throw new IOException("The processed Production page metadata is unavailable.");
        var state = await stateStore.LoadAsync(workspace, cancellationToken);
        await stateStore.SaveAsync(
            workspace,
            state.RecordProcessedPage(
                assetKind,
                ProductionFileSignature.From(sourceMetadata),
                ProductionFileSignature.From(outputMetadata),
                CreateSettingsSignature(settings),
                completedAt),
            cancellationToken);
        return new ProductionPageProcessingResult(assetKind, source, pipelineResult.FinalPage, completedAt);
    }

    public static string CreateSettingsSignature(GlobalSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalization = settings.EffectiveArtworkSourceNormalization;
        var border = settings.EffectiveBorderLineDetection;
        var canonical = string.Join('|',
            "production-page-v1",
            settings.ArtworkDetectionThreshold,
            settings.ArtworkMaximumSide,
            settings.WorkingPageWidth,
            settings.WorkingPageHeight,
            settings.FinalPageWidth,
            settings.FinalPageHeight,
            settings.Dpi,
            normalization.NormalizedSourceSize,
            border.Pass1SearchDepth,
            border.Pass2SearchDepth,
            border.CornerSearchPadding,
            border.TrackDepthTolerance,
            border.CornerLineTolerance,
            border.MaximumDepthSpread,
            border.SegmentCount,
            border.CornerExclusionRatio,
            border.MinimumCompatibleCorners,
            border.MinimumSegmentSupportRatio,
            border.MinimumSideSupportRatio,
            border.MinimumSpanRatio,
            border.MinimumSupportedSegments,
            border.MaximumMissingSegmentRun);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
