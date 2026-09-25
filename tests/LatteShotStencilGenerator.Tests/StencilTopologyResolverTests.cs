using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class StencilTopologyResolverTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;
    private static readonly BridgeConfiguration Auto = new(BridgeMode.Auto, 1.5, 1.0);
    private static readonly BridgeConfiguration Off = new(BridgeMode.Off, 1.5, 1.0);

    [Theory]
    [MemberData(nameof(RepresentativeIslandArtwork))]
    public void Auto_bridges_representative_enclosed_artwork(StencilArtwork artwork, int expectedIslands)
    {
        var geometry = CardGeometry.Create(Preset, artwork, Auto);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.Equal(expectedIslands, geometry.ResolvedTopology.DetectedIslandCount);
        Assert.Equal(0, geometry.ResolvedTopology.DetachedComponentCount);
        Assert.Equal(expectedIslands, geometry.Bridges.Count);
    }

    [Fact]
    public void Multiple_islands_are_resolved_deterministically()
    {
        var artwork = new StencilArtwork([
            Rectangle(18, 45, 55, 82),
            Rectangle(23, 50, 31, 58),
            Rectangle(39, 65, 49, 76)
        ]);

        var first = CardGeometry.Create(Preset, artwork, Auto);
        var second = CardGeometry.Create(Preset, artwork, Auto);

        Assert.Equal(2, first.Bridges.Count);
        Assert.Equal(
            first.Bridges.Select(bridge => bridge.Contour.Points.ToArray()),
            second.Bridges.Select(bridge => bridge.Contour.Points.ToArray()),
            PointSequenceComparer.Instance);
        Assert.Equal(
            CardMeshGenerator.Generate(first).Triangles,
            CardMeshGenerator.Generate(second).Triangles);
    }

    [Fact]
    public void Nested_contours_and_inversion_identify_their_material_components()
    {
        var artwork = new StencilArtwork([
            Rectangle(18, 45, 58, 85),
            Rectangle(24, 51, 52, 79),
            Rectangle(31, 58, 45, 72)
        ], true);

        var geometry = CardGeometry.Create(Preset, artwork, Auto);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.Equal(2, geometry.ResolvedTopology.DetectedIslandCount);
        Assert.Equal(0, geometry.ResolvedTopology.DetachedComponentCount);
        Assert.Equal(2, geometry.Bridges.Count);
        var validation = MeshValidator.Validate(CardMeshGenerator.Generate(geometry));
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void Bridges_stay_in_bounds_and_honor_the_configured_minimum_width()
    {
        var configuration = new BridgeConfiguration(BridgeMode.Auto, 0.4, 1.2);
        var geometry = CardGeometry.Create(Preset, Ring(), configuration);

        var bridge = Assert.Single(geometry.Bridges);
        Assert.Equal(1.2, bridge.WidthMm, 6);
        Assert.All(bridge.Contour.Points, point =>
        {
            Assert.InRange(point.X, Preset.WorkingArea.X, Preset.WorkingArea.Right);
            Assert.InRange(point.Y, Preset.WorkingArea.Y, Preset.WorkingArea.Bottom);
        });
        var edges = bridge.Contour.Points.Zip(bridge.Contour.Points.Skip(1), Distance).Order().ToArray();
        Assert.Equal(1.2, edges[0], 6);
        Assert.Equal(1.2, edges[1], 6);
    }

    [Fact]
    public void Off_allows_artwork_without_islands()
    {
        var geometry = CardGeometry.Create(Preset, new StencilArtwork([Rectangle(20, 50, 40, 70)]), Off);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.Empty(geometry.Bridges);
    }

    [Fact]
    public void Off_allows_inverted_material_that_reaches_the_working_area_boundary()
    {
        var area = Preset.WorkingArea;
        var artwork = new StencilArtwork([Rectangle(area.X, area.Y, area.Right, area.Bottom)], true);
        var geometry = CardGeometry.Create(Preset, artwork, Off);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.Equal(0, geometry.ResolvedTopology.DetachedComponentCount);
        Assert.True(MeshValidator.Validate(CardMeshGenerator.Generate(geometry)).IsValid);
    }

    [Fact]
    public void Off_blocks_export_with_an_actionable_island_error()
    {
        var geometry = CardGeometry.Create(Preset, Ring(), Off);

        Assert.False(geometry.IsExportable);
        Assert.Contains("detached material island", geometry.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("automatic bridges", geometry.ValidationMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(CardMeshGenerator.Generate(geometry).Triangles);
    }

    [Fact]
    public void Automatically_bridged_artwork_generates_a_watertight_manifold_mesh()
    {
        var geometry = CardGeometry.Create(Preset, Ring(), Auto);
        var mesh = CardMeshGenerator.Generate(geometry);
        var validation = MeshValidator.Validate(mesh);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.Single(geometry.Bridges);
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void Overlapping_touching_and_nested_artwork_is_canonical_before_topology_and_mesh_generation()
    {
        var artwork = new StencilArtwork([
            Rectangle(18, 45, 38, 65),
            Rectangle(38, 45, 58, 65),
            Rectangle(22, 49, 54, 81),
            Rectangle(28, 55, 48, 75)
        ]);

        var geometry = CardGeometry.Create(Preset, artwork, Auto);
        var repeated = CardGeometry.Create(Preset, artwork, Auto);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.All(geometry.Artwork.Contours, contour => Assert.Equal(StencilFillRule.NonZero, contour.FillRule));
        Assert.NotEmpty(geometry.ResolvedTopology.OpeningRegions);
        Assert.Equal(
            geometry.ResolvedTopology.OpeningRegions.Select(RegionKey),
            repeated.ResolvedTopology.OpeningRegions.Select(RegionKey));
    }

    public static TheoryData<StencilArtwork, int> RepresentativeIslandArtwork() => new()
    {
        { Ring(), 1 },
        { new StencilArtwork([Triangle((18, 80), (38, 45), (58, 80)), Triangle((32, 69), (38, 57), (44, 69))]), 1 },
        { new StencilArtwork([Rectangle(18, 45, 58, 85), Rectangle(24, 51, 32, 59), Rectangle(43, 68, 52, 78)]), 2 }
    };

    private static StencilArtwork Ring() => new([
        Rectangle(18, 45, 58, 85),
        Rectangle(28, 55, 48, 75)
    ]);

    private static StencilContour Rectangle(double left, double top, double right, double bottom) =>
        new([new(left, top), new(right, top), new(right, bottom), new(left, bottom), new(left, top)], StencilFillRule.EvenOdd);

    private static StencilContour Triangle((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        new([new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y), new(a.X, a.Y)], StencilFillRule.EvenOdd);

    private static double Distance(PointMm a, PointMm b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static string RegionKey(PlanarPolygon region) => string.Join(";", region.Outer.Concat(region.Holes.SelectMany(hole => hole)));

    private sealed class PointSequenceComparer : IEqualityComparer<PointMm[]>
    {
        public static PointSequenceComparer Instance { get; } = new();
        public bool Equals(PointMm[]? x, PointMm[]? y) => x is not null && y is not null && x.SequenceEqual(y);
        public int GetHashCode(PointMm[] obj) => 0;
    }
}
