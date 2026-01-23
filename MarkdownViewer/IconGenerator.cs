using SkiaSharp;

namespace MarkdownViewer;

/// <summary>
/// Generates application and file type icons programmatically.
/// Run this once to generate the .ico files, then include them in the project.
/// </summary>
public static class IconGenerator
{
    public static void GenerateIcons()
    {
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets");
        Directory.CreateDirectory(assetsPath);

        // Generate app icon (lV on dark background)
        GenerateAppIcon(Path.Combine(assetsPath, "app-icon.ico"));

        // Generate file type icon (document with lV badge)
        GenerateFileIcon(Path.Combine(assetsPath, "markdown-file.ico"));

        Console.WriteLine("Icons generated in Assets folder");
    }

    private static void GenerateAppIcon(string outputPath)
    {
        var sizes = new[] { 16, 32, 48, 256 };
        var images = new List<SKBitmap>();

        foreach (var size in sizes)
        {
            using var surface = SKSurface.Create(new SKImageInfo(size, size));
            var canvas = surface.Canvas;

            // Dark background with rounded corners
            var cornerRadius = size / 8f;
            using var bgPaint = new SKPaint { Color = new SKColor(26, 26, 46), IsAntialias = true };
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(0, 0, size, size), cornerRadius), bgPaint);

            // Calculate font size proportionally
            var fontSize = size * 0.5625f; // 36/64 ratio

