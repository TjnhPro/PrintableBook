using PrintableBook.Core.Domain.Errors;

namespace PrintableBook.Core.Application.Results;

public sealed record PrintableBookResult(bool IsSuccess, PrintableBookError? Error = null);
