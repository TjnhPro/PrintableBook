using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;
using PrintableBook.Core.Domain.Processing;

namespace PrintableBook.Infrastructure.Workspaces;

public sealed class JsonBookWorkspaceStateStore(IFileSystem fileSystem) : IBookWorkspaceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions LogJsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<BookProcessingState?> LoadAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
        => (await LoadWithMetadataAsync(workspace, cancellationToken)).State;

    public async ValueTask<BookWorkspaceStateLoadResult> LoadWithMetadataAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var stateFile = StateFile(workspace);
        if (!await fileSystem.FileExistsAsync(stateFile, cancellationToken))
        {
            return new(null, BookProcessingState.CurrentFrameModeContractVersion, LegacyFrameContractDetected: false);
        }

        try
        {
            var content = await fileSystem.ReadTextAsync(stateFile, cancellationToken);
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The workspace state root must be an object.");
            }

            var sourceVersion = ReadFrameModeContractVersion(document.RootElement);
            var legacy = sourceVersion == 1;
            var (overrides, explicitAutoKeys, explicitFrameKeys) = ReadFrameOverrides(document.RootElement, legacy);
            var sanitized = JsonNode.Parse(content) as JsonObject
                ?? throw new InvalidDataException("The workspace state root must be an object.");
            var frameProperty = sanitized
                .Select(property => property.Key)
                .FirstOrDefault(key => string.Equals(key, "interiorFrameOverrides", StringComparison.OrdinalIgnoreCase));
            if (frameProperty is not null) sanitized.Remove(frameProperty);

            var state = JsonSerializer.Deserialize<BookProcessingState>(sanitized.ToJsonString(), JsonOptions)
                ?? throw new InvalidDataException("The workspace state file is empty or invalid.");
            var normalized = state with
            {
                FrameModeContractVersion = BookProcessingState.CurrentFrameModeContractVersion,
                InteriorFrameOverrides = overrides,
                InactiveInteriorSourceKeys = state.InactiveInteriorSourceKeys?
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                    .ToArray() is { Length: > 0 } inactive ? inactive : null,
                Metadata = state.Metadata?.Normalize(),
                AssignedBrand = string.IsNullOrWhiteSpace(state.AssignedBrand) ? null : state.AssignedBrand.Trim()
            };
            return new(normalized, sourceVersion, legacy, explicitAutoKeys, explicitFrameKeys);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException or InvalidDataException)
        {
            throw new InvalidDataException(
                $"Workspace state for Book '{workspace.BookId.Value}' at '{stateFile.Value}' is invalid. The file was not changed. {exception.Message}",
                exception);
        }
    }

    public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        var normalized = state with
        {
            FrameModeContractVersion = BookProcessingState.CurrentFrameModeContractVersion,
            InteriorFrameOverrides = NormalizeFrameOverrides(state.InteriorFrameOverrides),
            Metadata = state.Metadata?.Normalize(),
            AssignedBrand = string.IsNullOrWhiteSpace(state.AssignedBrand) ? null : state.AssignedBrand.Trim()
        };
        return fileSystem.WriteTextAtomicallyAsync(StateFile(workspace), JsonSerializer.Serialize(normalized, JsonOptions), cancellationToken);
    }

    public ValueTask AppendLogAsync(BookWorkspace workspace, BookProcessingLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var logFile = Path.Combine(workspace.WorkingDirectory.Value, "logs", "processing.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        File.AppendAllText(logFile, JsonSerializer.Serialize(entry, LogJsonOptions) + Environment.NewLine);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<IReadOnlyList<BookProcessingLogEntry>> LoadLogsAsync(BookWorkspace workspace, CancellationToken cancellationToken = default)
    {
        var logFile = Path.Combine(workspace.WorkingDirectory.Value, "logs", "processing.jsonl");
        if (!File.Exists(logFile)) return [];

        try
        {
            return ParseLogs(await File.ReadAllTextAsync(logFile, cancellationToken));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The workspace processing log '{logFile}' is invalid.", exception);
        }
    }

    public ValueTask SaveErrorAsync(BookWorkspace workspace, ProcessingFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return fileSystem.WriteTextAtomicallyAsync(
            new FileReference(Path.Combine(workspace.WorkingDirectory.Value, "errors", "latest-error.json")),
            JsonSerializer.Serialize(failure, JsonOptions),
            cancellationToken);
    }

    private static FileReference StateFile(BookWorkspace workspace) =>
        new(Path.Combine(workspace.WorkingDirectory.Value, "state", "book-state.json"));

    private static int ReadFrameModeContractVersion(JsonElement root)
    {
        var property = root.EnumerateObject()
            .FirstOrDefault(item => string.Equals(item.Name, "frameModeContractVersion", StringComparison.OrdinalIgnoreCase));
        if (property.Value.ValueKind == JsonValueKind.Undefined) return 1;
        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var version))
        {
            throw new InvalidDataException("Frame mode contract version must be an integer.");
        }
        if (version != BookProcessingState.CurrentFrameModeContractVersion)
        {
            throw new InvalidDataException($"Frame mode contract version '{version}' is not supported.");
        }
        return version;
    }

    private static (
        IReadOnlyDictionary<string, FrameMode>? Overrides,
        IReadOnlyList<string>? ExplicitAutoKeys,
        IReadOnlyList<string>? ExplicitFrameKeys) ReadFrameOverrides(
        JsonElement root,
        bool legacy)
    {
        var property = root.EnumerateObject()
            .FirstOrDefault(item => string.Equals(item.Name, "interiorFrameOverrides", StringComparison.OrdinalIgnoreCase));
        if (property.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return (null, null, null);
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Interior frame overrides must be an object.");
        }

        var seen = new Dictionary<string, FrameMode>(StringComparer.OrdinalIgnoreCase);
        var enabled = new Dictionary<string, FrameMode>(StringComparer.OrdinalIgnoreCase);
        var explicitAutoKeys = new List<string>();
        var explicitFrameKeys = new List<string>();
        foreach (var entry in property.Value.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new InvalidDataException("Interior frame override source keys cannot be blank.");
            }

            var (mode, wasAuto) = ReadFrameMode(entry.Value, legacy);
            if (seen.TryGetValue(entry.Name, out var existing))
            {
                if (existing != mode)
                {
                    throw new InvalidDataException($"Interior frame override source key '{entry.Name}' conflicts with another key that differs only by case.");
                }
                continue;
            }

            seen.Add(entry.Name, mode);
            explicitFrameKeys.Add(entry.Name);
            if (mode == FrameMode.Enabled) enabled.Add(entry.Name, mode);
            if (wasAuto) explicitAutoKeys.Add(entry.Name);
        }

        return (
            enabled.Count == 0 ? null : enabled,
            explicitAutoKeys.Count == 0 ? null : explicitAutoKeys,
            explicitFrameKeys.Count == 0 ? null : explicitFrameKeys);
    }

    private static (FrameMode Mode, bool WasAuto) ReadFrameMode(JsonElement value, bool legacy)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (legacy)
            {
                if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase)) return (FrameMode.Disabled, true);
                if (string.Equals(text, "disabled", StringComparison.OrdinalIgnoreCase)) return (FrameMode.Disabled, false);
                if (string.Equals(text, "enabled", StringComparison.OrdinalIgnoreCase)) return (FrameMode.Enabled, false);
            }
            else
            {
                if (text == "disabled") return (FrameMode.Disabled, false);
                if (text == "enabled") return (FrameMode.Enabled, false);
            }
        }
        else if (legacy && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var numeric))
        {
            return numeric switch
            {
                0 => (FrameMode.Disabled, true),
                1 => (FrameMode.Enabled, false),
                2 => (FrameMode.Disabled, false),
                _ => throw new InvalidDataException($"Legacy frame mode integer '{numeric}' is not supported.")
            };
        }

        throw new InvalidDataException($"Frame mode token '{value.GetRawText()}' is not supported by contract v{(legacy ? 1 : 2)}.");
    }

    private static IReadOnlyDictionary<string, FrameMode>? NormalizeFrameOverrides(IReadOnlyDictionary<string, FrameMode>? source)
    {
        if (source is null) return null;
        var enabled = new Dictionary<string, FrameMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, mode) in source)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Interior frame override source keys cannot be blank.");
            if (!Enum.IsDefined(mode)) throw new InvalidDataException($"Frame mode '{mode}' is not supported.");
            if (mode == FrameMode.Enabled) enabled.Add(key, mode);
        }
        return enabled.Count == 0 ? null : enabled;
    }

    private static IReadOnlyList<BookProcessingLogEntry> ParseLogs(string content)
    {
        var reader = new Utf8JsonReader(
            Encoding.UTF8.GetBytes(content),
            new JsonReaderOptions { AllowMultipleValues = true });
        var entries = new List<BookProcessingLogEntry>();

        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.StartObject) continue;

            var entry = JsonSerializer.Deserialize<BookProcessingLogEntry>(ref reader, LogJsonOptions);
            if (entry is not null) entries.Add(entry);
        }

        return entries;
    }
}
