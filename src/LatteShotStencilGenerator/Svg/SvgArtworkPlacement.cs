using LatteShotStencilGenerator.Geometry;

namespace LatteShotStencilGenerator.Svg;

/// <summary>Normalised placement of imported SVG contours in a card working area.</summary>
public sealed record SvgArtworkPlacement(
    SvgImportResult Artwork,
    RectMm WorkingArea,
    double FittedScaleMmPerUnit,
    double ScalePercent,
    double OffsetX,
    double OffsetY,
    SvgBounds SourceBounds)
{
    private const double BoundsTolerance = 0.000001;

    public double ScaleMmPerUnit => FittedScaleMmPerUnit * ScalePercent / 100d;

    public static SvgArtworkPlacement Fit(SvgImportResult artwork, RectMm workingArea, double paddingMm)
    {
        if (paddingMm < 0 || paddingMm * 2 >= workingArea.Width || paddingMm * 2 >= workingArea.Height)
            throw new ArgumentOutOfRangeException(nameof(paddingMm), "Artwork padding must leave space inside the working area.");

        var bounds = SvgBounds.From(artwork.Contours);
        var availableWidth = workingArea.Width - 2 * paddingMm;
        var availableHeight = workingArea.Height - 2 * paddingMm;
        var scale = Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height);
        return new SvgArtworkPlacement(artwork, workingArea, scale, 100, 0, 0, bounds);
    }

    public SvgArtworkPlacement WithAdjustments(double scalePercent, double offsetX, double offsetY)
    {
        if (!double.IsFinite(scalePercent) || scalePercent <= 0)
            throw new ArgumentOutOfRangeException(nameof(scalePercent), "Scale must be greater than zero.");
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
            throw new ArgumentOutOfRangeException("offset", "Offsets must be finite numbers.");

        return this with { ScalePercent = scalePercent, OffsetX = offsetX, OffsetY = offsetY };
    }

    public SvgArtworkPlacement Reset() => this with { ScalePercent = 100, OffsetX = 0, OffsetY = 0 };

    public SvgBounds PlacedBounds
    {
        get
        {
            var scale = ScaleMmPerUnit;
            var x = WorkingArea.X + (WorkingArea.Width - SourceBounds.Width * scale) / 2 - SourceBounds.X * scale + OffsetX;
            var y = WorkingArea.Y + (WorkingArea.Height - SourceBounds.Height * scale) / 2 - SourceBounds.Y * scale + OffsetY;
            return new SvgBounds(SourceBounds.X * scale + x, SourceBounds.Y * scale + y, SourceBounds.Width * scale, SourceBounds.Height * scale);
        }
    }

    public bool IsWithinWorkingArea => PlacedBounds.X >= WorkingArea.X - BoundsTolerance
        && PlacedBounds.Y >= WorkingArea.Y - BoundsTolerance
        && PlacedBounds.Right <= WorkingArea.Right + BoundsTolerance
        && PlacedBounds.Bottom <= WorkingArea.Bottom + BoundsTolerance;

    public string? ValidationMessage => IsWithinWorkingArea ? null : "Artwork extends outside the working area. Reset or adjust its scale and offsets before export.";

    public IReadOnlyList<SvgContour> PlacedContours() => Artwork.Contours.Select(Place).ToArray();

    public StencilArtwork ToStencilArtwork(bool invert = false) => new(
        PlacedContours().Select(contour => new StencilContour(
            contour.Points.Select(point => new PointMm(point.X, point.Y)).ToArray(),
            contour.FillRule == SvgFillRule.EvenOdd ? StencilFillRule.EvenOdd : StencilFillRule.NonZero)).ToArray(),
        invert);

    private SvgContour Place(SvgContour contour)
    {
        var scale = ScaleMmPerUnit;
        var x = WorkingArea.X + (WorkingArea.Width - SourceBounds.Width * scale) / 2 - SourceBounds.X * scale + OffsetX;
        var y = WorkingArea.Y + (WorkingArea.Height - SourceBounds.Height * scale) / 2 - SourceBounds.Y * scale + OffsetY;
        return contour with { Points = contour.Points.Select(point => new SvgPoint(point.X * scale + x, point.Y * scale + y)).ToArray() };
    }
}

public readonly record struct SvgBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static SvgBounds From(IEnumerable<SvgContour> contours)
    {
        var points = contours.SelectMany(contour => contour.Points).ToArray();
        if (points.Length == 0) throw new ArgumentException("Artwork must contain contours.", nameof(contours));
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        if (maxX <= minX || maxY <= minY) throw new ArgumentException("Artwork bounds must have positive width and height.", nameof(contours));
        return new SvgBounds(minX, minY, maxX - minX, maxY - minY);
    }
}
