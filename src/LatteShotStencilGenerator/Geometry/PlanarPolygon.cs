namespace LatteShotStencilGenerator.Geometry;

/// <summary>A canonical filled planar region in millimetres: open counter-clockwise outer boundaries and open clockwise holes.</summary>
public sealed record PlanarPolygon(IReadOnlyList<PointMm> Outer, IReadOnlyList<IReadOnlyList<PointMm>> Holes);

/// <summary>Library-independent operations on canonical planar polygon regions.</summary>
public interface IPolygonEngine
{
    IReadOnlyList<PlanarPolygon> Normalize(IReadOnlyList<StencilContour> contours);

    IReadOnlyList<PlanarPolygon> Union(IReadOnlyList<PlanarPolygon> subject, IReadOnlyList<PlanarPolygon> clip);

    IReadOnlyList<PlanarPolygon> Difference(IReadOnlyList<PlanarPolygon> subject, IReadOnlyList<PlanarPolygon> clip);

    IReadOnlyList<PlanarPolygon> Intersection(IReadOnlyList<PlanarPolygon> subject, IReadOnlyList<PlanarPolygon> clip);

    IReadOnlyList<PlanarPolygon> Inflate(IReadOnlyList<PlanarPolygon> polygons, double deltaMm);
}
