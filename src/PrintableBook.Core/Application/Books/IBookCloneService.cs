using PrintableBook.Core.Application.Brands;
using PrintableBook.Core.Application.Discovery;

namespace PrintableBook.Core.Application.Books;

public sealed record BookCloneResult(
    DiscoveredBook SourceBook,
    SupportedLanguageOption Language,
    DiscoveredBook DestinationBook);

public sealed class BookCloneException : Exception
{
    public BookCloneException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

public interface IBookCloneService
{
    ValueTask<BookCloneResult> CloneAsync(
        ApplicationPaths paths,
        DiscoveredBook sourceBook,
        SupportedLanguageOption language,
        CancellationToken cancellationToken = default);
}
