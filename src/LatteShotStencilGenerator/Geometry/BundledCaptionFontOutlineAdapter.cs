namespace LatteShotStencilGenerator.Geometry;

/// <summary>Deterministic local outlines for the bundled caption fonts.</summary>
public sealed class BundledCaptionFontOutlineAdapter : ICaptionFontOutlineAdapter
{
    private readonly IPolygonEngine _polygonEngine;

    private static readonly IReadOnlyDictionary<char, string> Segments = new Dictionary<char, string>
    {
        ['A'] = "abcefg", ['B'] = "cdefg", ['C'] = "adef", ['D'] = "bcdef", ['E'] = "adefg", ['F'] = "aefg", ['G'] = "acdef", ['H'] = "bcefg", ['I'] = "bc", ['J'] = "bcd", ['K'] = "efg", ['L'] = "def", ['M'] = "abcef", ['N'] = "abcef", ['O'] = "abcdef", ['P'] = "abefg", ['Q'] = "abcdefg", ['R'] = "abcefg", ['S'] = "acdfg", ['T'] = "defg", ['U'] = "bcdef", ['V'] = "bcdef", ['W'] = "bcdef", ['X'] = "bcefg", ['Y'] = "bcdfg", ['Z'] = "abdeg", ['0'] = "abcdef", ['1'] = "bc", ['2'] = "abdeg", ['3'] = "abcdg", ['4'] = "bcfg", ['5'] = "acdfg", ['6'] = "acdefg", ['7'] = "abc", ['8'] = "abcdefg", ['9'] = "abcdfg", ['-'] = "g", ['_'] = "d"
    };

    public BundledCaptionFontOutlineAdapter(IPolygonEngine polygonEngine)
    {
        _polygonEngine = polygonEngine ?? throw new ArgumentNullException(nameof(polygonEngine));
    }

    public CaptionOutline GetOutlines(string text, CaptionFont font)
    {
        ArgumentNullException.ThrowIfNull(text);
        const double glyphHeight = 10;
        var glyphWidth = font == CaptionFont.Wide ? 7 : 6;
        var advance = glyphWidth + 2;
        var strokes = new List<StencilContour>();
        for (var index = 0; index < text.Length; index++)
        {
            if (!Segments.TryGetValue(char.ToUpperInvariant(text[index]), out var segments)) continue;

            var glyphStrokes = segments
                .Select(segment => Segment(segment, index * advance, glyphWidth))
                .ToArray();
            strokes.AddRange(glyphStrokes);
        }

        // The font source owns only raw closed glyph strokes. The project polygon
        // engine resolves their overlaps and emits canonical regions directly to
        // fitting, preview, and mesh generation.
        var regions = strokes.Count == 0 ? [] : _polygonEngine.Normalize(strokes);
        return new CaptionOutline(regions, Math.Max(1, text.Length * advance - 2), glyphHeight);
    }

    private static StencilContour Segment(char segment, double offset, double width)
    {
        const double thickness = 1.25;
        const double gap = .1;
        var half = 5d - thickness / 2d - 2 * gap;
        var (x, y, w, h) = segment switch
        {
            'a' => (offset + thickness + gap, 0d, width - 2 * thickness - 2 * gap, thickness),
            'b' => (offset + width - thickness, thickness + gap, thickness, half),
            'c' => (offset + width - thickness, 5d + thickness / 2d + gap, thickness, half),
            'd' => (offset + thickness + gap, 10d - thickness, width - 2 * thickness - 2 * gap, thickness),
            'e' => (offset, 5d + thickness / 2d + gap, thickness, half),
            'f' => (offset, thickness + gap, thickness, half),
            'g' => (offset + thickness + gap, 5d - thickness / 2d, width - 2 * thickness - 2 * gap, thickness),
            _ => throw new ArgumentOutOfRangeException(nameof(segment))
        };
        return new StencilContour([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h), new(x, y)], StencilFillRule.NonZero);
    }
}
