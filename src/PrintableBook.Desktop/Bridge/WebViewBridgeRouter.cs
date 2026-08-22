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
        if (string.IsNullOrWhiteSpace(json))
        {
            return BridgeResponse.InvalidRequest();
        }

        try
        {
            var request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions);
            if (request is null ||
                request.Version != Version ||
                string.IsNullOrWhiteSpace(request.Id) ||
                string.IsNullOrWhiteSpace(request.Command))
            {
                return BridgeResponse.InvalidRequest();
            }

            return request.Command switch
            {
                "app.ping" => BridgeResponse.Pong(request.Id),
                "app.refresh" or "settings.save" => new BridgeResponse(Version, request.Id, true, null, null),
                _ => BridgeResponse.UnsupportedCommand(request.Id)
            };
        }
        catch (JsonException)
        {
            return BridgeResponse.InvalidRequest();
        }
    }

    public async ValueTask<BridgeResponse> HandleAsync(string? json, CancellationToken cancellationToken = default)
    {
        var response = Handle(json);
        if (response.Error is not null || response.Command is not null || string.IsNullOrWhiteSpace(json)) return response;
        var request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions)!;
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
}
