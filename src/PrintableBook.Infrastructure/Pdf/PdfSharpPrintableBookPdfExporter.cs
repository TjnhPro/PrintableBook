using System.Collections.Concurrent;
using System.IO.Compression;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PrintableBook.Core.Abstractions;
using PrintableBook.Core.Application.Processing;

namespace PrintableBook.Infrastructure.Pdf;

public sealed class PdfSharpPrintableBookPdfExporter : IPrintableBookPdfExporter
{
    public async ValueTask<PrintableBookPdfExportResult> ExportAsync(
        PrintableBookPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);

        var coverPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "cover.pdf"));
        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        WriteSingleRasterPdf(coverPdf, request.Cover, request.CoverPageSize, cancellationToken);
        await WriteInteriorPdfAsync(
            interiorPdf,
            request.IntroPages,
            request.OrderedInteriorPages,
            request.BackgroundPage,
            request.InteriorPageSize,
            request.MaximumPageConcurrency,
            cancellationToken);

        return new PrintableBookPdfExportResult(coverPdf, interiorPdf);
    }

    public async ValueTask<InteriorPdfExportResult> ExportInteriorAsync(
        InteriorPdfExportRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request, cancellationToken);
        Directory.CreateDirectory(request.TemporaryOutputDirectory.Value);

        var interiorPdf = new FileReference(Path.Combine(request.TemporaryOutputDirectory.Value, "interior.pdf"));
        await WriteInteriorPdfAsync(
            interiorPdf,
            request.IntroPages,
            request.OrderedInteriorPages,
            request.BackgroundPage,
            request.InteriorPageSize,
            request.MaximumPageConcurrency,
            cancellationToken);

        return new InteriorPdfExportResult(interiorPdf);
    }

    private static void Validate(PrintableBookPdfExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInteriorRequest(
            request.OrderedInteriorPages,
            request.InteriorPageSize,
            request.MaximumPageConcurrency);

        if (request.CoverPageSize.WidthInches <= 0 || request.CoverPageSize.HeightInches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "PDF page dimensions must be positive.");
        }
    }

    private static void Validate(InteriorPdfExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInteriorRequest(
            request.OrderedInteriorPages,
            request.InteriorPageSize,
            request.MaximumPageConcurrency);
    }

    private static void ValidateInteriorRequest(
        IReadOnlyList<FileReference> orderedInteriorPages,
        PhysicalPageSize interiorPageSize,
        int maximumPageConcurrency)
    {
        if (orderedInteriorPages.Count == 0)
        {
            throw new ArgumentException("At least one interior artwork page is required for PDF export.");
        }

        if (interiorPageSize.WidthInches <= 0 || interiorPageSize.HeightInches <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(interiorPageSize), "PDF page dimensions must be positive.");
        }

        if (maximumPageConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPageConcurrency), "Maximum page concurrency must be positive.");
        }
    }

    private static void WriteSingleRasterPdf(
        FileReference target,
        FileReference source,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        using var document = new PdfDocument();
        AddRasterPage(document, source, pageSize, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        document.Save(target.Value);
    }

    private static async ValueTask WriteInteriorPdfAsync(
        FileReference target,
        IReadOnlyList<FileReference> introPages,
        IReadOnlyList<FileReference> orderedInteriorPages,
        FileReference? backgroundPage,
        PhysicalPageSize pageSize,
        int maximumPageConcurrency,
        CancellationToken cancellationToken)
    {
        var importedInteriors = new ConcurrentDictionary<int, PdfDocument>();
        PdfDocument? introImport = null;
        using var semaphore = new SemaphoreSlim(maximumPageConcurrency, maximumPageConcurrency);
        using var remainingWorkCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task[] importTasks = [];

        try
        {
            if (introPages.Count > 0)
            {
                introImport = CreateImportDocument(
                    BuildIntroRasterPages(introPages, backgroundPage),
                    pageSize,
                    cancellationToken);
            }

            importTasks = orderedInteriorPages
                .Select((artwork, index) =>
                    Task.Run(
                        () => ImportInteriorAsync(
                            index,
                            artwork,
                            backgroundPage,
                            pageSize,
                            importedInteriors,
                            semaphore,
                            remainingWorkCancellation),
                        CancellationToken.None))
                .ToArray();

            try
            {
                await Task.WhenAll(importTasks);
            }
            catch
            {
                await remainingWorkCancellation.CancelAsync();

                try
                {
                    await Task.WhenAll(importTasks);
                }
                catch
                {
                    // Preserve the original worker failure.
                }

                throw;
            }

            using var finalDocument = new PdfDocument();
            if (introImport is not null)
            {
                finalDocument.Pages.InsertRange(finalDocument.Pages.Count, introImport);
                CloseAndDispose(introImport);
                introImport = null;
            }

            foreach (var index in importedInteriors.Keys.Order())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!importedInteriors.TryRemove(index, out var imported))
                {
                    throw new InvalidOperationException($"Interior import index {index} is missing.");
                }

                try
                {
                    finalDocument.Pages.InsertRange(finalDocument.Pages.Count, imported);
                }
                finally
                {
                    CloseAndDispose(imported);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            finalDocument.Save(target.Value);
        }
        finally
        {
            if (introImport is not null)
            {
                CloseAndDispose(introImport);
            }

            foreach (var pair in importedInteriors)
            {
                if (importedInteriors.TryRemove(pair.Key, out var imported))
                {
                    CloseAndDispose(imported);
                }
            }
        }
    }

    private static IReadOnlyList<FileReference> BuildIntroRasterPages(
        IReadOnlyList<FileReference> introPages,
        FileReference? backgroundPage)
    {
        var result = new List<FileReference>(introPages.Count * (backgroundPage is null ? 1 : 2));
        foreach (var intro in introPages)
        {
            result.Add(intro);
            if (backgroundPage is not null)
            {
                result.Add(backgroundPage);
            }
        }

        return result;
    }

    private static PdfDocument CreateImportDocument(
        IReadOnlyList<FileReference> rasterPages,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        using (var staging = new PdfDocument())
        {
            foreach (var source in rasterPages)
            {
                AddRasterPage(staging, source, pageSize, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            staging.Save(buffer, closeStream: false);
        }

        buffer.Position = 0;
        cancellationToken.ThrowIfCancellationRequested();
        return PdfReader.Open(buffer, PdfDocumentOpenMode.Import);
    }

    private static async Task ImportInteriorAsync(
        int index,
        FileReference artwork,
        FileReference? backgroundPage,
        PhysicalPageSize pageSize,
        ConcurrentDictionary<int, PdfDocument> importedInteriors,
        SemaphoreSlim semaphore,
        CancellationTokenSource remainingWorkCancellation)
    {
        var enteredSemaphore = false;
        PdfDocument? imported = null;

        try
        {
            await semaphore.WaitAsync(remainingWorkCancellation.Token);
            enteredSemaphore = true;
            var pages = backgroundPage is null ? [artwork] : new[] { artwork, backgroundPage };
            imported = CreateImportDocument(pages, pageSize, remainingWorkCancellation.Token);
            if (!importedInteriors.TryAdd(index, imported))
            {
                throw new InvalidOperationException($"Interior import index {index} already exists.");
            }

            imported = null;
        }
        catch
        {
            if (imported is not null)
            {
                CloseAndDispose(imported);
            }

            await remainingWorkCancellation.CancelAsync();
            throw;
        }
        finally
        {
            if (enteredSemaphore)
            {
                semaphore.Release();
            }
        }
    }

    private static void CloseAndDispose(PdfDocument document)
    {
        try
        {
            document.Close();
        }
        finally
        {
            document.Dispose();
        }
    }

    private static void AddRasterPage(
        PdfDocument document,
        FileReference source,
        PhysicalPageSize pageSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(pageSize.WidthInPoints);
        page.Height = XUnit.FromPoint(pageSize.HeightInPoints);

        var raster = File.ReadAllBytes(source.Value);

        try
        {
            DrawRasterPage(page, raster, pageSize);
        }
        catch (InvalidOperationException) when (TryExpandMonochromePng(raster, out var compatiblePng))
        {
            DrawRasterPage(page, compatiblePng, pageSize);
        }
    }

    private static void DrawRasterPage(PdfPage page, byte[] raster, PhysicalPageSize pageSize)
    {
        using var stream = new MemoryStream(raster, writable: false);
        using var image = XImage.FromStream(stream);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(image, 0, 0, pageSize.WidthInPoints, pageSize.HeightInPoints);
    }

    private static bool TryExpandMonochromePng(byte[] source, out byte[] compatiblePng)
    {
        compatiblePng = [];
        if (source.Length < 33 ||
            source[0] != 137 || source[1] != 80 || source[2] != 78 || source[3] != 71 ||
            source[4] != 13 || source[5] != 10 || source[6] != 26 || source[7] != 10)
        {
            return false;
        }

        var position = 8;
        ReadOnlySpan<byte> header = default;
        using var compressed = new MemoryStream();
        while (position + 12 <= source.Length)
        {
            var length = ReadUInt32(source, position);
            var chunkLength = checked((int)length);
            var dataStart = position + 8;
            var next = checked(dataStart + chunkLength + 4);
            if (next > source.Length)
            {
                return false;
            }

            var type = source.AsSpan(position + 4, 4);
            if (((ReadOnlySpan<byte>)type).SequenceEqual("IHDR"u8))
            {
                header = source.AsSpan(dataStart, chunkLength);
            }
            else if (((ReadOnlySpan<byte>)type).SequenceEqual("IDAT"u8))
            {
                compressed.Write(source, dataStart, chunkLength);
            }
            else if (((ReadOnlySpan<byte>)type).SequenceEqual("IEND"u8))
            {
                break;
            }

            position = next;
        }

        if (header.Length != 13 || header[8] != 1 || header[9] != 0 || header[10] != 0 || header[11] != 0 || header[12] != 0)
        {
            return false;
        }

        var width = checked((int)ReadUInt32(header, 0));
        var height = checked((int)ReadUInt32(header, 4));
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var packedRowLength = checked((width + 7) / 8);
        var decoded = new byte[checked(height * (packedRowLength + 1))];
        compressed.Position = 0;
        try
        {
            using var decompressor = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            ReadExactly(decompressor, decoded);
            if (decompressor.ReadByte() != -1)
            {
                return false;
            }
        }
        catch (InvalidDataException)
        {
            return false;
        }

        var previous = new byte[packedRowLength];
        var current = new byte[packedRowLength];
        using var rgb = new MemoryStream(checked(height * (width * 3 + 1)));
        for (var row = 0; row < height; row++)
        {
            var offset = row * (packedRowLength + 1);
            var filter = decoded[offset];
            Array.Copy(decoded, offset + 1, current, 0, packedRowLength);
            if (!Unfilter(current, previous, filter))
            {
                return false;
            }

            rgb.WriteByte(0);
            for (var column = 0; column < width; column++)
            {
                var value = (current[column / 8] & (1 << (7 - (column % 8)))) == 0 ? (byte)0 : byte.MaxValue;
                rgb.WriteByte(value);
                rgb.WriteByte(value);
                rgb.WriteByte(value);
            }

            (previous, current) = (current, previous);
        }

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> outputHeader = stackalloc byte[13];
        WriteUInt32(outputHeader, 0, (uint)width);
        WriteUInt32(outputHeader, 4, (uint)height);
        outputHeader[8] = 8;
        outputHeader[9] = 2;
        WriteChunk(output, "IHDR"u8, outputHeader);

        using var outputCompressed = new MemoryStream();
        rgb.Position = 0;
        using (var compressor = new ZLibStream(outputCompressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            rgb.CopyTo(compressor);
        }

        WriteChunk(output, "IDAT"u8, outputCompressed.ToArray());
        WriteChunk(output, "IEND"u8, []);
        compatiblePng = output.ToArray();
        return true;
    }

    private static bool Unfilter(byte[] current, byte[] previous, byte filter)
    {
        for (var index = 0; index < current.Length; index++)
        {
            var left = index == 0 ? 0 : current[index - 1];
            var above = previous[index];
            var upperLeft = index == 0 ? 0 : previous[index - 1];
            current[index] = filter switch
            {
                0 => current[index],
                1 => unchecked((byte)(current[index] + left)),
                2 => unchecked((byte)(current[index] + above)),
                3 => unchecked((byte)(current[index] + ((left + above) / 2))),
                4 => unchecked((byte)(current[index] + Paeth(left, above, upperLeft))),
                _ => 0
            };

            if (filter > 4)
            {
                return false;
            }
        }

        return true;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        var prediction = left + above - upperLeft;
        var leftDistance = Math.Abs(prediction - left);
        var aboveDistance = Math.Abs(prediction - above);
        var upperLeftDistance = Math.Abs(prediction - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

    private static void WriteUInt32(Span<byte> bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteUInt32(length, 0, (uint)data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);

        Span<byte> checksum = stackalloc byte[4];
        WriteUInt32(checksum, 0, CalculateCrc32(type, data));
        output.Write(checksum);
    }

    private static uint CalculateCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type)
        {
            crc = UpdateCrc32(crc, value);
        }

        foreach (var value in data)
        {
            crc = UpdateCrc32(crc, value);
        }

        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320;
        }

        return crc;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var bytesRead = stream.Read(buffer, read, buffer.Length - read);
            if (bytesRead == 0)
            {
                throw new InvalidDataException("The PNG image data ended unexpectedly.");
            }

            read += bytesRead;
        }
    }
}
