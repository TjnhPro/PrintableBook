using System.Text.Json;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Desktop.Bridge;

/// <summary>
/// Parses and routes the narrow, versioned messages accepted from the WebView.
/// </summary>
internal sealed class WebViewBridgeRouter(
    IApplicationSnapshotService? snapshotService = null,
    IGlobalSettingsStore? settingsStore = null,
    IProcessSessionService? processSessionService = null)
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

    private static BridgeResponse RouteSynchronous(BridgeRequest request) => request.Command switch
    {
        "app.ping" => BridgeResponse.Pong(request.Id),
        "app.refresh" or "book.validate" or "settings.save" or "process.get" or "process.cancel" or "process.start" => new BridgeResponse(Version, request.Id, true, null, null),
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
        return await sessionService.StartAsync(bookIds, brandName, cancellationToken);
    }
}
