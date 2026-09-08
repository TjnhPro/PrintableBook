using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Desktop.Updates;

public interface IUpdateInstallHandoff
{
    ValueTask<UpdateInstallHandoffOutcome> BeginAsync(PreparedUpdate preparedUpdate, CancellationToken cancellationToken = default);
}
