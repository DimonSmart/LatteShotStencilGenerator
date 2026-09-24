using System.Text;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class SvgImportAdapterTests
{
    private readonly SvgImportAdapter _adapter = new();

    [Fact]
    public void Imports_filled_shapes_with_flattened_nested_transforms_and_fill_rule()
    {
        var outcome = Import("<svg><g transform='translate(10,20)'><rect transform='scale(2)' x='1' y='2' width='3' height='4' fill-rule='evenodd'/></g></svg>");

        var result = Assert.IsType<SvgImportResult>(outcome.Value);
        Assert.Equal("art.svg", result.SourceFileName);
        var contour = Assert.Single(result.Contours);
        Assert.Equal(SvgFillRule.EvenOdd, contour.FillRule);
        Assert.Equal(new SvgPoint(12, 24), contour.Points[0]);
        Assert.Equal(contour.Points[0], contour.Points[^1]);
    }

    [Theory]
    [InlineData("<svg><script/></svg>")]
    [InlineData("<svg onload='alert(1)'><rect width='1' height='1'/></svg>")]
    [InlineData("<svg><image href='https://example.test/a.png'/></svg>")]
    [InlineData("<svg><text>no</text></svg>")]
    [InlineData("<svg><path d='M 0 0 L 1'/></svg>")]
    public void Rejects_unsafe_unsupported_or_malformed_svg(string svg)
    {
        Assert.False(Import(svg).IsSuccess);
    }

    [Fact]
    public void Identifies_stroke_only_artwork()
    {
        var outcome = Import("<svg><path d='M0 0 L10 0 L10 10 Z' fill='none' stroke='black'/></svg>");

        Assert.False(outcome.IsSuccess);
        Assert.Contains("Convert strokes to filled paths", outcome.Error!.Message);
    }

    [Fact]
    public void Rejects_absent_and_oversized_input()
    {
        Assert.False(_adapter.Import(null, ReadOnlyMemory<byte>.Empty).IsSuccess);
        Assert.False(_adapter.Import("large.svg", new byte[SvgImportAdapter.MaximumFileBytes + 1]).IsSuccess);
    }

    [Fact]
    public void Rejects_more_than_5000_source_shapes()
    {
        var svg = "<svg>" + string.Concat(Enumerable.Repeat("<rect width='1' height='1'/>", SvgImportAdapter.MaximumSourceShapes + 1)) + "</svg>";
        Assert.False(Import(svg).IsSuccess);
    }

    private SvgImportOutcome Import(string svg) => _adapter.Import("art.svg", Encoding.UTF8.GetBytes(svg));
}
