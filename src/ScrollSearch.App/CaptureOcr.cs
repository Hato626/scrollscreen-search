using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using ScrollSearch.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ScrollSearch.App;

internal sealed record Capture(byte[] Png, string Hash, int Width, int Height, byte[] Thumbnail)
{
    public double Difference(Capture other) => Thumbnail.Zip(other.Thumbnail).Count(p => Math.Abs(p.First - p.Second) > 22) / (double)Thumbnail.Length;
}
internal sealed record Observation(Capture Capture, Frame Frame, Box ScreenRegion, bool Unchanged = false);

internal static class ScreenCapture
{
    public static Capture Take(Box region)
    {
        var width = (int)region.Width; var height = (int)region.Height;
        if (width < 24 || height < 24 || (long)width * height > 32_000_000)
            throw new InvalidOperationException("识别区域必须至少为 24 × 24 像素，且不超过 3200 万像素。 ");
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen((int)region.X, (int)region.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var png = stream.ToArray();
        using var small = new Bitmap(bitmap, new Size(96, 64));
        var thumbnail = new byte[96 * 64];
        for (var y = 0; y < 64; y++) for (var x = 0; x < 96; x++)
        { var c = small.GetPixel(x, y); thumbnail[y * 96 + x] = (byte)((c.R * 3 + c.G * 6 + c.B) / 10); }
        return new(png, Convert.ToHexString(SHA256.HashData(png)), width, height, thumbnail);
    }
}

internal sealed class LocalOcr
{
    readonly OcrEngine engine;
    public static IReadOnlyList<Language> Languages => OcrEngine.AvailableRecognizerLanguages.ToArray();
    public LocalOcr(string language) => engine = OcrEngine.TryCreateFromLanguage(new Language(language))
        ?? throw new InvalidOperationException("该 OCR 语言未安装。请到 Windows 设置 → 时间和语言 → 语言和区域安装所需语言的 OCR 功能。 ");
    public async Task<Frame> Recognize(Capture capture, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(capture.Png);
            await writer.StoreAsync().AsTask(token);
        }
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        var scale = Math.Min(1, (double)OcrEngine.MaxImageDimension / Math.Max(capture.Width, capture.Height));
        var transform = new BitmapTransform { ScaledWidth = (uint)Math.Max(1, capture.Width * scale), ScaledHeight = (uint)Math.Max(1, capture.Height * scale) };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token);
        var recognized = await engine.RecognizeAsync(bitmap).AsTask(token);
        token.ThrowIfCancellationRequested();
        var sx = (double)capture.Width / bitmap.PixelWidth; var sy = (double)capture.Height / bitmap.PixelHeight;
        var lines = recognized.Lines.Select(line =>
        {
            var words = line.Words.Select(w => new Word(w.Text, new(w.BoundingRect.X * sx, w.BoundingRect.Y * sy,
                w.BoundingRect.Width * sx, w.BoundingRect.Height * sy))).ToArray();
            return new TextLine(line.Text, Box.Union(words.Select(w => w.Bounds)), words);
        }).ToArray();
        return new(lines, capture.Width, capture.Height);
    }
}
