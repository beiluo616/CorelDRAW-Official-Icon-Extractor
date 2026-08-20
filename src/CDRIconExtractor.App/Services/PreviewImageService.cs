using System.Collections.Concurrent;
using System.Windows.Media.Imaging;
using System.IO;
using CDRIconExtractor.Core.Models;

namespace CDRIconExtractor.App.Services;

public sealed class PreviewImageService
{
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BitmapSource? Get(IconAsset? asset, int? preferredSize = null)
    {
        if (asset is null)
            return null;

        var bytes = asset.PngBytes;
        var width = asset.Width;
        var height = asset.Height;
        var sha = asset.Sha256;
        if (preferredSize is not null && asset.Variants.Count > 0)
        {
            var variant = asset.Variants
                .OrderBy(x => Math.Abs(x.Width - preferredSize.Value))
                .ThenBy(x => Math.Abs(x.Height - preferredSize.Value))
                .FirstOrDefault();
            if (variant is not null)
            {
                bytes = variant.PngBytes;
                width = variant.Width;
                height = variant.Height;
                sha = variant.Sha256;
            }
        }

        if (bytes.Length == 0)
            return null;
        var key = $"{sha}|{width}x{height}";
        return _cache.GetOrAdd(key, _ => Decode(bytes));
    }

    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.DecodePixelWidth = 128;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
