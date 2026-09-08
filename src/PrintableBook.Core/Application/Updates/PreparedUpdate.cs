namespace PrintableBook.Core.Application.Updates;

public sealed record PreparedUpdate(
    Version Version,
    string PayloadDirectoryPath);
