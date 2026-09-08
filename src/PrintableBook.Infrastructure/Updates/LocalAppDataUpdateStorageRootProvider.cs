namespace PrintableBook.Infrastructure.Updates;

public sealed class LocalAppDataUpdateStorageRootProvider : IUpdateStorageRootProvider
{
    public string RootPath
    {
        get
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException(
                    "Local application data directory is unavailable.");
            }

            return Path.Combine(localAppData, "PrintableBook", "Updates");
        }
    }
}
