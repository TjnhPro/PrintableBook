using PrintableBook.Core.Application.Updates;

namespace PrintableBook.Infrastructure.Updates;

public sealed class HttpUpdateAssetDownloader(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "PrintableBook.UpdateDownload";

    public async ValueTask DownloadAsync(
        UpdateAssetInfo asset,
        string destinationPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(asset);

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Download destination path is required.", nameof(destinationPath));
        }

        if (asset.SizeBytes <= 0)
        {
            throw new ArgumentException("Update asset size must be positive.", nameof(asset));
        }

        if (!asset.DownloadUri.IsAbsoluteUri)
        {
            throw new ArgumentException("Update asset download URI must be absolute.", nameof(asset));
        }

        var partialPath = $"{destinationPath}.partial";
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException("Download destination has no parent directory.");

        try
        {
            Directory.CreateDirectory(destinationDirectory);
            File.Delete(partialPath);

            using var httpClient = httpClientFactory.CreateClient(HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUri);
            request.Headers.UserAgent.ParseAdd("PrintableBook-AutoUpdate");
            request.Headers.Accept.ParseAdd("application/octet-stream");

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[81920];
            long received = 0;
            await using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = await source.ReadAsync(buffer, cancellationToken);
                    if (count == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    received += count;
                    progress?.Report(received);
                }

                await destination.FlushAsync(cancellationToken);
            }

            if (received != asset.SizeBytes)
            {
                throw new InvalidDataException(
                    $"Downloaded asset size {received} does not match expected {asset.SizeBytes}.");
            }

            File.Move(partialPath, destinationPath, overwrite: true);
        }
        catch
        {
            File.Delete(partialPath);
            throw;
        }
    }
}
