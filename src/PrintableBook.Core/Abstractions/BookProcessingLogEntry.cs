namespace PrintableBook.Core.Abstractions;

public sealed record BookProcessingLogEntry(
    DateTimeOffset Timestamp,
    string Event,
    string? Step = null,
    string? Detail = null);
