namespace PrintableBook.Desktop.Updates;

public interface IDesktopUpdateCoordinator
{
    DesktopUpdateSnapshot GetState();
    ValueTask<DesktopUpdateSnapshot> CheckAsync(UpdateCheckTrigger trigger, CancellationToken cancellationToken = default);
    ValueTask<DesktopUpdateSnapshot> DownloadAsync(CancellationToken cancellationToken = default);
    ValueTask<DesktopUpdateSnapshot> InstallAsync(CancellationToken cancellationToken = default);
}
