using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace ModShelf.Desktop.Views.Pictures;

/// <summary>
/// A saved picture, read and decoded off the UI thread at the width it's shown at, and kept decoded for a while, so rows
/// that scroll back show at once. A blurred one is decoded a few pixels wide, so it scales up soft and the picture itself
/// is never drawn.
/// </summary>
internal sealed class ModImage : Image
{
    public static readonly StyledProperty<string?> FileProperty = AvaloniaProperty.Register<ModImage, string?>(nameof(File));

    public static readonly StyledProperty<int> DecodeWidthProperty = AvaloniaProperty.Register<ModImage, int>(nameof(DecodeWidth), 160);

    public static readonly StyledProperty<bool> IsBlurredProperty = AvaloniaProperty.Register<ModImage, bool>(nameof(IsBlurred));

    /// <summary>
    /// How wide a blurred picture is decoded: few enough pixels that nothing in it can be made out.
    /// </summary>
    private const int BlurredWidth = 12;

    private static readonly DecodedPictures Decoded = new(capacity: 300);

    private int _loads;

    static ModImage()
    {
        FileProperty.Changed.AddClassHandler<ModImage>((image, _) => image.Loading = image.LoadAsync());
        DecodeWidthProperty.Changed.AddClassHandler<ModImage>((image, _) => image.Loading = image.LoadAsync());
        IsBlurredProperty.Changed.AddClassHandler<ModImage>((image, _) => image.Loading = image.LoadAsync());
    }

    /// <summary>
    /// The saved picture's file; null for none.
    /// </summary>
    public string? File
    {
        get => GetValue(FileProperty);
        set => SetValue(FileProperty, value);
    }

    /// <summary>
    /// The width to decode it at, in pixels: about twice the width it's shown at, for screens that scale.
    /// </summary>
    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    public bool IsBlurred
    {
        get => GetValue(IsBlurredProperty);
        set => SetValue(IsBlurredProperty, value);
    }

    /// <summary>
    /// The last picture asked for, done once this is; it never throws.
    /// </summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    protected override Type StyleKeyOverride => typeof(Image);

    /// <summary>
    /// Shows the picture, decoding it first unless it was decoded lately; a picture asked for later wins.
    /// </summary>
    private async Task LoadAsync()
    {
        int load = ++_loads;
        string? file = File;
        if (file is null)
        {
            Source = null;
            return;
        }

        (string, int) key = (file, IsBlurred ? BlurredWidth : Math.Max(1, DecodeWidth));
        if (Decoded.TryGet(key, out Bitmap? kept))
        {
            Source = kept;
            return;
        }

        Source = null;
        Bitmap? decoded = await Task.Run(() => DecodedPictures.Decode(key.Item1, key.Item2));
        if (decoded is not null)
        {
            Decoded.Add(key, decoded);
        }

        if (load == _loads)
        {
            Source = decoded;
        }
    }
}
