using System.Globalization;
using CDRIconExtractor.Core.Models;
using CDRIconExtractor.Core.Parsing;

namespace CDRIconExtractor.Windows.Resources;

public sealed class CrlIconsReader
{
    public async Task<IReadOnlyList<IconAsset>> ReadPngAssetsAsync(string crlIconsPath, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(crlIconsPath);
        token.ThrowIfCancellationRequested();

        var options = new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        };

        await using var stream = new FileStream(crlIconsPath, options);
        if (stream.Length > int.MaxValue)
            throw new IOException("CrlIcons.dll is too large to scan safely in memory.");

        var bytes = new byte[checked((int)stream.Length)];
        var read = 0;
        while (read < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(bytes.AsMemory(read), token).ConfigureAwait(false);
            if (count == 0)
                break;
            read += count;
        }

        if (read != bytes.Length)
            Array.Resize(ref bytes, read);

        var slices = PngStreamScanner.Find(bytes);
        var result = new List<IconAsset>(slices.Count);
        for (var index = 0; index < slices.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var slice = slices[index];
            var pngBytes = bytes.AsSpan(slice.Offset, slice.Length).ToArray();
            result.Add(new IconAsset(
                crlIconsPath,
                "CrlIconsPng",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                slice.Width,
                slice.Height,
                slice.Sha256,
                pngBytes));
        }

        return result;
    }
}
