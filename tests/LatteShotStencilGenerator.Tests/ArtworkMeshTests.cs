using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class ArtworkMeshTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;

    [Fact]
    public void Filled_contour_becomes_watertight_through_opening()
    {
        var geometry = Geometry(Contour((20, 50), (30, 50), (30, 60), (20, 60)));
        var mesh = CardMeshGenerator.Generate(geometry);

        Assert.True(geometry.IsExportable);
        Assert.True(MeshValidator.Validate(mesh).IsValid, MeshValidator.Validate(mesh).Message);
        Assert.DoesNotContain(mesh.Triangles, triangle => InSquare(Centre(triangle), 20, 50, 30, 60));
    }

    [Fact]
    public void Evenodd_nested_contour_preserves_inner_material_hole()
    {
        var geometry = Geometry(
            Contour((20, 50), (40, 50), (40, 70), (20, 70), StencilFillRule.EvenOdd),
            Contour((25, 55), (35, 55), (35, 65), (25, 65), StencilFillRule.EvenOdd));
        var mesh = CardMeshGenerator.Generate(geometry);

        Assert.True(MeshValidator.Validate(mesh).IsValid, MeshValidator.Validate(mesh).Message);
        Assert.Contains(mesh.Triangles, triangle => InSquare(Centre(triangle), 25, 55, 35, 65));
    }

    [Fact]
    public void Inversion_reverses_open_and_retained_regions_deterministically()
    {
        var contour = Contour((20, 50), (30, 50), (30, 60), (20, 60));
        var normal = CardMeshGenerator.Generate(Geometry(contour));
        var inverted = CardMeshGenerator.Generate(CardGeometry.Create(Preset, new StencilArtwork([contour], true)));

        Assert.DoesNotContain(normal.Triangles, triangle => InSquare(Centre(triangle), 20, 50, 30, 60));
        Assert.Contains(inverted.Triangles, triangle => InSquare(Centre(triangle), 20, 50, 30, 60));
        Assert.Equal(inverted.Triangles, CardMeshGenerator.Generate(CardGeometry.Create(Preset, new StencilArtwork([contour], true))).Triangles);
    }

    [Fact]
    public void Out_of_bounds_artwork_is_not_exportable_and_does_not_generate_mesh()
    {
        var geometry = Geometry(Contour((0, 0), (10, 0), (10, 10), (0, 10)));

        Assert.False(geometry.IsExportable);
        Assert.NotNull(geometry.ValidationMessage);
        Assert.Empty(CardMeshGenerator.Generate(geometry).Triangles);
    }

    private static CardGeometry Geometry(params StencilContour[] contours) => CardGeometry.Create(Preset, new StencilArtwork(contours));
    private static StencilContour Contour((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d, StencilFillRule fillRule = StencilFillRule.NonZero) =>
        new([new(a.X, a.Y), new(b.X, b.Y), new(c.X, c.Y), new(d.X, d.Y), new(a.X, a.Y)], fillRule);
    private static PointMm Centre(Triangle triangle) => new((triangle.A.X + triangle.B.X + triangle.C.X) / 3, (triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3);
    private static bool InSquare(PointMm point, double left, double top, double right, double bottom) => point.X > left && point.X < right && point.Y > top && point.Y < bottom;
}
