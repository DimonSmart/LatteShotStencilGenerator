namespace LatteShotStencilGenerator.Geometry;

public sealed record PreviewMeshBuildResult(
    MeshPreviewModel? Model,
    string? Message,
    int EstimatedTriangleCount,
    int? TriangleCount)
{
    public bool IsAvailable => Model is not null;
}

public static class PreviewMeshBuilder
{
    public const int DefaultMaximumTriangles = 8_000;

    private const int FixedTriangleAllowance = 256;
    private const int EstimatedTrianglesPerBoundarySegment = 48;

    public static PreviewMeshBuildResult Build(CardGeometry geometry, int maximumTriangles = DefaultMaximumTriangles)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        if (maximumTriangles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTriangles), "The preview triangle limit must be positive.");

        var estimate = EstimateTriangleCount(geometry);
        if (!geometry.IsExportable)
            return Disabled(estimate, "3D preview is unavailable until the geometry errors are fixed.");

        if (estimate > maximumTriangles)
        {
            return Disabled(
                estimate,
                $"3D preview disabled: the conservative preview estimate is about {estimate:N0} triangles, above the safe preview limit of {maximumTriangles:N0}. " +
                "STL export remains available. Simplify the SVG or increase generation resolution to enable the 3D preview.");
        }

        try
        {
            var previewGeometry = geometry with
            {
                Preset = geometry.Preset with { MaximumMeshTriangles = maximumTriangles }
            };
            var mesh = CardMeshGenerator.Generate(previewGeometry);
            if (mesh.Triangles.Count == 0)
                return Disabled(estimate, "3D preview is unavailable because the current geometry produced no preview triangles. STL export remains available.");

            return new PreviewMeshBuildResult(MeshPreviewModel.Create(mesh), null, estimate, mesh.Triangles.Count);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Disabled(
                estimate,
                $"3D preview disabled: {exception.Message} STL export remains available and will validate the full mesh before download.");
        }
    }

    public static int EstimateTriangleCount(CardGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        long boundarySegments = CountSegments(geometry.ResolvedTopology.OpeningRegions);
        if (geometry.Caption is not null)
            boundarySegments += CountSegments(geometry.Caption.Regions);

        var estimate = FixedTriangleAllowance + boundarySegments * EstimatedTrianglesPerBoundarySegment;
        return estimate >= int.MaxValue ? int.MaxValue : (int)estimate;
    }

    private static long CountSegments(IEnumerable<PlanarPolygon> regions)
    {
        long count = 0;
        foreach (var region in regions)
        {
            count += region.Outer.Count;
            foreach (var hole in region.Holes)
                count += hole.Count;
        }

        return count;
    }

    private static PreviewMeshBuildResult Disabled(int estimate, string message) =>
        new(null, message, estimate, null);
}
