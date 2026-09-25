namespace LatteShotStencilGenerator.Geometry;

public sealed record StencilBridge(StencilContour Contour, double WidthMm, int IslandIndex);

public sealed record ResolvedStencilTopology(
    IReadOnlyList<StencilContour> OpeningContours,
    IReadOnlyList<StencilBridge> Bridges,
    int DetectedIslandCount,
    int DetachedComponentCount,
    IReadOnlyList<StencilMaterialComponent> RetainedMaterialComponents);

public sealed record StencilMaterialComponent(int IslandIndex, RectMm Bounds);

/// <summary>Resolves retained-material connectivity without altering the supplied artwork contours.</summary>
public static class StencilTopologyResolver
{
    private const double Epsilon = 0.000001;

    public static ResolvedStencilTopology Resolve(
        StencilArtwork artwork,
        RectMm workingArea,
        BridgeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(configuration);

        if (artwork.Contours.Count == 0)
            return new(artwork.Invert ? [RectangleContour(workingArea)] : [], [], 0, 0, []);

        var nodes = BuildNodes(artwork.Contours);
        var openingContours = ResolveOpeningContours(artwork, workingArea, nodes);
        var islands = nodes
            .Where(node => IsMaterial(node.InsideWinding, artwork.Invert) &&
                           !IsMaterial(node.OutsideWinding, artwork.Invert) &&
                           !(node.Parent is null && TouchesBoundary(node.Contour, workingArea)))
            .OrderBy(node => node.Depth)
            .ThenBy(node => node.Index)
            .ToArray();

        if (configuration.Mode == BridgeMode.Off || islands.Length == 0)
            return new(openingContours, [], islands.Length, islands.Length, ToMaterialComponents(islands));

        var width = configuration.ResolvedWidthMm;
        if (width > workingArea.Width + Epsilon || width > workingArea.Height + Epsilon)
            throw new InvalidOperationException("The configured bridge width does not fit inside the working area.");

        var bridges = new List<StencilBridge>(islands.Length);
        foreach (var island in islands)
        {
            var openingBoundary = Ancestors(island)
                .FirstOrDefault(node => !IsMaterial(node.InsideWinding, artwork.Invert) &&
                                        IsMaterial(node.OutsideWinding, artwork.Invert));
            var contour = openingBoundary is null
                ? BridgeToWorkingArea(island.Contour, workingArea, width)
                : BridgeBetween(island.Contour, openingBoundary.Contour, workingArea, width);
            bridges.Add(new StencilBridge(contour, width, island.Index));
        }

        return new(openingContours, bridges, islands.Length, 0, ToMaterialComponents(islands));
    }

