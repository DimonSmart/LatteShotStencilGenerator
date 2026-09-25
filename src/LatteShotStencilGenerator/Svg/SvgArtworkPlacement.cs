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

        var bounds = SvgBounds.From(artwork.Artwork);
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

    public IReadOnlyList<SvgContour> PlacedContours(double generationResolutionMm, int maximumFlattenedSegments)
    {
        try { return VectorFlattener.Flatten(PlacedArtwork(), generationResolutionMm / 4d, maximumFlattenedSegments); }
        catch (SvgFlattenLimitException) { throw; }
    }

    public StencilArtwork ToStencilArtwork(bool invert, double generationResolutionMm, int maximumFlattenedSegments) => new(
        PlacedContours(generationResolutionMm, maximumFlattenedSegments).Select(contour => new StencilContour(
            contour.Points.Select(point => new PointMm(point.X, point.Y)).ToArray(),
            contour.FillRule == SvgFillRule.EvenOdd ? StencilFillRule.EvenOdd : StencilFillRule.NonZero)).ToArray(),
        invert);

    private VectorArtwork PlacedArtwork() => new(Artwork.Artwork.Contours.Select(Place).ToArray());

    private VectorContour Place(VectorContour contour)
    {
        var scale = ScaleMmPerUnit;
        var x = WorkingArea.X + (WorkingArea.Width - SourceBounds.Width * scale) / 2 - SourceBounds.X * scale + OffsetX;
        var y = WorkingArea.Y + (WorkingArea.Height - SourceBounds.Height * scale) / 2 - SourceBounds.Y * scale + OffsetY;
        SvgPoint PlacePoint(SvgPoint point) => new(point.X * scale + x, point.Y * scale + y);
        return contour with { Start = PlacePoint(contour.Start), Segments = contour.Segments.Select(segment => (VectorSegment)(segment switch
        {
            VectorLineSegment line => new VectorLineSegment(PlacePoint(line.End)),
            VectorCubicBezierSegment cubic => new VectorCubicBezierSegment(PlacePoint(cubic.FirstControlPoint), PlacePoint(cubic.SecondControlPoint), PlacePoint(cubic.End)),
            _ => throw new InvalidOperationException()
        })).ToArray() };
    }
}

public readonly record struct SvgBounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public static SvgBounds From(VectorArtwork artwork)
    {
        var points = new List<SvgPoint>();
        foreach (var contour in artwork.Contours)
        {
            var start = contour.Start;
            points.Add(start);
            foreach (var segment in contour.Segments)
            {
                if (segment is VectorCubicBezierSegment cubic)
                {
                    AddExtrema(start.X, cubic.FirstControlPoint.X, cubic.SecondControlPoint.X, cubic.End.X, t => points.Add(new SvgPoint(CubicValue(start.X, cubic.FirstControlPoint.X, cubic.SecondControlPoint.X, cubic.End.X, t), CubicValue(start.Y, cubic.FirstControlPoint.Y, cubic.SecondControlPoint.Y, cubic.End.Y, t))));
                    AddExtrema(start.Y, cubic.FirstControlPoint.Y, cubic.SecondControlPoint.Y, cubic.End.Y, t => points.Add(new SvgPoint(CubicValue(start.X, cubic.FirstControlPoint.X, cubic.SecondControlPoint.X, cubic.End.X, t), CubicValue(start.Y, cubic.FirstControlPoint.Y, cubic.SecondControlPoint.Y, cubic.End.Y, t))));
                }
                points.Add(segment.End); start = segment.End;
            }
        }
        if (points.Count == 0) throw new ArgumentException("Artwork must contain contours.", nameof(artwork));
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        if (maxX <= minX || maxY <= minY) throw new ArgumentException("Artwork bounds must have positive width and height.", nameof(artwork));
        return new SvgBounds(minX, minY, maxX - minX, maxY - minY);
    }

    private static void AddExtrema(double p0, double p1, double p2, double p3, Action<double> add)
    {
        var a = -p0 + 3 * p1 - 3 * p2 + p3; var b = 2 * (p0 - 2 * p1 + p2); var c = p1 - p0;
        if (Math.Abs(a) < 1e-12) { if (Math.Abs(b) > 1e-12) Add(-c / b); return; }
        var discriminant = b * b - 4 * a * c; if (discriminant < 0) return; var root = Math.Sqrt(discriminant); Add((-b + root) / (2 * a)); Add((-b - root) / (2 * a));
        void Add(double t) { if (t > 0 && t < 1) add(t); }
    }
    private static double CubicValue(double p0, double p1, double p2, double p3, double t) { var u = 1 - t; return u*u*u*p0 + 3*u*u*t*p1 + 3*u*t*t*p2 + t*t*t*p3; }
}
