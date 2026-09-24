namespace LatteShotStencilGenerator.Geometry;

/// <summary>Library-independent filled artwork supplied to the stencil geometry engine, in millimetres.</summary>
public sealed record StencilArtwork(IReadOnlyList<StencilContour> Contours, bool Invert = false)
{
    public static StencilArtwork Empty { get; } = new([]);
}

public sealed record StencilContour(IReadOnlyList<PointMm> Points, StencilFillRule FillRule);

public readonly record struct PointMm(double X, double Y);

public enum StencilFillRule { NonZero, EvenOdd }
