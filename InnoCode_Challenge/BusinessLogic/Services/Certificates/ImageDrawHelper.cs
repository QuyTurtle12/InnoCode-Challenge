using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BusinessLogic.Services.Certificates
{
    internal static class ImageDrawHelper
    {
        private static readonly FontCollection BundledFonts = new FontCollection();
        private static readonly object BundledFontsLock = new();
        private static readonly Dictionary<string, FontFamily> BundledFontLookup = new(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<FontFamily> _bundledFontFamilies = Array.Empty<FontFamily>();
        private static bool _bundledFontsLoaded;

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
            ApplyFallbackFonts(options);

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
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                if (SystemFonts.TryGet(fontFamily, out var family))
                    return family.CreateFont(fontSize, FontStyle.Regular);

                var bundledByName = TryGetBundledFontByName(fontFamily);
                if (bundledByName != null)
                    return bundledByName.Value.CreateFont(fontSize, FontStyle.Regular);
            }

            var bundledFamily = TryGetFirstBundledFont();
            if (bundledFamily != null)
                return bundledFamily.Value.CreateFont(fontSize, FontStyle.Regular);

            var fallbackFamily = SystemFonts.Collection.Families.FirstOrDefault();
            if (fallbackFamily != null)
                return fallbackFamily.CreateFont(fontSize, FontStyle.Regular);

            throw new InvalidOperationException("No system fonts are available for certificate rendering.");
        }

        private static void ApplyFallbackFonts(TextOptions options)
        {
            var fallbacks = new List<FontFamily>();

            foreach (var bundled in GetBundledFonts())
                AddFallbackFont(fallbacks, bundled);

            AddSystemFontIfAvailable(fallbacks, "Segoe UI");
            AddSystemFontIfAvailable(fallbacks, "Arial Unicode MS");
            AddSystemFontIfAvailable(fallbacks, "Arial");
            AddSystemFontIfAvailable(fallbacks, "Tahoma");
            AddSystemFontIfAvailable(fallbacks, "DejaVu Sans");
            AddSystemFontIfAvailable(fallbacks, "Noto Sans");

            if (fallbacks.Count == 0)
                fallbacks.AddRange(SystemFonts.Collection.Families);

            if (fallbacks.Count > 0)
                options.FallbackFontFamilies = fallbacks;
        }

        private static void AddSystemFontIfAvailable(List<FontFamily> fallbacks, string name)
        {
            if (!SystemFonts.TryGet(name, out var family))
                return;

            AddFallbackFont(fallbacks, family);
        }

        private static void AddFallbackFont(List<FontFamily> fallbacks, FontFamily family)
        {
            if (!fallbacks.Any(f => string.Equals(f.Name, family.Name, StringComparison.OrdinalIgnoreCase)))
                fallbacks.Add(family);
        }

        private static FontFamily? TryGetBundledFontByName(string name)
        {
            EnsureBundledFontsLoaded();
            return BundledFontLookup.TryGetValue(name, out var family) ? family : null;
        }

        private static FontFamily? TryGetFirstBundledFont()
        {
            EnsureBundledFontsLoaded();
            return _bundledFontFamilies.FirstOrDefault();
        }

        private static IReadOnlyList<FontFamily> GetBundledFonts()
        {
            EnsureBundledFontsLoaded();
            return _bundledFontFamilies;
        }

        private static void EnsureBundledFontsLoaded()
        {
            if (_bundledFontsLoaded)
                return;

            lock (BundledFontsLock)
            try
            {
                if (_bundledFontsLoaded)
                    return;

                BundledFontLookup.Clear();
                var families = new List<FontFamily>();

                var assetsPath = Path.Combine(AppContext.BaseDirectory, "assets");
                if (Directory.Exists(assetsPath))
                {
                    foreach (var fontPath in Directory.EnumerateFiles(assetsPath, "*.ttf"))
                        TryAddBundledFont(fontPath, families);

                    foreach (var fontPath in Directory.EnumerateFiles(assetsPath, "*.otf"))
                        TryAddBundledFont(fontPath, families);
                }

                _bundledFontFamilies = families;
                _bundledFontsLoaded = true;
            }
            catch
            {
                _bundledFontFamilies = Array.Empty<FontFamily>();
                _bundledFontsLoaded = true;
            }
        }

        private static void TryAddBundledFont(string path, List<FontFamily> families)
        {
            try
            {
                var family = BundledFonts.Add(path);
                if (!BundledFontLookup.ContainsKey(family.Name))
                {
                    BundledFontLookup[family.Name] = family;
                    families.Add(family);
                }
            }
            catch
            {
                // Ignore invalid font files.
            }
        }

        private static Color ParseColor(string hex)
        {
            try { return Color.ParseHex(hex); }
            catch { return Color.Black; }
        }
    }
}
