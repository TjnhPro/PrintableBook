using System.Text.Json;
using PrintableBook.Core.Application.Desktop;

namespace PrintableBook.Desktop.Bridge;

/// <summary>
/// Parses and routes the narrow, versioned messages accepted from the WebView.
/// </summary>
internal sealed class WebViewBridgeRouter(IApplicationSnapshotService? snapshotService = null, IGlobalSettingsStore? settingsStore = null)
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
        if (request.Command == "app.refresh")
        {
            return snapshotService is null
                ? BridgeResponse.UnsupportedCommand(request.Id)
                : BridgeResponse.Succeeded(request.Id, "app.snapshot", await snapshotService.RefreshAsync(cancellationToken));
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
        "app.refresh" or "settings.save" => new BridgeResponse(Version, request.Id, true, null, null),
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
}
