using Clipper2Lib;

namespace LatteShotStencilGenerator.Geometry;

/// <summary>Clipper2-backed implementation of the project's millimetre polygon contracts.</summary>
public sealed class ClipperPolygonEngine : IPolygonEngine
{
    // Integer coordinates make every boolean operation repeatable while retaining sub-micron millimetre detail.
    private const double Scale = 1_000_000d;
    private const long MaximumCoordinate = 4_000_000_000_000_000_000L / 2;

    public IReadOnlyList<PlanarPolygon> Normalize(IReadOnlyList<StencilContour> contours)
    {
        ArgumentNullException.ThrowIfNull(contours);

        var nonZero = ToPaths(contours.Where(contour => contour.FillRule == StencilFillRule.NonZero).Select(contour => contour.Points));
        var evenOdd = ToPaths(contours.Where(contour => contour.FillRule == StencilFillRule.EvenOdd).Select(contour => contour.Points));
        var normalizedNonZero = nonZero.Count == 0 ? new Paths64() : Clipper.Union(nonZero, FillRule.NonZero);
        var normalizedEvenOdd = evenOdd.Count == 0 ? new Paths64() : Clipper.Union(evenOdd, FillRule.EvenOdd);

        return ToPolygons(ExecuteTree(ClipType.Union, normalizedNonZero, normalizedEvenOdd));
    }

    public IReadOnlyList<PlanarPolygon> Union(IReadOnlyList<PlanarPolygon> subject, IReadOnlyList<PlanarPolygon> clip) =>
        ToPolygons(ExecuteTree(ClipType.Union, ToPaths(subject), ToPaths(clip)));

    public IReadOnlyList<PlanarPolygon> Difference(IReadOnlyList<PlanarPolygon> subject, IReadOnlyList<PlanarPolygon> clip) =>
        ToPolygons(ExecuteTree(ClipType.Difference, ToPaths(subject), ToPaths(clip)));

    public IReadOnlyList<PlanarPolygon> Intersection(IReadOnlyList<PlanarPolygon> subject, IReadOnlyList<PlanarPolygon> clip) =>
        ToPolygons(ExecuteTree(ClipType.Intersection, ToPaths(subject), ToPaths(clip)));

    public IReadOnlyList<PlanarPolygon> Inflate(IReadOnlyList<PlanarPolygon> polygons, double deltaMm)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        if (!double.IsFinite(deltaMm)) throw new ArgumentOutOfRangeException(nameof(deltaMm));
        if (deltaMm == 0) return ToPolygons(ExecuteTree(ClipType.Union, ToPaths(polygons), new Paths64()));

        var inflated = Clipper.InflatePaths(ToPaths(polygons), ToCoordinate(deltaMm), JoinType.Round, EndType.Polygon);
        return ToPolygons(ExecuteTree(ClipType.Union, inflated, new Paths64()));
    }

    private static PolyTree64 ExecuteTree(ClipType operation, Paths64 subject, Paths64 clip)
    {
        var result = new PolyTree64();
        Clipper.BooleanOp(operation, subject, clip, result, FillRule.NonZero);
        return result;
    }

    private static Paths64 ToPaths(IEnumerable<IReadOnlyList<PointMm>> contours)
    {
        var paths = new Paths64();
        foreach (var contour in contours)
        {
            var path = ToPath(contour);
            if (path.Count >= 3) paths.Add(path);
        }

        return paths;
    }

    private static Paths64 ToPaths(IReadOnlyList<PlanarPolygon> polygons)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        var paths = new Paths64();
        foreach (var polygon in polygons)
        {
            ArgumentNullException.ThrowIfNull(polygon);
            AddCanonicalPath(paths, polygon.Outer, counterClockwise: true);
            foreach (var hole in polygon.Holes)
                AddCanonicalPath(paths, hole, counterClockwise: false);
        }

        return paths;
    }

    private static void AddCanonicalPath(Paths64 paths, IReadOnlyList<PointMm> points, bool counterClockwise)
    {
        var path = ToPath(points);
        if (path.Count < 3) return;
        if ((Clipper.Area(path) > 0) != counterClockwise) path.Reverse();
        paths.Add(path);
    }

    private static Path64 ToPath(IReadOnlyList<PointMm> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var path = new Path64(points.Count);
        foreach (var point in points)
        {
            var converted = new Point64(ToCoordinate(point.X), ToCoordinate(point.Y));
            if (path.Count == 0 || path[^1] != converted) path.Add(converted);
        }

        if (path.Count > 1 && path[0] == path[^1]) path.RemoveAt(path.Count - 1);
        return path;
    }

    private static long ToCoordinate(double millimetres)
    {
        if (!double.IsFinite(millimetres) || Math.Abs(millimetres) > MaximumCoordinate / Scale)
            throw new ArgumentOutOfRangeException(nameof(millimetres));
        return checked((long)Math.Round(millimetres * Scale, MidpointRounding.AwayFromZero));
    }

    private static IReadOnlyList<PlanarPolygon> ToPolygons(PolyTree64 tree)
    {
        var polygons = new List<PlanarPolygon>();
        foreach (PolyPath64 child in tree)
            AddRegions(child, polygons);

        return polygons
            .OrderBy(polygon => SortKey(polygon.Outer))
            .ThenBy(polygon => polygon.Outer.Count)
            .ToArray();
    }

    private static void AddRegions(PolyPath64 node, List<PlanarPolygon> polygons)
    {
        if (!node.IsHole)
        {
            var holes = new List<IReadOnlyList<PointMm>>();
            foreach (PolyPath64 child in node)
                if (child.IsHole) holes.Add(CanonicalPoints(child.Polygon!, counterClockwise: false));

            polygons.Add(new PlanarPolygon(
                CanonicalPoints(node.Polygon!, counterClockwise: true),
                holes.OrderBy(SortKey).ThenBy(hole => hole.Count).ToArray()));
        }

        foreach (PolyPath64 child in node)
            AddRegions(child, polygons);
    }

    private static IReadOnlyList<PointMm> CanonicalPoints(Path64 path, bool counterClockwise)
    {
        var points = path.Select(point => new PointMm(point.X / Scale, point.Y / Scale)).ToList();
        if ((SignedArea(points) > 0) != counterClockwise) points.Reverse();

        var first = points
            .Select((point, index) => (point, index))
            .OrderBy(item => item.point.X)
            .ThenBy(item => item.point.Y)
            .Select(item => item.index)
            .First();
        return points.Skip(first).Concat(points.Take(first)).ToArray();
    }

    private static (double X, double Y, double Area) SortKey(IReadOnlyList<PointMm> points)
    {
        var first = points[0];
        return (first.X, first.Y, -Math.Abs(SignedArea(points)));
    }

    private static double SignedArea(IReadOnlyList<PointMm> points) =>
        points.Zip(points.Skip(1).Append(points[0]), (first, second) => first.X * second.Y - second.X * first.Y).Sum() / 2d;
}
