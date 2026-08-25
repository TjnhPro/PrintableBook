using System.Text.Json;
using PrintableBook.Core.Application.Desktop;
using PrintableBook.Core.Application.Discovery;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Desktop.Bridge;

/// <summary>
/// Parses and routes the narrow, versioned messages accepted from the WebView.
/// </summary>
internal sealed class WebViewBridgeRouter(
    IApplicationSnapshotService? snapshotService = null,
    IGlobalSettingsStore? settingsStore = null,
    IProcessSessionService? processSessionService = null,
    IApplicationRootDiscovery? rootDiscovery = null,
    IBrandSettingsStore? brandSettingsStore = null,
    IBookCoverSelectionService? coverSelectionService = null,
    IInteriorFrameModeService? interiorFrameModeService = null,
    IBookAssetPreviewService? assetPreviewService = null,
    ILocalOutputActionService? outputActionService = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const int Version = 1;

    public BridgeResponse Handle(string? json)
    {
        return TryParseRequest(json, out var request)
            ? RouteSynchronous(request)
            : BridgeResponse.InvalidRequest();
    }

    public async ValueTask<BridgeResponse> HandleAsync(string? json, CancellationToken cancellationToken = default)
    {
        if (!TryParseRequest(json, out var request)) return BridgeResponse.InvalidRequest();

        try
        {
            var response = RouteSynchronous(request);
            if (response.Error is not null || response.Command is not null) return response;
            if (request.Command is "app.refresh" or "book.validate")
            {
                if (snapshotService is null) return BridgeResponse.UnsupportedCommand(request.Id);
                var snapshot = await snapshotService.RefreshAsync(cancellationToken);
                if (request.Command == "book.validate" &&
                    (request.Payload is not { } validationPayload ||
                     !validationPayload.TryGetProperty("bookId", out var bookId) ||
                     string.IsNullOrWhiteSpace(bookId.GetString()) ||
                     !snapshot.BookSummaries.Any(summary => summary.BookId.Value == bookId.GetString())))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "book_not_found");
                }

                return BridgeResponse.Succeeded(request.Id, "app.snapshot", snapshot);
            }

            if (request.Command == "book.cover.select")
            {
                if (snapshotService is null || coverSelectionService is null || request.Payload is not { } coverPayload ||
                    !coverPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !coverPayload.TryGetProperty("coverReference", out var coverElement) || string.IsNullOrWhiteSpace(coverElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_cover_selection");
                }

                try
                {
                    await coverSelectionService.SelectAsync(bookIdElement.GetString()!, coverElement.GetString()!, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "app.snapshot", await snapshotService.RefreshAsync(cancellationToken));
                }
                catch (ArgumentException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_cover_selection");
                }
            }

            if (request.Command == "book.interior.frame-mode.set")
            {
                if (snapshotService is null || interiorFrameModeService is null || request.Payload is not { } frameModePayload ||
                    !frameModePayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !frameModePayload.TryGetProperty("sourceReference", out var sourceElement) || string.IsNullOrWhiteSpace(sourceElement.GetString()) ||
                    !frameModePayload.TryGetProperty("mode", out var modeElement) || !TryParseFrameMode(modeElement, out var mode))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_frame_mode");
                }

                try
                {
                    await interiorFrameModeService.SetAsync(bookIdElement.GetString()!, sourceElement.GetString()!, mode, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "app.snapshot", await snapshotService.RefreshAsync(cancellationToken));
                }
                catch (ArgumentException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_interior_frame_mode");
                }
            }

            if (request.Command == "book.asset.preview.get")
            {
                if (assetPreviewService is null || request.Payload is not { } previewPayload ||
                    !previewPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !previewPayload.TryGetProperty("sourceReference", out var sourceElement) || string.IsNullOrWhiteSpace(sourceElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_asset_preview_request");
                }

                var preview = await assetPreviewService.GetAsync(bookIdElement.GetString()!, sourceElement.GetString()!, cancellationToken);
                return preview is null
                    ? new BridgeResponse(Version, request.Id, false, null, "asset_preview_not_found")
                    : BridgeResponse.Succeeded(request.Id, "book.asset.preview", preview);
            }

            if (request.Command is "book.output.open" or "book.output.reveal" or "book.output.copy-path")
            {
                if (snapshotService is null || outputActionService is null || request.Payload is not { } outputPayload ||
                    !outputPayload.TryGetProperty("bookId", out var bookIdElement) || string.IsNullOrWhiteSpace(bookIdElement.GetString()) ||
                    !outputPayload.TryGetProperty("artifactReference", out var artifactElement) || string.IsNullOrWhiteSpace(artifactElement.GetString()))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_output_action");
                }

                var snapshot = await snapshotService.RefreshAsync(cancellationToken);
                var book = snapshot.BookSummaries.FirstOrDefault(item => item.BookId.Value == bookIdElement.GetString());
                var artifact = artifactElement.GetString()!;
                if (book is null || !book.PublishedArtifacts.Contains(artifact, StringComparer.Ordinal) || !System.IO.File.Exists(artifact))
                {
                    return new BridgeResponse(Version, request.Id, false, null, "output_not_found");
                }

                var file = new PrintableBook.Core.Abstractions.FileReference(artifact);
                if (request.Command == "book.output.open") await outputActionService.OpenAsync(file, cancellationToken);
                if (request.Command == "book.output.reveal") await outputActionService.RevealAsync(file, cancellationToken);
                if (request.Command == "book.output.copy-path") await outputActionService.CopyPathAsync(file, cancellationToken);
                return BridgeResponse.Succeeded(request.Id, "book.output.action.completed", new { });
            }

            if (request.Command is "process.get" or "process.cancel" or "process.start")
            {
                if (processSessionService is null) return BridgeResponse.UnsupportedCommand(request.Id);
                try
                {
                    var process = request.Command switch
                    {
                        "process.get" => await processSessionService.GetAsync(cancellationToken),
                        "process.cancel" => await processSessionService.CancelAsync(cancellationToken),
                        "process.start" => await StartProcessAsync(request, processSessionService, cancellationToken),
                        _ => throw new InvalidOperationException("Unsupported process command.")
                    };
                    return BridgeResponse.Succeeded(request.Id, "process.snapshot", process);
                }
                catch (ArgumentException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Message);
                }
                catch (InvalidOperationException exception)
                {
                    return new BridgeResponse(Version, request.Id, false, null, exception.Message);
                }
            }

            if (request.Command is "brand.settings.get" or "brand.settings.save")
            {
                if (rootDiscovery is null || brandSettingsStore is null || request.Payload is not { } brandPayload ||
                    !brandPayload.TryGetProperty("brandName", out var brandNameElement) || string.IsNullOrWhiteSpace(brandNameElement.GetString()))
                {
                    return BridgeResponse.UnsupportedCommand(request.Id);
                }

                var brand = (await rootDiscovery.DiscoverAsync(cancellationToken)).Brands
                    .FirstOrDefault(item => string.Equals(item.Name, brandNameElement.GetString(), StringComparison.Ordinal));
                if (brand is null) return new BridgeResponse(Version, request.Id, false, null, "brand_not_found");

                try
                {
                    if (request.Command == "brand.settings.get")
                    {
                        return BridgeResponse.Succeeded(request.Id, "brand.settings", await brandSettingsStore.LoadAsync(brand.Directory, cancellationToken));
                    }

                    if (!brandPayload.TryGetProperty("json", out var jsonElement) || jsonElement.ValueKind != JsonValueKind.String)
                    {
                        return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_settings");
                    }
                    await brandSettingsStore.SaveAsync(brand.Directory, jsonElement.GetString()!, cancellationToken);
                    return BridgeResponse.Succeeded(request.Id, "brand.settings.saved", jsonElement.GetString()!);
                }
                catch (JsonException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_settings");
                }
                catch (ArgumentException)
                {
                    return new BridgeResponse(Version, request.Id, false, null, "invalid_brand_settings");
                }
            }

            if (request.Command != "settings.save" || settingsStore is null || request.Payload is not { } payload)
            {
                return BridgeResponse.UnsupportedCommand(request.Id);
            }

            try
            {
                var settings = payload.Deserialize<GlobalSettings>(JsonOptions);
                if (settings is null) return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
                await settingsStore.SaveAsync(settings, cancellationToken);
                return BridgeResponse.Succeeded(request.Id, "settings.saved", settings);
            }
            catch (JsonException)
            {
                return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
            }
            catch (ArgumentOutOfRangeException)
            {
                return new BridgeResponse(Version, request.Id, false, null, "invalid_settings");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return BridgeResponse.Failed(request.Id, $"{request.Command.Replace('.', '_')}_failed", exception);
        }
    }

    private static BridgeResponse RouteSynchronous(BridgeRequest request) => request.Command switch
    {
        "app.ping" => BridgeResponse.Pong(request.Id),
        "app.refresh" or "book.validate" or "book.cover.select" or "book.interior.frame-mode.set" or "book.asset.preview.get" or "book.output.open" or "book.output.reveal" or "book.output.copy-path" or "settings.save" or "process.get" or "process.cancel" or "process.start" or "brand.settings.get" or "brand.settings.save" => new BridgeResponse(Version, request.Id, true, null, null),
        _ => BridgeResponse.UnsupportedCommand(request.Id)
    };

    private static bool TryParseRequest(string? json, out BridgeRequest request)
    {
        request = default!;
        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions)!;
            return request is not null &&
                request.Version == Version &&
                !string.IsNullOrWhiteSpace(request.Id) &&
                !string.IsNullOrWhiteSpace(request.Command);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseFrameMode(JsonElement value, out FrameMode mode)
    {
        mode = value.ValueKind is JsonValueKind.String
            ? value.GetString() switch
            {
                "auto" => FrameMode.Auto,
                "enabled" => FrameMode.Enabled,
                "disabled" => FrameMode.Disabled,
                _ => default
            }
            : default;
        return value.ValueKind is JsonValueKind.String && value.GetString() is "auto" or "enabled" or "disabled";
    }

    private static async ValueTask<ProcessSessionSnapshot> StartProcessAsync(BridgeRequest request, IProcessSessionService sessionService, CancellationToken cancellationToken)
    {
        if (request.Payload is not { } payload ||
            !payload.TryGetProperty("bookIds", out var bookIdsElement) ||
            bookIdsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("A process start request requires Book ids.");
        }

        var bookIds = bookIdsElement.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.String)
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var brandName = payload.TryGetProperty("brandName", out var brandElement) && brandElement.ValueKind == JsonValueKind.String
            ? brandElement.GetString()
            : null;
        var mode = payload.TryGetProperty("mode", out var modeElement) && modeElement.ValueKind == JsonValueKind.String
            ? modeElement.GetString() switch
            {
                "interior-only" => BookProcessingMode.InteriorOnly,
                "full-book" => BookProcessingMode.FullBook,
                _ => throw new ArgumentException("The requested processing mode is not supported.")
            }
            : throw new ArgumentException("A process start request requires a processing mode.");
        return await sessionService.StartAsync(bookIds, brandName, mode, cancellationToken);
    }
}
