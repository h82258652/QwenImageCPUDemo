using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageCpuDemo;

internal static class InputImage
{
    internal const long MaxFileBytes = 100 * 1024 * 1024;
    internal const long MaxPixels = 64_000_000;

    internal static Bitmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length == 0 || stream.Length > MaxFileBytes)
            throw new InvalidDataException("Input images must be nonempty and no larger than 100 MiB.");
        try
        {
            using var image = Image.FromStream(stream, useEmbeddedColorManagement: true, validateImageData: true);
            if (image.RawFormat.Guid != ImageFormat.Png.Guid && image.RawFormat.Guid != ImageFormat.Jpeg.Guid &&
                image.RawFormat.Guid != ImageFormat.Bmp.Guid)
                throw new InvalidDataException("Only PNG, JPEG and BMP input images are supported.");
            if ((long)image.Width * image.Height > MaxPixels)
                throw new InvalidDataException("Input images must contain no more than 64 million pixels.");
            if (image.PropertyIdList.Contains(0x0112))
            {
                var value = image.GetPropertyItem(0x0112)?.Value;
                var orientation = value is { Length: >= 2 } ? BitConverter.ToUInt16(value) : 1;
                image.RotateFlip(orientation switch
                {
                    2 => RotateFlipType.RotateNoneFlipX,
                    3 => RotateFlipType.Rotate180FlipNone,
                    4 => RotateFlipType.Rotate180FlipX,
                    5 => RotateFlipType.Rotate90FlipX,
                    6 => RotateFlipType.Rotate90FlipNone,
                    7 => RotateFlipType.Rotate270FlipX,
                    8 => RotateFlipType.Rotate270FlipNone,
                    _ => RotateFlipType.RotateNoneFlipNone
                });
            }
            return new Bitmap(image);
        }
        catch (Exception exception) when (exception is ArgumentException or ExternalException or OutOfMemoryException)
        {
            throw new InvalidDataException("Could not decode the image. Use a valid PNG/JPEG/BMP with enough free memory.", exception);
        }
    }

    internal static void Prepare(string source, string destination, int width, int height)
    {
        using var image = Load(source);
        using var fitted = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(fitted))
        {
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            var scale = Math.Min((double)width / image.Width, (double)height / image.Height);
            var scaledWidth = Math.Clamp((int)Math.Round(image.Width * scale), 1, width);
            var scaledHeight = Math.Clamp((int)Math.Round(image.Height * scale), 1, height);
            var target = new Rectangle((width - scaledWidth) / 2, (height - scaledHeight) / 2, scaledWidth, scaledHeight);
            using var attributes = new ImageAttributes();
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            graphics.DrawImage(image, target, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        }
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        fitted.Save(output, ImageFormat.Png);
    }
}
