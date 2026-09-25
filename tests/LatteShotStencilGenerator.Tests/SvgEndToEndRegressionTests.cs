using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class SvgEndToEndRegressionTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;

    [Fact]
    public void Curved_path_commands_flatten_after_nested_transforms_and_generate_a_deterministic_mesh()
    {
        var imported = ImportFixture("curve-commands.svg");
        var placement = SvgArtworkPlacement.Fit(imported, Preset.WorkingArea, Preset.ArtworkPadding);
        var artwork = placement.ToStencilArtwork(false, Preset.GenerationResolutionMm, Preset.MaximumFlattenedSvgSegments);
        var first = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);
        var second = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);
        var mesh = CardMeshGenerator.Generate(first);

        Assert.True(placement.IsWithinWorkingArea);
        Assert.Single(artwork.Contours);
        Assert.True(artwork.Contours[0].Points.Count > 20);
        Assert.True(first.IsExportable, first.ValidationMessage);
        Assert.Equal(0, first.ResolvedTopology.DetachedComponentCount);
        Assert.True(MeshValidator.Validate(mesh).IsValid, MeshValidator.Validate(mesh).Message);
        Assert.Equal(first.ResolvedTopology.OpeningRegions.Select(RegionKey), second.ResolvedTopology.OpeningRegions.Select(RegionKey));
        Assert.Equal(mesh.Triangles, CardMeshGenerator.Generate(second).Triangles);
    }

    [Fact]
    public void Nested_holes_and_overlapping_touching_contours_are_canonicalized_then_bridged()
    {
        var imported = ImportFixture("topology-contours.svg");
        var placement = SvgArtworkPlacement.Fit(imported, Preset.WorkingArea, Preset.ArtworkPadding);
        var artwork = placement.ToStencilArtwork(false, Preset.GenerationResolutionMm, Preset.MaximumFlattenedSvgSegments);
        var canonical = new ClipperPolygonEngine().Normalize(artwork.Contours);
        var first = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);
        var second = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);
        var mesh = CardMeshGenerator.Generate(first);

        Assert.True(placement.IsWithinWorkingArea);
        Assert.Contains(canonical, region => region.Holes.Count > 0);
        Assert.True(first.IsExportable, first.ValidationMessage);
        Assert.True(first.ResolvedTopology.DetectedIslandCount > 0);
        Assert.NotEmpty(first.Bridges);
        Assert.Equal(0, first.ResolvedTopology.DetachedComponentCount);
        Assert.NotEmpty(first.ResolvedTopology.OpeningRegions);
        Assert.True(MeshValidator.Validate(mesh).IsValid, MeshValidator.Validate(mesh).Message);
        Assert.Equal(first.ResolvedTopology.OpeningRegions.Select(RegionKey), second.ResolvedTopology.OpeningRegions.Select(RegionKey));
        Assert.Equal(mesh.Triangles, CardMeshGenerator.Generate(second).Triangles);
    }

    [Fact]
    public void Gat4_inkscape_example_generates_a_valid_deterministic_mesh()
    {
        var imported = ImportFixture("gat4.svg");
        var placement = SvgArtworkPlacement.Fit(imported, Preset.WorkingArea, Preset.ArtworkPadding);
        var artwork = placement.ToStencilArtwork(false, Preset.GenerationResolutionMm, Preset.MaximumFlattenedSvgSegments);
        var first = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);
        var second = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);
        var mesh = CardMeshGenerator.Generate(first);

        Assert.True(placement.IsWithinWorkingArea);
        Assert.Single(artwork.Contours);
        Assert.True(first.IsExportable, first.ValidationMessage);
        Assert.Equal(0, first.ResolvedTopology.DetachedComponentCount);
        Assert.True(MeshValidator.Validate(mesh).IsValid, MeshValidator.Validate(mesh).Message);
        Assert.Equal(first.ResolvedTopology.OpeningRegions.Select(RegionKey), second.ResolvedTopology.OpeningRegions.Select(RegionKey));
        Assert.Equal(mesh.Triangles, CardMeshGenerator.Generate(second).Triangles);
    }

    private static SvgImportResult ImportFixture(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var outcome = new SvgImportAdapter().Import(fixtureName, File.ReadAllBytes(path), Preset.MaximumFlattenedSvgSegments);
        return Assert.IsType<SvgImportResult>(outcome.Value);
    }

    private static string RegionKey(PlanarPolygon region) => string.Join(";", region.Outer.Concat(region.Holes.SelectMany(hole => hole)));
}
