using LatteShotStencilGenerator.Geometry;
using Xunit;

namespace LatteShotStencilGenerator.Tests;

public sealed class ClipperPolygonEngineTests
{
    private readonly IPolygonEngine _engine = new ClipperPolygonEngine();

    [Fact]
    public void Normalize_MergesOverlappingAndTouchingRectanglesIntoOrderedCounterClockwiseRegions()
    {
        var polygons = _engine.Normalize([
            Rectangle(10, 0, 20, 10), Rectangle(0, 0, 10, 10), Rectangle(18, 0, 30, 10)
        ]);

        var polygon = Assert.Single(polygons);
        Assert.Empty(polygon.Holes);
        Assert.True(Area(polygon.Outer) > 0);
        Assert.Equal(new PointMm(0, 0), polygon.Outer[0]);
        Assert.DoesNotContain(polygon.Outer.Skip(1), point => point == polygon.Outer[0]);
        Assert.Equal(300, Math.Abs(Area(polygon.Outer)), 8);
    }

    [Fact]
    public void Normalize_EvenOddNestedContours_ProducesOuterHoleAndInnerIsland()
    {
        var polygons = _engine.Normalize([
            Rectangle(0, 0, 30, 30, StencilFillRule.EvenOdd),
            Rectangle(5, 5, 25, 25, StencilFillRule.EvenOdd),
            Rectangle(10, 10, 20, 20, StencilFillRule.EvenOdd)
        ]);

        Assert.Equal(2, polygons.Count);
        var outer = Assert.Single(polygons, polygon => polygon.Holes.Count == 1);
        Assert.True(Area(outer.Outer) > 0);
        Assert.True(Area(outer.Holes[0]) < 0);
        Assert.Contains(polygons, polygon => polygon.Holes.Count == 0 && Math.Abs(Area(polygon.Outer)) == 100);
    }

    [Fact]
    public void Normalize_ResolvesEachFillRuleAndDuplicateWindingDeterministically()
    {
        var nonZeroDuplicate = _engine.Normalize([Rectangle(0, 0, 10, 10), Rectangle(0, 0, 10, 10)]);
        var evenOddDuplicate = _engine.Normalize([
            Rectangle(0, 0, 10, 10, StencilFillRule.EvenOdd), Rectangle(0, 0, 10, 10, StencilFillRule.EvenOdd)
        ]);
        var oppositeNonZero = _engine.Normalize([Rectangle(0, 0, 10, 10), Rectangle(0, 10, 10, 0)]);

        Assert.Single(nonZeroDuplicate);
        Assert.Empty(evenOddDuplicate);
        Assert.Empty(oppositeNonZero);
    }

    [Fact]
    public void Difference_RetainsSubjectHole()
    {
        var subject = _engine.Normalize([
            Rectangle(0, 0, 30, 30, StencilFillRule.EvenOdd), Rectangle(10, 10, 20, 20, StencilFillRule.EvenOdd)
        ]);
        var result = _engine.Difference(subject, _engine.Normalize([Rectangle(0, 0, 5, 30)]));

        var polygon = Assert.Single(result);
        var hole = Assert.Single(polygon.Holes);
        Assert.True(Area(polygon.Outer) > 0);
        Assert.True(Area(hole) < 0);
        Assert.Equal(100, Math.Abs(Area(hole)), 8);
    }

    [Fact]
    public void Union_ConnectsMaterialWithBridge()
    {
        var left = _engine.Normalize([Rectangle(0, 0, 10, 10)]);
        var right = _engine.Normalize([Rectangle(20, 0, 30, 10)]);
        var bridge = _engine.Normalize([Rectangle(10, 4, 20, 6)]);

        var result = _engine.Union(_engine.Union(left, right), bridge);

        var polygon = Assert.Single(result);
        Assert.Empty(polygon.Holes);
        Assert.Equal(220, Math.Abs(Area(polygon.Outer)), 8);
    }

    [Fact]
    public void IntersectionAndInflate_AreCanonicalAndDeterministic()
    {
        var left = _engine.Normalize([Rectangle(0, 0, 10, 10)]);
        var right = _engine.Normalize([Rectangle(5, 0, 15, 10)]);

        var intersection = _engine.Intersection(left, right);
        var inflated = _engine.Inflate(intersection, 1);

        Assert.Equal(50, Math.Abs(Area(Assert.Single(intersection).Outer)), 8);
        Assert.True(Math.Abs(Area(Assert.Single(inflated).Outer)) > 50);
        var repeated = Assert.Single(_engine.Inflate(intersection, 1));
        var first = Assert.Single(inflated);
        Assert.Equal(first.Outer, repeated.Outer);
        Assert.Equal(first.Holes.Count, repeated.Holes.Count);
    }

    private static StencilContour Rectangle(double left, double top, double right, double bottom, StencilFillRule fillRule = StencilFillRule.NonZero) =>
        new([new(left, top), new(right, top), new(right, bottom), new(left, bottom), new(left, top)], fillRule);

    private static double Area(IReadOnlyList<PointMm> points) =>
        points.Zip(points.Skip(1).Append(points[0]), (first, second) => first.X * second.Y - second.X * first.Y).Sum() / 2d;
}
