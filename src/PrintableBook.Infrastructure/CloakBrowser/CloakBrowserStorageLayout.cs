namespace PrintableBook.Infrastructure.CloakBrowser;

public sealed class CloakBrowserStorageLayout
{
    public CloakBrowserStorageLayout(string applicationRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationRoot);
        Root = Path.Combine(Path.GetFullPath(applicationRoot), ".cloakbrowser");
        Profile = Path.Combine(Root, "profile-v1");
        Cache = Path.Combine(Root, "cache");
    }

    public string Root { get; }
    public string Profile { get; }
    public string Cache { get; }

    public void EnsureWritable()
    {
        Directory.CreateDirectory(Profile);
        Directory.CreateDirectory(Cache);
        var probe = Path.Combine(Root, $".write-{Guid.NewGuid():N}");
        try
        {
            using (File.Create(probe)) { }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            throw new AmazonCrawlStorageException("browser_storage_not_writable", "The app cannot write the CloakBrowser profile and cache folders.", exception);
        }
        finally
        {
            try { File.Delete(probe); }
            catch (Exception) { }
        }
    }
}

public sealed class AmazonCrawlStorageException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = code;
}
