using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class PreviewMeshBuilderTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;

    [Fact]
    public void Build_StopsAtIndependentPreviewTriangleLimit()
    {
        var geometry = CardGeometry.Create(Preset);

        var preview = PreviewMeshBuilder.Build(geometry, maximumTriangles: 1);

        Assert.False(preview.IsAvailable);
        Assert.Null(preview.Model);
        Assert.Null(preview.TriangleCount);
        Assert.NotNull(preview.Message);
        Assert.Contains("preview", preview.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limit", preview.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisabledPreview_DoesNotBlockExactStlExport()
    {
        var geometry = CardGeometry.Create(Preset);
        var preview = PreviewMeshBuilder.Build(geometry, maximumTriangles: 1);

        var export = StlExportGenerator.Generate(geometry, "simple.svg", "SIMPLE");
        var fullMesh = CardMeshGenerator.Generate(geometry);
        var validation = MeshValidator.Validate(fullMesh);

        Assert.False(preview.IsAvailable);
        Assert.True(export.IsSuccess, export.Error);
        Assert.True(validation.IsValid, validation.Message);
    }

    [Fact]
    public void Gat4_DefaultPreviewPolicy_DisablesComplexPreview_ButFullExportRemainsValid()
    {
        var imported = ImportFixture("gat4.svg");
        var placement = SvgArtworkPlacement.Fit(imported, Preset.WorkingArea, Preset.ArtworkPadding);
        var artwork = placement.ToStencilArtwork(false, Preset.GenerationResolutionMm, Preset.MaximumFlattenedSvgSegments);
        var geometry = CardGeometry.Create(Preset, artwork, Preset.BridgeConfiguration);

        var preview = PreviewMeshBuilder.Build(geometry);
        var export = StlExportGenerator.Generate(geometry, "gat4.svg", string.Empty);

        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.False(preview.IsAvailable);
        Assert.True(preview.EstimatedTriangleCount > PreviewMeshBuilder.DefaultMaximumTriangles);
        Assert.NotNull(preview.Message);
        Assert.Contains("STL export remains available", preview.Message);
        Assert.True(export.IsSuccess, export.Error);
    }

    private static SvgImportResult ImportFixture(string fixtureName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        var outcome = new SvgImportAdapter().Import(
            fixtureName,
            File.ReadAllBytes(path),
            Preset.MaximumFlattenedSvgSegments);
        return Assert.IsType<SvgImportResult>(outcome.Value);
    }
}
