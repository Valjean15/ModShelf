using Avalonia.Media.Imaging;

namespace ModShelf.Desktop.Views.Pictures;

/// <summary>
/// Pictures decoded lately, by file and width, the least recently used dropped first. Used on the UI thread only.
/// </summary>
/// <remarks>
/// A picture dropped isn't disposed, since an image may still show it; it goes once nothing does.
/// </remarks>
internal sealed class DecodedPictures(int capacity)
{
    private readonly Dictionary<(string File, int Width), LinkedListNode<((string, int) Key, Bitmap Bitmap)>> _byKey = [];
    private readonly LinkedList<((string, int) Key, Bitmap Bitmap)> _byUse = new();

    public bool TryGet((string File, int Width) key, out Bitmap? bitmap)
    {
        if (_byKey.TryGetValue(key, out LinkedListNode<((string, int) Key, Bitmap Bitmap)>? node))
        {
            _byUse.Remove(node);
            _byUse.AddFirst(node);
            bitmap = node.Value.Bitmap;
            return true;
        }

        bitmap = null;
        return false;
    }

    public void Add((string File, int Width) key, Bitmap bitmap)
    {
        if (_byKey.Remove(key, out LinkedListNode<((string, int) Key, Bitmap Bitmap)>? old))
        {
            _byUse.Remove(old);
        }

        _byKey[key] = _byUse.AddFirst((key, bitmap));
        while (_byKey.Count > capacity && _byUse.Last is { } oldest)
        {
            _byUse.RemoveLast();
            _byKey.Remove(oldest.Value.Key);
        }
    }

    /// <returns>The picture, at most <paramref name="width"/> pixels wide; null when it's gone or isn't a picture.</returns>
    public static Bitmap? Decode(string file, int width)
    {
        try
        {
            using FileStream stream = File.OpenRead(file);
            return Bitmap.DecodeToWidth(stream, width, BitmapInterpolationMode.HighQuality);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            // Cleared meanwhile, or not a picture after all: the image shows nothing, as for no picture.
            return null;
        }
    }
}