    private static IReadOnlyList<StencilMaterialComponent> ToMaterialComponents(IEnumerable<Node> islands) =>
        islands.Select(island =>
        {
            var bounds = Bounds(island.Contour.Points);
            return new StencilMaterialComponent(
                island.Index,
                new RectMm(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        }).ToArray();

    private static IReadOnlyList<StencilContour> ResolveOpeningContours(StencilArtwork artwork, RectMm area, IReadOnlyList<Node> nodes)
    {
        var contours = new List<StencilContour>(nodes.Count + 1);
        if (artwork.Invert) contours.Add(RectangleContour(area));
        foreach (var node in nodes.OrderBy(node => node.Index))
        {
            var points = node.Contour.Points.Take(node.Contour.Points.Count - 1).ToArray();
            var wantPositive = EffectiveWinding(node) > 0;
            if (artwork.Invert) wantPositive = !wantPositive;
            if ((Area(points) > 0) != wantPositive) Array.Reverse(points);
            contours.Add(new StencilContour([.. points, points[0]], StencilFillRule.NonZero));
        }
        return contours;
    }

    private static StencilContour RectangleContour(RectMm area) => new(
        [new(area.X, area.Y), new(area.Right, area.Y), new(area.Right, area.Bottom), new(area.X, area.Bottom), new(area.X, area.Y)],
        StencilFillRule.NonZero);

    private static IReadOnlyList<Node> BuildNodes(IReadOnlyList<StencilContour> contours)
    {
        var nodes = contours.Select((contour, index) => new Node(index, contour, Math.Abs(Area(contour.Points)))).ToArray();
        foreach (var node in nodes)
        {
            var sample = InteriorSample(node.Contour.Points);
            node.Parent = nodes
                .Where(candidate => candidate.Index != node.Index && candidate.AbsoluteArea > node.AbsoluteArea + Epsilon && Contains(candidate.Contour.Points, sample))
                .OrderBy(candidate => candidate.AbsoluteArea)
                .ThenBy(candidate => candidate.Index)
                .FirstOrDefault();
        }

        foreach (var node in nodes)
            node.Depth = Ancestors(node).Count();

        foreach (var node in nodes)
        {
            node.OutsideWinding = Ancestors(node).Reverse().Sum(EffectiveWinding);
            node.InsideWinding = node.OutsideWinding + EffectiveWinding(node);
        }
        return nodes;
    }

    private static IEnumerable<Node> Ancestors(Node node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            yield return parent;
    }

    private static int EffectiveWinding(Node node)
    {
        if (node.Contour.FillRule == StencilFillRule.EvenOdd)
            return node.Depth % 2 == 0 ? 1 : -1;
        return Area(node.Contour.Points) >= 0 ? 1 : -1;
    }

    private static bool IsMaterial(int winding, bool invert) => invert ? winding != 0 : winding == 0;

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

    private static StencilContour BridgeBetween(StencilContour island, StencilContour boundary, RectMm area, double width)
    {
        Candidate? best = null;
        var islandPoints = island.Points.Take(island.Points.Count - 1).ToArray();
        var boundaryPoints = boundary.Points;
        for (var sourceIndex = 0; sourceIndex < islandPoints.Length; sourceIndex++)
        for (var segmentIndex = 0; segmentIndex < boundaryPoints.Count - 1; segmentIndex++)
        {
            var target = ClosestPoint(islandPoints[sourceIndex], boundaryPoints[segmentIndex], boundaryPoints[segmentIndex + 1]);
            var distance = SquaredDistance(islandPoints[sourceIndex], target);
            var candidate = new Candidate(islandPoints[sourceIndex], target, distance, sourceIndex, segmentIndex);
            if ((best is null || candidate.CompareTo(best.Value) < 0) &&
                Fits(RectanglePoints(candidate.Source, candidate.Target, width, width / 2, width / 2), area))
                best = candidate;
        }

        if (best is null)
            throw new InvalidOperationException("No bridge of the configured width fits between a material island and its surrounding opening.");
        return RectangleAround(best.Value.Source, best.Value.Target, width, area, width / 2, width / 2);
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

    private static bool TouchesBoundary(StencilContour contour, RectMm area) => contour.Points.Any(point =>
        Math.Abs(point.X - area.X) <= Epsilon || Math.Abs(point.X - area.Right) <= Epsilon ||
        Math.Abs(point.Y - area.Y) <= Epsilon || Math.Abs(point.Y - area.Bottom) <= Epsilon);

    private static PointMm ClosestPoint(PointMm point, PointMm a, PointMm b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var denominator = dx * dx + dy * dy;
        var t = denominator <= Epsilon ? 0 : Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / denominator, 0, 1);
        return new PointMm(a.X + t * dx, a.Y + t * dy);
    }

    private static PointMm InteriorSample(IReadOnlyList<PointMm> polygon)
    {
        var centroid = new PointMm(polygon.Take(polygon.Count - 1).Average(point => point.X), polygon.Take(polygon.Count - 1).Average(point => point.Y));
        if (Contains(polygon, centroid)) return centroid;
        var first = polygon[0];
        return new PointMm(first.X * 0.999 + centroid.X * 0.001, first.Y * 0.999 + centroid.Y * 0.001);
    }

    private static bool Contains(IReadOnlyList<PointMm> polygon, PointMm point)
    {
        var inside = false;
        for (var i = 0; i < polygon.Count - 1; i++)
        {
            var a = polygon[i];
            var b = polygon[i + 1];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    private static double Area(IReadOnlyList<PointMm> points)
    {
        var count = points.Count > 1 && points[0] == points[^1] ? points.Count - 1 : points.Count;
        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var next = (index + 1) % count;
                return points[index].X * points[next].Y - points[next].X * points[index].Y;
            })
            .Sum() / 2;
    }

    private static (double Left, double Top, double Right, double Bottom) Bounds(IEnumerable<PointMm> points)
    {
        var array = points.ToArray();
        return (array.Min(point => point.X), array.Min(point => point.Y), array.Max(point => point.X), array.Max(point => point.Y));
    }

    private static double SquaredDistance(PointMm a, PointMm b) => (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    private sealed class Node(int index, StencilContour contour, double absoluteArea)
    {
        public int Index { get; } = index;
        public StencilContour Contour { get; } = contour;
        public double AbsoluteArea { get; } = absoluteArea;
        public Node? Parent { get; set; }
        public int Depth { get; set; }
        public int OutsideWinding { get; set; }
        public int InsideWinding { get; set; }
    }

    private readonly record struct Candidate(PointMm Source, PointMm Target, double Distance, int SourceIndex, int SegmentIndex) : IComparable<Candidate>
    {
        public int CompareTo(Candidate other)
        {
            var comparison = Distance.CompareTo(other.Distance);
            if (comparison != 0) return comparison;
            comparison = SourceIndex.CompareTo(other.SourceIndex);
            return comparison != 0 ? comparison : SegmentIndex.CompareTo(other.SegmentIndex);
        }
    }
}
