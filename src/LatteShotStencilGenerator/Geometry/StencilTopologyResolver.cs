namespace LatteShotStencilGenerator.Geometry;

public sealed record StencilBridge(StencilContour Contour, double WidthMm, int IslandIndex);

public sealed record ResolvedStencilTopology(
    IReadOnlyList<StencilContour> OpeningContours,
    IReadOnlyList<StencilBridge> Bridges,
    int DetectedIslandCount,
    int DetachedComponentCount,
    IReadOnlyList<StencilMaterialComponent> RetainedMaterialComponents)
{
    /// <summary>The canonical Clipper regions consumed by preview and meshing.</summary>
    public IReadOnlyList<PlanarPolygon> OpeningRegions { get; init; } = [];
}

public sealed record StencilMaterialComponent(int IslandIndex, RectMm Bounds);

/// <summary>Resolves retained-material connectivity from canonical polygon regions.</summary>
public static class StencilTopologyResolver
{
    private const double Epsilon = 0.000001;

    public static ResolvedStencilTopology Resolve(
        StencilArtwork artwork,
        RectMm workingArea,
        BridgeConfiguration configuration,
        IPolygonEngine? polygonEngine = null)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(configuration);

        var engine = polygonEngine ?? new ClipperPolygonEngine();
        var area = engine.Normalize([RectangleContour(workingArea)]);
        var artworkRegions = engine.Normalize(artwork.Contours);
        var openings = artwork.Invert
            ? engine.Difference(area, artworkRegions)
            : artworkRegions;
        var material = engine.Difference(area, openings);
        var islands = DetachedComponents(material, workingArea);
        var detectedIslandCount = islands.Count;

        if (configuration.Mode == BridgeMode.Off || islands.Count == 0)
            return Result(openings, [], detectedIslandCount, islands, islands);

        var width = configuration.ResolvedWidthMm;
        if (width > workingArea.Width + Epsilon || width > workingArea.Height + Epsilon)
            throw new InvalidOperationException("The configured bridge width does not fit inside the working area.");

