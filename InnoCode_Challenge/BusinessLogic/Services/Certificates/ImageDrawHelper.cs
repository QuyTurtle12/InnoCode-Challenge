using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;
using System.Linq;

namespace BusinessLogic.Services.Certificates
{
    internal static class ImageDrawHelper
    {
        private static readonly FontCollection BundledFonts = new FontCollection();
        private static bool _bundledFontLoaded;
        private static FontFamily? _bundledFontFamily;

        public static byte[] RenderTextOnImage(
            Stream template,
            string displayText,
            string fontFamily,
            float fontSize,
            string colorHex,
            int x, int y,
            int maxWidth,
            string align = "left")
        {
            template.Position = 0;
            using var img = Image.Load<Rgba32>(template);

            Font font = ResolveFont(fontFamily, fontSize);

            var options = new TextOptions(font)
            {
                WrappingLength = maxWidth > 0 ? maxWidth : 0,
                HorizontalAlignment =
                    align?.Equals("center", StringComparison.OrdinalIgnoreCase) == true ? HorizontalAlignment.Center :
                    align?.Equals("right", StringComparison.OrdinalIgnoreCase) == true ? HorizontalAlignment.Right :
                                                                                    HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };

            var text = displayText ?? string.Empty;

            var size = TextMeasurer.MeasureSize(text, options);

            if (maxWidth > 0 && size.Width > maxWidth)
            {
                float scale = maxWidth / size.Width;
                float newSize = Math.Max(6, font.Size * scale);
                font = new Font(font, newSize);

                options = new TextOptions(font)
                {
                    WrappingLength = maxWidth,
                    HorizontalAlignment = options.HorizontalAlignment,
                    VerticalAlignment = VerticalAlignment.Top
                };
                size = TextMeasurer.MeasureSize(text, options);
            }

            float originX = x;
            if (options.HorizontalAlignment == HorizontalAlignment.Center)
                originX = x - size.Width / 2f;
            else if (options.HorizontalAlignment == HorizontalAlignment.Right)
                originX = x - size.Width;

            var color = ParseColor(colorHex);

            img.Mutate(ctx => ctx.DrawText(text, font, color, new PointF(originX, y)));

            using var output = new MemoryStream();
            img.Save(output, new PngEncoder());
            return output.ToArray();
        }

        private static Font ResolveFont(string fontFamily, float fontSize)
        {
            if (!string.IsNullOrWhiteSpace(fontFamily) && SystemFonts.TryGet(fontFamily, out var family))
                return family.CreateFont(fontSize, FontStyle.Regular);

            var bundledFamily = TryGetBundledFont();
            if (bundledFamily != null)
                return bundledFamily.Value.CreateFont(fontSize, FontStyle.Regular);

            var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
            if (fallbackFamily != null)
                return fallbackFamily.CreateFont(fontSize, FontStyle.Regular);

            throw new InvalidOperationException("No system fonts are available for certificate rendering.");
        }

        private static FontFamily? TryGetBundledFont()
        {
            if (_bundledFontLoaded)
                return _bundledFontFamily;

            _bundledFontLoaded = true;

            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "assets", "Roboto-Regular.ttf");
                if (!File.Exists(path))
                    return null;

                _bundledFontFamily = BundledFonts.Add(path);
                return _bundledFontFamily;
            }
            catch
            {
                _bundledFontFamily = null;
                return null;
            }
        }

        private static Color ParseColor(string hex)
        {
            try { return Color.ParseHex(hex); }
            catch { return Color.Black; }
        }
    }
}
