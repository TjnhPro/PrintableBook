using System.Text.Json;
using System.Text.Json.Serialization;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Desktop.Bridge;

internal sealed class FrameModeJsonConverter : JsonConverter<FrameMode>
{
    public override FrameMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? reader.GetString() switch
            {
                "enabled" => FrameMode.Enabled,
                "disabled" => FrameMode.Disabled,
                _ => throw new JsonException("Frame mode must be 'enabled' or 'disabled'.")
            }
            : throw new JsonException("Frame mode must be a string.");

    public override void Write(Utf8JsonWriter writer, FrameMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            FrameMode.Enabled => "enabled",
            FrameMode.Disabled => "disabled",
            _ => throw new JsonException($"Frame mode '{value}' is not supported.")
        });
}
