namespace PrintableBook.Core.Application.Updates;

public interface IApplicationVersionProvider
{
    Version CurrentVersion { get; }
}
