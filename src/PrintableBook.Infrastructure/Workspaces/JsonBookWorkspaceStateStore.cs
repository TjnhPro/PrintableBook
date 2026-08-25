using System.Text.Json;
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
    {
        var stateFile = StateFile(workspace);
        if (!await fileSystem.FileExistsAsync(stateFile, cancellationToken))
        {
            return null;
        }

        var content = await fileSystem.ReadTextAsync(stateFile, cancellationToken);
        var state = JsonSerializer.Deserialize<BookProcessingState>(content, JsonOptions)
            ?? throw new InvalidDataException("The workspace state file is empty or invalid.");
        return state with
        {
            InteriorFrameOverrides = state.InteriorFrameOverrides is null
                ? null
                : new Dictionary<string, FrameMode>(state.InteriorFrameOverrides, StringComparer.OrdinalIgnoreCase),
            InactiveInteriorSourceKeys = state.InactiveInteriorSourceKeys?
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray() is { Length: > 0 } inactive ? inactive : null
        };
    }

    public ValueTask SaveAsync(BookWorkspace workspace, BookProcessingState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        return fileSystem.WriteTextAtomicallyAsync(StateFile(workspace), JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
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
