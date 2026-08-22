namespace PrintableBook.Desktop.Bridge;

internal sealed record BridgeRequest(int Version, string Id, string Command);
