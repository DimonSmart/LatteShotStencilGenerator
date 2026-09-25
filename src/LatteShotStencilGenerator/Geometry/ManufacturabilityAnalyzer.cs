namespace LatteShotStencilGenerator.Geometry;

/// <summary>Category of an advisory manufacturing constraint observed in the generated geometry.</summary>
public enum ManufacturabilityFindingKind
{
    Wall,
    Opening,
    SvgDetail,
    Bridge,
    RetainedMaterial
}

/// <summary>A deterministic, non-blocking manufacturing warning suitable for display to a user.</summary>
public sealed record ManufacturabilityFinding(
    ManufacturabilityFindingKind Kind,
    string Message,
    double ActualMm,
    double RequiredMm);

/// <summary>Measures generated stencil geometry against the preset's advisory manufacturing thresholds.</summary>
public static class ManufacturabilityAnalyzer
{
    private const double Epsilon = 0.000001;

    public static IReadOnlyList<ManufacturabilityFinding> Analyze(
        StencilPreset preset,
        StencilArtwork artwork,
        ResolvedStencilTopology topology)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(artwork);
        ArgumentNullException.ThrowIfNull(topology);

        var findings = new List<ManufacturabilityFinding>();
        AddOpeningAndDetailFindings(findings, artwork, preset);
        AddWallFindings(findings, artwork, preset);
        AddBridgeFindings(findings, topology, preset);
        AddRetainedMaterialFindings(findings, topology, preset);
        return findings;
    }

    private static void AddOpeningAndDetailFindings(
        ICollection<ManufacturabilityFinding> findings,
        StencilArtwork artwork,
        StencilPreset preset)
    {
        foreach (var item in artwork.Contours.Select((contour, index) => (contour, index)).OrderBy(item => item.index))
        {
            var points = OpenPoints(item.contour);
            if (points.Count == 0) continue;
            var bounds = Bounds(points);
            var size = Math.Min(bounds.Width, bounds.Height);
            if (size + Epsilon < preset.MinimumFeatureSizeMm)
            {
                findings.Add(new(
                    ManufacturabilityFindingKind.Opening,
                    $"Opening {item.index + 1} is {Format(size)} mm at its narrowest bounding dimension; the minimum feature size is {Format(preset.MinimumFeatureSizeMm)} mm.",
                    size,
                    preset.MinimumFeatureSizeMm));
            }

            foreach (var segment in SegmentLengths(points).Select((length, index) => (length, index)).OrderBy(item => item.index))
            {
                if (segment.length + Epsilon >= preset.GenerationResolutionMm) continue;
                findings.Add(new(
                    ManufacturabilityFindingKind.SvgDetail,
                    $"SVG detail {item.index + 1}.{segment.index + 1} is {Format(segment.length)} mm; the generation resolution is {Format(preset.GenerationResolutionMm)} mm.",
                    segment.length,
                    preset.GenerationResolutionMm));
            }
        }
    }

    private static void AddWallFindings(ICollection<ManufacturabilityFinding> findings, StencilArtwork artwork, StencilPreset preset)
    {
        var bounds = artwork.Contours.Select((contour, index) => (Bounds: Bounds(OpenPoints(contour)), Index: index)).ToArray();
        for (var index = 0; index < bounds.Length; index++)
        for (var other = index + 1; other < bounds.Length; other++)
        {
            var clearance = RectangleClearance(bounds[index].Bounds, bounds[other].Bounds);
            if (clearance + Epsilon >= preset.MinimumWallThicknessMm) continue;
            findings.Add(new(
                ManufacturabilityFindingKind.Wall,
                $"Wall between artwork regions {bounds[index].Index + 1} and {bounds[other].Index + 1} is {Format(clearance)} mm by bounding clearance; the minimum wall thickness is {Format(preset.MinimumWallThicknessMm)} mm.",
                clearance,
                preset.MinimumWallThicknessMm));
        }
    }

    private static void AddBridgeFindings(ICollection<ManufacturabilityFinding> findings, ResolvedStencilTopology topology, StencilPreset preset)
    {
        foreach (var bridge in topology.Bridges.OrderBy(bridge => bridge.IslandIndex))
        {
            if (bridge.WidthMm + Epsilon >= preset.MinimumFeatureSizeMm) continue;
            findings.Add(new(
                ManufacturabilityFindingKind.Bridge,
                $"Bridge for retained component {bridge.IslandIndex + 1} is {Format(bridge.WidthMm)} mm wide; the minimum feature size is {Format(preset.MinimumFeatureSizeMm)} mm.",
                bridge.WidthMm,
                preset.MinimumFeatureSizeMm));
        }
    }

    private static void AddRetainedMaterialFindings(ICollection<ManufacturabilityFinding> findings, ResolvedStencilTopology topology, StencilPreset preset)
    {
        foreach (var component in topology.RetainedMaterialComponents.OrderBy(component => component.IslandIndex))
        {
            var size = Math.Min(component.Bounds.Width, component.Bounds.Height);
            if (size + Epsilon >= preset.MinimumFeatureSizeMm) continue;
            findings.Add(new(
                ManufacturabilityFindingKind.RetainedMaterial,
                $"Retained material component {component.IslandIndex + 1} is {Format(size)} mm at its narrowest bounding dimension; the minimum feature size is {Format(preset.MinimumFeatureSizeMm)} mm.",
                size,
                preset.MinimumFeatureSizeMm));
        }
    }

    private static IReadOnlyList<PointMm> OpenPoints(StencilContour contour) =>
        contour.Points.Count > 1 && contour.Points[0] == contour.Points[^1]
            ? contour.Points.Take(contour.Points.Count - 1).ToArray()
            : contour.Points;

    private static IEnumerable<double> SegmentLengths(IReadOnlyList<PointMm> points) =>
        Enumerable.Range(0, points.Count)
            .Select(index => Distance(points[index], points[(index + 1) % points.Count]));

    private static RectMm Bounds(IReadOnlyList<PointMm> points) => new(
        points.Min(point => point.X),
        points.Min(point => point.Y),
        points.Max(point => point.X) - points.Min(point => point.X),
        points.Max(point => point.Y) - points.Min(point => point.Y));

    private static double RectangleClearance(RectMm first, RectMm second)
    {
        var firstContainsSecond = Contains(first, second);
        if (firstContainsSecond || Contains(second, first))
        {
            var outer = firstContainsSecond ? first : second;
            var inner = firstContainsSecond ? second : first;
            return new[]
            {
                inner.X - outer.X,
                outer.Right - inner.Right,
                inner.Y - outer.Y,
                outer.Bottom - inner.Bottom
            }.Min();
        }

        var horizontal = Math.Max(0, Math.Max(first.X - second.Right, second.X - first.Right));
        var vertical = Math.Max(0, Math.Max(first.Y - second.Bottom, second.Y - first.Bottom));
        return horizontal > 0 && vertical > 0 ? Math.Sqrt(horizontal * horizontal + vertical * vertical) : Math.Max(horizontal, vertical);
    }

    private static bool Contains(RectMm outer, RectMm inner) =>
        outer.X <= inner.X && outer.Y <= inner.Y && outer.Right >= inner.Right && outer.Bottom >= inner.Bottom;

    private static double Distance(PointMm first, PointMm second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    private static string Format(double value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
