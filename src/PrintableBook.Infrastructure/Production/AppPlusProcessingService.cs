using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Application.S3Storage;
namespace PrintableBook.Infrastructure.Production;

public sealed class AppPlusProcessingService(IBookOutputLeaseCoordinator leases) : IAppPlusProcessingService
{
    public async ValueTask ProcessAsync(BookWorkspace workspace, DirectoryReference output, string split, CancellationToken cancellationToken = default)
    {
        var ys = AppPlusSplit.Parse(split);
        using var source = new MagickImage(ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.AppPlus).Value);
        if (source.Format != MagickFormat.Png || ys[3] >= source.Height) throw new ArgumentException("The last App Plus Y position must be below image height.");
        var staging = Path.Combine(workspace.WorkingDirectory.Value, "app-plus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            for (var i = 0; i < 4; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var image = source.Clone();
                var bottom = i == 3 ? checked((int)source.Height) : ys[i + 1];
                image.Crop(new MagickGeometry(0, ys[i], source.Width, (uint)(bottom - ys[i])));
                image.ResetPage();
                image.Write(Path.Combine(staging, $"app_plus_{i + 1}.png"), MagickFormat.Png);
            }
            await using var lease = await leases.TryAcquireAsync(output.Value, cancellationToken)
                ?? throw new IOException("Book output is busy. Retry shortly.");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(output.Value);
            var replaced = new List<(string Target, string Backup, bool Existed)>();
            try
            {
                for (var i = 1; i <= 4; i++)
                {
                    var name = $"app_plus_{i}.png";
                    var target = Path.Combine(output.Value, name);
                    var backup = Path.Combine(staging, name + ".backup");
                    var existed = File.Exists(target);
                    if (existed) File.Copy(target, backup);
                    File.Move(Path.Combine(staging, name), target, true);
                    replaced.Add((target, backup, existed));
                }
            }
            catch
            {
                foreach (var item in replaced.AsEnumerable().Reverse())
                    if (item.Existed) File.Copy(item.Backup, item.Target, true); else File.Delete(item.Target);
                throw;
            }
        }
        finally { Directory.Delete(staging, true); }
    }
}