        var bridges = new List<StencilBridge>(islands.Count);
        var islandIndex = 0;
        while (true)
        {
            var remaining = DetachedComponents(material, workingArea);
            if (remaining.Count == 0) break;
            var bridgeContour = BridgeToWorkingArea(ToContour(remaining[0]), workingArea, width);
            var bridge = engine.Normalize([bridgeContour]);
            material = engine.Union(material, bridge);
            bridges.Add(new StencilBridge(ToContour(bridge[0]), width, islandIndex++));
        }
        openings = engine.Difference(area, material);
        var unresolved = DetachedComponents(material, workingArea);
        if (unresolved.Count != 0)
            throw new InvalidOperationException("Automatic bridge generation could not connect all retained material.");
        return Result(openings, bridges, detectedIslandCount, unresolved, islands);
    }

    private static ResolvedStencilTopology Result(
        IReadOnlyList<PlanarPolygon> openings,
        IReadOnlyList<StencilBridge> bridges,
        int detectedIslandCount,
        IReadOnlyList<PlanarPolygon> detached,
        IReadOnlyList<PlanarPolygon> detected) => new(ToContours(openings), bridges, detectedIslandCount, detached.Count, ToMaterialComponents(detached))
    {
        RetainedMaterialComponents = ToMaterialComponents(detected),
        OpeningRegions = openings
    };

    private static IReadOnlyList<PlanarPolygon> DetachedComponents(IReadOnlyList<PlanarPolygon> material, RectMm area) =>
        material.Where(region => !TouchesBoundary(region, area)).ToArray();

    private static bool TouchesBoundary(PlanarPolygon region, RectMm area) => region.Outer.Any(point =>
        Math.Abs(point.X - area.X) <= Epsilon || Math.Abs(point.X - area.Right) <= Epsilon ||
        Math.Abs(point.Y - area.Y) <= Epsilon || Math.Abs(point.Y - area.Bottom) <= Epsilon);

    private static IReadOnlyList<StencilMaterialComponent> ToMaterialComponents(IEnumerable<PlanarPolygon> islands) =>
        islands.Select((island, index) =>
        {
            var bounds = Bounds(island.Outer);
            return new StencilMaterialComponent(index, new RectMm(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        }).ToArray();

    private static IReadOnlyList<StencilContour> ToContours(IReadOnlyList<PlanarPolygon> polygons) =>
        polygons.SelectMany(polygon => new[] { ToContour(polygon) }.Concat(polygon.Holes.Select(hole => ToContour(hole)))).ToArray();

    private static StencilContour ToContour(PlanarPolygon polygon) => ToContour(polygon.Outer);

    private static StencilContour ToContour(IReadOnlyList<PointMm> points) =>
        new(points.Append(points[0]).ToArray(), StencilFillRule.NonZero);

    private static StencilContour RectangleContour(RectMm area) => new(
        [new(area.X, area.Y), new(area.Right, area.Y), new(area.Right, area.Bottom), new(area.X, area.Bottom), new(area.X, area.Y)],
        StencilFillRule.NonZero);

    private static StencilContour BridgeToWorkingArea(StencilContour island, RectMm area, double width)
    {
        var bounds = Bounds(island.Points);
        var candidates = new[]
        {
            (Distance: bounds.Left - area.X, Priority: 0, Horizontal: true, TowardMinimum: true),
            (Distance: area.Right - bounds.Right, Priority: 1, Horizontal: true, TowardMinimum: false),
            (Distance: bounds.Top - area.Y, Priority: 2, Horizontal: false, TowardMinimum: true),
            (Distance: area.Bottom - bounds.Bottom, Priority: 3, Horizontal: false, TowardMinimum: false)
        };
        var selected = candidates.OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Priority).First();
        var half = width / 2;

        if (selected.Horizontal)
        {
            var y = Math.Clamp((bounds.Top + bounds.Bottom) / 2, area.Y + half, area.Bottom - half);
            var start = selected.TowardMinimum ? new PointMm(bounds.Left, y) : new PointMm(bounds.Right, y);
            var end = new PointMm(selected.TowardMinimum ? area.X : area.Right, y);
            return RectangleAround(start, end, width, area, extendStart: half, extendEnd: 0);
        }
        else
        {
            var x = Math.Clamp((bounds.Left + bounds.Right) / 2, area.X + half, area.Right - half);
            var start = selected.TowardMinimum ? new PointMm(x, bounds.Top) : new PointMm(x, bounds.Bottom);
            var end = new PointMm(x, selected.TowardMinimum ? area.Y : area.Bottom);
            return RectangleAround(start, end, width, area, extendStart: half, extendEnd: 0);
        }
    }

    private static StencilContour RectangleAround(PointMm start, PointMm end, double width, RectMm area, double extendStart, double extendEnd)
    {
        var points = RectanglePoints(start, end, width, extendStart, extendEnd);
        if (!Fits(points, area))
            throw new InvalidOperationException("A generated bridge would extend outside the working area.");
        return new StencilContour([.. points, points[0]], StencilFillRule.NonZero);
    }

    private static PointMm[] RectanglePoints(PointMm start, PointMm end, double width, double extendStart, double extendEnd)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length <= Epsilon) throw new InvalidOperationException("A material island touches its surrounding boundary and does not require a bridge.");
        var ux = dx / length;
        var uy = dy / length;
        var px = -uy * width / 2;
        var py = ux * width / 2;
        var a = new PointMm(start.X - ux * extendStart, start.Y - uy * extendStart);
        var b = new PointMm(end.X + ux * extendEnd, end.Y + uy * extendEnd);
        return
        [
            new(a.X - px, a.Y - py),
            new(b.X - px, b.Y - py),
            new(b.X + px, b.Y + py),
            new(a.X + px, a.Y + py)
        ];
    }

    private static bool Fits(IEnumerable<PointMm> points, RectMm area) => points.All(point =>
        point.X >= area.X - Epsilon && point.X <= area.Right + Epsilon &&
        point.Y >= area.Y - Epsilon && point.Y <= area.Bottom + Epsilon);


    private static (double Left, double Top, double Right, double Bottom) Bounds(IEnumerable<PointMm> points)
    {
        var array = points.ToArray();
        return (array.Min(point => point.X), array.Min(point => point.Y), array.Max(point => point.X), array.Max(point => point.Y));
    }

}
