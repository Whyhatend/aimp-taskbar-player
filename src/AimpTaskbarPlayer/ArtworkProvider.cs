using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace AimpTaskbarPlayer;

/// <summary>Loads cover art for the current file: embedded picture first, then common folder images.</summary>
public sealed class ArtworkProvider
{
    private static readonly string[] FolderCandidates =
    {
        "cover.jpg", "folder.jpg", "front.jpg", "album.jpg", "artwork.jpg", "cover.png", "folder.png", "front.png",
    };

    private readonly Dictionary<string, (DateTime Stamp, BitmapSource? Image)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public async Task<BitmapSource?> GetAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(path); }
        catch { return null; }

        lock (_gate)
        {
            if (_cache.TryGetValue(path, out var hit) && hit.Stamp == stamp) return hit.Image;
        }

        var image = await Task.Run(() => Load(path)).ConfigureAwait(true);

        lock (_gate)
        {
            if (_cache.Count >= 8) _cache.Clear();
            _cache[path] = (stamp, image);
        }
        return image;
    }

    /// <summary>Drops cached covers; they are cheap to reload and the OS file cache still helps.</summary>
    public void Clear()
    {
        lock (_gate) _cache.Clear();
    }

    private static BitmapSource? Load(string path)
    {
        var embedded = TryEmbedded(path);
        if (embedded != null) return embedded;

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return null;

            foreach (var name in FolderCandidates)
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate))
                {
                    var img = DecodeFile(candidate);
                    if (img != null) return img;
                }
            }

            foreach (var candidate in Directory.EnumerateFiles(dir, "*.*").Take(200)
                         .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                                  || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            {
                var img = DecodeFile(candidate);
                if (img != null) return img;
            }
        }
        catch
        {
            // ignored - artwork is optional
        }

        return null;
    }

    private static BitmapSource? TryEmbedded(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var picture = file.Tag.Pictures?.FirstOrDefault();
            if (picture?.Data?.Data is { Length: > 0 } bytes) return Decode(bytes);
        }
        catch
        {
            // unsupported / broken file
        }
        return null;
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return Decode(stream);
    }

    private static BitmapSource? DecodeFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Decode(stream);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? Decode(Stream stream)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            // The panel shows the cover at 34 DIP; 96 px is plenty even at 200% scaling.
            image.DecodePixelWidth = 96;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }
}
