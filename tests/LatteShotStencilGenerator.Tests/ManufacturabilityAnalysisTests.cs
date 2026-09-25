using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class ManufacturabilityAnalysisTests
{
    [Fact]
    public void Reference_donut_has_the_configured_manufacturability_defaults()
    {
        var preset = StencilPreset.ReferenceDonut;

        Assert.Equal(0.8, preset.MinimumWallThicknessMm);
        Assert.Equal(0.8, preset.MinimumFeatureSizeMm);
        Assert.Equal(0.2, preset.GenerationResolutionMm);
    }

    [Fact]
    public void Custom_copy_retains_editable_valid_manufacturability_thresholds()
    {
        var state = new StencilConfigurationState();
        state.Edit(preset => preset with
        {
            MinimumWallThicknessMm = 1.2,
            MinimumFeatureSizeMm = 0.6,
            GenerationResolutionMm = 0.1
        });

        Assert.True(state.IsValid, string.Join(" ", state.ValidationErrors));
        Assert.Equal(1.2, state.ActivePreset.MinimumWallThicknessMm);
        Assert.Equal(0.6, state.ActivePreset.MinimumFeatureSizeMm);
        Assert.Equal(0.1, state.ActivePreset.GenerationResolutionMm);
        Assert.Equal(0.8, StencilPreset.ReferenceDonut.MinimumWallThicknessMm);
    }

    [Theory]
    [InlineData("wall")]
    [InlineData("feature")]
    [InlineData("resolution")]
    public void Invalid_manufacturability_thresholds_are_actionable_configuration_errors(string invalidSetting)
    {
        var state = new StencilConfigurationState();
        state.Edit(preset => invalidSetting switch
        {
            "wall" => preset with { MinimumWallThicknessMm = 0 },
            "feature" => preset with { MinimumFeatureSizeMm = double.NaN },
            _ => preset with { GenerationResolutionMm = -0.1 }
        });

        Assert.False(state.IsValid);
        Assert.Contains(state.ValidationErrors, error => error.Contains("Minimum", StringComparison.OrdinalIgnoreCase) || error.Contains("Generation resolution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Geometry_reports_deterministic_advisory_findings_for_walls_openings_svg_details_bridges_and_retained_material()
    {
        var preset = StencilPreset.ReferenceDonut with
        {
            MinimumWallThicknessMm = 0.8,
            MinimumFeatureSizeMm = 0.8,
            GenerationResolutionMm = 0.2
        };
        var artwork = new StencilArtwork([
            Rectangle(20, 50, 50, 80),
            Rectangle(20.4, 60, 20.5, 61),
            Rectangle(28, 58, 28.4, 72)
        ]);
        var bridges = new BridgeConfiguration(BridgeMode.Auto, 0.3, 0.3);

        var first = CardGeometry.Create(preset, artwork, bridges);
        var second = CardGeometry.Create(preset, artwork, bridges);

        Assert.True(first.IsExportable, first.ValidationMessage);
        Assert.Contains(first.ManufacturabilityFindings, finding => finding.Kind == ManufacturabilityFindingKind.Wall);
        Assert.Contains(first.ManufacturabilityFindings, finding => finding.Kind == ManufacturabilityFindingKind.Opening);
        Assert.Contains(first.ManufacturabilityFindings, finding => finding.Kind == ManufacturabilityFindingKind.SvgDetail);
        Assert.Contains(first.ManufacturabilityFindings, finding => finding.Kind == ManufacturabilityFindingKind.Bridge);
        Assert.Contains(first.ManufacturabilityFindings, finding => finding.Kind == ManufacturabilityFindingKind.RetainedMaterial);
        Assert.Equal(first.ManufacturabilityFindings, second.ManufacturabilityFindings);
    }

    [Fact]
    public void Manufacturability_warnings_do_not_block_stl_export()
    {
        var preset = StencilPreset.ReferenceDonut with { MinimumFeatureSizeMm = 0.8, GenerationResolutionMm = 0.5 };
        var artwork = new StencilArtwork([Rectangle(20, 50, 20.4, 60)]);
        var geometry = CardGeometry.Create(preset, artwork);

        var export = StlExportGenerator.Generate(geometry, "thin.svg", "THIN");

        Assert.NotEmpty(geometry.ManufacturabilityFindings);
        Assert.True(geometry.IsExportable, geometry.ValidationMessage);
        Assert.True(export.IsSuccess, export.Error);
    }

    private static StencilContour Rectangle(double left, double top, double right, double bottom) =>
        new([new(left, top), new(right, top), new(right, bottom), new(left, bottom), new(left, top)], StencilFillRule.EvenOdd);
}
