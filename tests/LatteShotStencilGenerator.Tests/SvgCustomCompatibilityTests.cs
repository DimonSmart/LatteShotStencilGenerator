using Svg;
using System.Text;
using LatteShotStencilGenerator.Geometry;
using LatteShotStencilGenerator.Svg;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class SvgCustomCompatibilityTests
{
    [Fact]
    public void Parses_supported_paths_with_nested_transforms()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">
              <g transform="translate(10 20)">
                <g transform="scale(2)">
                  <path d="M 1 1 C 5 1 5 9 9 9 Q 12 12 15 9 A 4 3 30 0 1 20 15 Z" fill="black" />
                </g>
              </g>
            </svg>
            """;

        var document = SvgDocument.FromSvg<SvgDocument>(svg);

        Assert.NotNull(document);
        Assert.Single(document.Children);
    }

    [Theory]
    [InlineData("<!DOCTYPE svg SYSTEM 'https://example.test/unsafe.dtd'><svg><rect width='1' height='1'/></svg>")]
    [InlineData("<svg><image href='https://example.test/external.png'/></svg>")]
    public void Rejects_unsafe_input_before_it_reaches_svg_custom(string svg)
    {
        var adapter = new SvgImportAdapter();

        var outcome = adapter.Import("unsafe.svg", Encoding.UTF8.GetBytes(svg), StencilPreset.ReferenceDonut.MaximumFlattenedSvgSegments);

        Assert.False(outcome.IsSuccess);
    }
}
