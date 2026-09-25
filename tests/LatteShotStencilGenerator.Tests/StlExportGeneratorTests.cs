using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class StlExportGeneratorTests
{
    private static readonly StencilPreset Preset = StencilPreset.ReferenceDonut;

    [Fact]
    public void Non_empty_caption_becomes_uppercase_filename()
    {
        Assert.Equal("LATTE TIME.stl", StlExportGenerator.DeriveFileName("  Latte Time  ", "donut.svg"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_caption_uses_source_filename_with_card_suffix(string? caption)
    {
        Assert.Equal("donut-card.stl", StlExportGenerator.DeriveFileName(caption, "donut.svg"));
    }

    [Fact]
    public void Export_is_blocked_until_an_svg_has_been_imported()
    {
        var result = StlExportGenerator.Generate(CardGeometry.Create(Preset), null, "DONUT");

        Assert.False(result.IsSuccess);
        Assert.Contains("Import an SVG", result.Error);
        Assert.Null(result.Content);
    }

    [Fact]
    public void Geometry_validation_blocks_payload_generation()
    {
        var contour = new StencilContour(
            [new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)],
            StencilFillRule.NonZero);
        var geometry = CardGeometry.Create(Preset, new StencilArtwork([contour]));

        var result = StlExportGenerator.Generate(geometry, "outside.svg", "OUTSIDE");

        Assert.False(result.IsSuccess);
        Assert.Contains("outside the working area", result.Error);
        Assert.Null(result.Content);
    }

    [Fact]
    public void Identical_current_geometry_produces_deterministic_binary_payload()
    {
        var geometry = CardGeometry.Create(Preset);

        var first = StlExportGenerator.Generate(geometry, "donut.svg", "DONUT");
        var second = StlExportGenerator.Generate(geometry, "donut.svg", "DONUT");

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal("DONUT.stl", first.FileName);
        Assert.Equal(first.Content, second.Content);
    }
}