            // Draw "l" in gray (italic approximation via skew)
            using var lucidTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.BoldItalic);
            using var lucidFont = new SKFont(lucidTypeface, fontSize);
            using var lucidPaint = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD), IsAntialias = true };

            canvas.Save();
            canvas.Skew(-0.15f, 0);
            canvas.DrawText("l", size * 0.22f, size * 0.72f, lucidFont, lucidPaint);
            canvas.Restore();

            // Draw "V" in white (bold)
            using var viewTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
            using var viewFont = new SKFont(viewTypeface, fontSize);
            using var viewPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("V", size * 0.47f, size * 0.72f, viewFont, viewPaint);

            // Convert to bitmap
            using var image = surface.Snapshot();
            var bitmap = SKBitmap.FromImage(image);
            images.Add(bitmap);
        }

        // Write ICO file
        WriteIcoFile(outputPath, images);

        foreach (var img in images) img.Dispose();
    }

    private static void GenerateFileIcon(string outputPath)
    {
        var sizes = new[] { 16, 32, 48, 256 };
        var images = new List<SKBitmap>();

        foreach (var size in sizes)
        {
            using var surface = SKSurface.Create(new SKImageInfo(size, size));
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            // Document proportions
            var docWidth = size * 0.75f;
            var docHeight = size * 0.9f;
            var docLeft = (size - docWidth) / 2;
            var docTop = (size - docHeight) / 2;
            var foldSize = size * 0.2f;

            // Document body (white with slight gray tint)
            using var docPaint = new SKPaint { Color = new SKColor(250, 250, 252), IsAntialias = true };
            using var docPath = new SKPath();
            docPath.MoveTo(docLeft, docTop);
            docPath.LineTo(docLeft + docWidth - foldSize, docTop);
            docPath.LineTo(docLeft + docWidth, docTop + foldSize);
            docPath.LineTo(docLeft + docWidth, docTop + docHeight);
            docPath.LineTo(docLeft, docTop + docHeight);
            docPath.Close();
            canvas.DrawPath(docPath, docPaint);

            // Document border
            using var borderPaint = new SKPaint
            {
                Color = new SKColor(180, 180, 190),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = Math.Max(1, size / 32f)
            };
            canvas.DrawPath(docPath, borderPaint);

            // Fold corner
            using var foldPath = new SKPath();
            foldPath.MoveTo(docLeft + docWidth - foldSize, docTop);
            foldPath.LineTo(docLeft + docWidth - foldSize, docTop + foldSize);
            foldPath.LineTo(docLeft + docWidth, docTop + foldSize);
            using var foldPaint = new SKPaint { Color = new SKColor(220, 220, 225), IsAntialias = true };
            canvas.DrawPath(foldPath, foldPaint);
            canvas.DrawPath(foldPath, borderPaint);

            // Markdown lines (horizontal lines to suggest text)
            using var linePaint = new SKPaint
            {
                Color = new SKColor(200, 200, 210),
                IsAntialias = true,
                StrokeWidth = Math.Max(1, size / 24f),
                StrokeCap = SKStrokeCap.Round
            };
            var lineStartX = docLeft + size * 0.08f;
            var lineEndX = docLeft + docWidth - size * 0.08f;
            var lineY = docTop + size * 0.35f;
            var lineSpacing = size * 0.12f;

            for (int i = 0; i < 3 && lineY < docTop + docHeight - size * 0.25f; i++)
            {
                var endX = i == 2 ? lineEndX - size * 0.15f : lineEndX; // Last line shorter
                canvas.DrawLine(lineStartX, lineY, endX, lineY, linePaint);
                lineY += lineSpacing;
            }

            // lV badge in bottom-right corner
            var badgeSize = size * 0.45f;
            var badgeX = docLeft + docWidth - badgeSize * 0.6f;
            var badgeY = docTop + docHeight - badgeSize * 0.6f;

            // Badge background (dark with rounded corners)
            using var badgeBgPaint = new SKPaint { Color = new SKColor(26, 26, 46), IsAntialias = true };
            var badgeRadius = badgeSize / 6f;
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(badgeX, badgeY, badgeX + badgeSize, badgeY + badgeSize), badgeRadius), badgeBgPaint);

            // Badge text
            var badgeFontSize = badgeSize * 0.5f;

            using var lucidTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.BoldItalic);
            using var lucidFont = new SKFont(lucidTypeface, badgeFontSize);
            using var lucidPaint = new SKPaint { Color = new SKColor(0xDD, 0xDD, 0xDD), IsAntialias = true };

            canvas.Save();
            canvas.Skew(-0.12f, 0);
            canvas.DrawText("l", badgeX + badgeSize * 0.18f, badgeY + badgeSize * 0.7f, lucidFont, lucidPaint);
            canvas.Restore();

            using var viewTypeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold);
            using var viewFont = new SKFont(viewTypeface, badgeFontSize);
            using var viewPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.DrawText("V", badgeX + badgeSize * 0.45f, badgeY + badgeSize * 0.7f, viewFont, viewPaint);

            // Convert to bitmap
            using var image = surface.Snapshot();
            var bitmap = SKBitmap.FromImage(image);
            images.Add(bitmap);
        }

        // Write ICO file
        WriteIcoFile(outputPath, images);

        foreach (var img in images) img.Dispose();
    }

    private static void WriteIcoFile(string path, List<SKBitmap> images)
    {
        using var fs = File.Create(path);
        using var writer = new BinaryWriter(fs);

        // ICO header
        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)1); // Type: 1 = ICO
        writer.Write((ushort)images.Count); // Number of images

        // Calculate offsets
        var headerSize = 6 + (images.Count * 16); // Main header + image entries
        var currentOffset = headerSize;
        var imageData = new List<byte[]>();

        foreach (var bitmap in images)
        {
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            var data = encoded.ToArray();
            imageData.Add(data);
        }

        // Write image directory entries
        for (int i = 0; i < images.Count; i++)
        {
            var bitmap = images[i];
            var data = imageData[i];

            writer.Write((byte)(bitmap.Width >= 256 ? 0 : bitmap.Width)); // Width (0 = 256)
            writer.Write((byte)(bitmap.Height >= 256 ? 0 : bitmap.Height)); // Height (0 = 256)
            writer.Write((byte)0); // Color palette
            writer.Write((byte)0); // Reserved
            writer.Write((ushort)1); // Color planes
            writer.Write((ushort)32); // Bits per pixel
            writer.Write((uint)data.Length); // Size of image data
            writer.Write((uint)currentOffset); // Offset to image data

            currentOffset += data.Length;
        }

        // Write image data
        foreach (var data in imageData)
        {
            writer.Write(data);
        }
    }
}
