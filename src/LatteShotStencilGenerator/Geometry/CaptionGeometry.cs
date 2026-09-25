namespace LatteShotStencilGenerator.Geometry;

public enum CaptionFont { Block, Wide }
public sealed record CaptionSettings(string Text, CaptionFont Font);
/// <summary>Canonical filled regions emitted by a bundled caption font.</summary>
public sealed record CaptionOutline(IReadOnlyList<PlanarPolygon> Regions, double Width, double Height);

/// <summary>Adapter boundary for bundled-font outline sources.</summary>
public interface ICaptionFontOutlineAdapter
{
    CaptionOutline GetOutlines(string text, CaptionFont font);
}

/// <summary>Caption geometry shared by the SVG preview and the mesh generator.</summary>
public sealed record EmbossedCaption(CaptionSettings Settings, IReadOnlyList<PlanarPolygon> Regions, double TopZ)
{
    public const double TargetHeight = 12.25;
    public const double EmbossHeight = 0.354;

    public static EmbossedCaption? Create(StencilPreset preset, CaptionSettings settings, ICaptionFontOutlineAdapter fontOutlines)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(fontOutlines);
        if (string.IsNullOrWhiteSpace(settings.Text)) return null;
        var outline = fontOutlines.GetOutlines(settings.Text, settings.Font);
        if (outline.Regions.Count == 0 || outline.Width <= 0 || outline.Height <= 0) return null;
        var sourcePoints = outline.Regions.SelectMany(RegionPoints).ToArray();
        var sourceLeft = sourcePoints.Min(point => point.X); var sourceTop = sourcePoints.Min(point => point.Y);
        var sourceWidth = sourcePoints.Max(point => point.X) - sourceLeft;
        var sourceHeight = sourcePoints.Max(point => point.Y) - sourceTop;
        var scale = Math.Min(TargetHeight / sourceHeight, preset.CaptionArea.Width / sourceWidth);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        var left = preset.CaptionArea.X + (preset.CaptionArea.Width - width) / 2d;
        var top = preset.CaptionArea.Y + (preset.CaptionArea.Height - height) / 2d;
        var regions = outline.Regions.Select(region => new PlanarPolygon(
            Transform(region.Outer, left, top, sourceLeft, sourceTop, scale),
            region.Holes.Select(hole => Transform(hole, left, top, sourceLeft, sourceTop, scale)).ToArray())).ToArray();
        return new EmbossedCaption(settings, regions, preset.RaisedSurfaceZ + EmbossHeight);
    }

    private static IEnumerable<PointMm> RegionPoints(PlanarPolygon region) => region.Outer.Concat(region.Holes.SelectMany(hole => hole));

    private static IReadOnlyList<PointMm> Transform(
        IReadOnlyList<PointMm> points, double left, double top, double sourceLeft, double sourceTop, double scale) =>
        points.Select(point => new PointMm(left + (point.X - sourceLeft) * scale, top + (point.Y - sourceTop) * scale)).ToArray();
}
