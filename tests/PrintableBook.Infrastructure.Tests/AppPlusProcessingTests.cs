using ImageMagick;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Domain.Books;
using PrintableBook.Core.Application.Production;
using PrintableBook.Core.Application.S3Storage;
using PrintableBook.Infrastructure.Production;
namespace PrintableBook.Infrastructure.Tests;
public sealed class AppPlusProcessingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Crops_exact_intervals_and_replaces_previous_outputs(int start)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var workspace = new BookWorkspace(new BookId("test"), new(root), new(root), new(root));
        Directory.CreateDirectory(ProductionWorkspacePaths.ProductionDirectory(workspace).Value);
        try
        {
            using var source = new MagickImage(MagickColors.Transparent, 5, 12);
            using (var pixels = source.GetPixels())
                for (var y = 0; y < 12; y++) for (var x = 0; x < 5; x++) pixels.SetPixel(x, y, new byte[] { (byte)(y * 10), 0, 0, 128 });
            var file = ProductionWorkspacePaths.SourceFile(workspace, ProductionAssetKind.AppPlus);
            source.Write(file.Value, MagickFormat.Png);
            var output = new DirectoryReference(Path.Combine(root, "Output"));
            var service = new AppPlusProcessingService(new BookOutputLeaseCoordinator());
            await service.ProcessAsync(workspace, output, $"{start}|4|7|10");
            var ys = new[] { start, 4, 7, 10, 12 };
            using var decoded = new MagickImage(file.Value);
            for (var i = 0; i < 4; i++)
            {
                using var crop = new MagickImage(Path.Combine(output.Value, $"app_plus_{i + 1}.png"));
                Assert.Equal(5u, crop.Width);
                Assert.Equal((uint)(ys[i + 1] - ys[i]), crop.Height);
                using var actual = crop.GetPixels(); using var expected = decoded.GetPixels();
                Assert.Equal(expected.GetPixel(0, ys[i]).ToColor(), actual.GetPixel(0, 0).ToColor());
                Assert.True(crop.HasAlpha);
            }
            var previous = File.ReadAllBytes(Path.Combine(output.Value, "app_plus_1.png"));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ProcessAsync(workspace, output, "0|4|7|12").AsTask());
            Assert.Equal(previous, File.ReadAllBytes(Path.Combine(output.Value, "app_plus_1.png")));
            await service.ProcessAsync(workspace, output, "1|3|5|9");
            var saved = File.ReadAllBytes(Path.Combine(output.Value, "app_plus_1.png"));
            File.Delete(Path.Combine(output.Value, "app_plus_4.png"));
            Directory.CreateDirectory(Path.Combine(output.Value, "app_plus_4.png"));
            var failure = await Record.ExceptionAsync(() => service.ProcessAsync(workspace, output, "0|4|7|10").AsTask());
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Assert.Equal(saved, File.ReadAllBytes(Path.Combine(output.Value, "app_plus_1.png")));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ProcessAsync(workspace, output, "0|4|7|10", cancelled.Token).AsTask());
            Assert.Equal(saved, File.ReadAllBytes(Path.Combine(output.Value, "app_plus_1.png")));
            using var replaced = new MagickImage(Path.Combine(output.Value, "app_plus_1.png"));
            Assert.Equal(2u, replaced.Height);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("0|1|2")] [InlineData("0|1|1|3")]
    [InlineData("-1|1|2|3")] [InlineData("0|1.5|2|3")] [InlineData("0|1|2|10001")]
    public void Rejects_invalid_settings(string? value) => Assert.Throws<ArgumentException>(() => AppPlusSplit.Parse(value));
}
