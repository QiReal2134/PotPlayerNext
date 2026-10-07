using System.Globalization;
using System.Xml;

namespace PotPlayerNext.Services;

/// <summary>Reads only a bounded SVG root header; never resolves XML entities or external resources.</summary>
public static class SvgDimensions
{
    public const int MaximumHeaderCharacters = 64 * 1024;
    public const uint DefaultWidth = 300;
    public const uint DefaultHeight = 150;

    /// <summary>Vectors may be rasterized above their viewport size; both pixel axes remain bounded.</summary>
    public static (int Width, int Height) RasterSize(uint width, uint height, uint maxEdge)
    {
        if (maxEdge == 0 || maxEdge > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(maxEdge));
        if (width == 0 || height == 0) { width = DefaultWidth; height = DefaultHeight; }
        var ratio = (double)maxEdge / Math.Max(width, height);
        return (Math.Max(1, (int)(width * ratio)), Math.Max(1, (int)(height * ratio)));
    }

    public static (uint Width, uint Height) Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumHeaderCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = false
        };
        using var reader = XmlReader.Create(input, settings);
        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg" ||
            (reader.NamespaceURI.Length != 0 && reader.NamespaceURI != "http://www.w3.org/2000/svg"))
            throw new InvalidDataException("The file does not have an SVG root element.");

        var width = ParseLength(reader.GetAttribute("width"));
        var height = ParseLength(reader.GetAttribute("height"));
        var viewBox = ParseViewBox(reader.GetAttribute("viewBox"));
        if (width == 0 && height == 0)
        {
            width = viewBox.Width > 0 ? viewBox.Width : DefaultWidth;
            height = viewBox.Height > 0 ? viewBox.Height : DefaultHeight;
        }
        else if (width == 0)
            width = viewBox.Width > 0 ? height * (viewBox.Width / viewBox.Height) : DefaultWidth;
        else if (height == 0)
            height = viewBox.Height > 0 ? width * (viewBox.Height / viewBox.Width) : DefaultHeight;

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return (DefaultWidth, DefaultHeight);
        // Preserve aspect ratio when a syntactically valid SVG exceeds the uint-sized API contract.
        var scale = Math.Min(1d, uint.MaxValue / Math.Max(width, height));
        return ((uint)Math.Clamp(Math.Ceiling(width * scale), 1d, uint.MaxValue),
            (uint)Math.Clamp(Math.Ceiling(height * scale), 1d, uint.MaxValue));
    }

    private static double ParseLength(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var value = text.AsSpan().Trim();
        var scale = 1d;
        if (value.EndsWith("px", StringComparison.Ordinal)) value = value[..^2];
        else if (value.EndsWith("in", StringComparison.Ordinal)) { value = value[..^2]; scale = 96; }
        else if (value.EndsWith("cm", StringComparison.Ordinal)) { value = value[..^2]; scale = 96 / 2.54; }
        else if (value.EndsWith("mm", StringComparison.Ordinal)) { value = value[..^2]; scale = 96 / 25.4; }
        else if (value.EndsWith("q", StringComparison.Ordinal)) { value = value[..^1]; scale = 96 / 101.6; }
        else if (value.EndsWith("pt", StringComparison.Ordinal)) { value = value[..^2]; scale = 96 / 72d; }
        else if (value.EndsWith("pc", StringComparison.Ordinal)) { value = value[..^2]; scale = 16; }
        // Relative lengths, including percentages, are layout dependent; use viewBox or the SVG default.
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
            double.IsFinite(parsed * scale) && parsed > 0 ? parsed * scale : 0;
    }

    private static (double Width, double Height) ParseViewBox(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        var values = text.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 4) return default;
        var numbers = new double[4];
        for (var index = 0; index < values.Length; index++)
            if (!double.TryParse(values[index], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[index]) ||
                !double.IsFinite(numbers[index])) return default;
        return numbers[2] > 0 && numbers[3] > 0 ? (numbers[2], numbers[3]) : default;
    }
}
