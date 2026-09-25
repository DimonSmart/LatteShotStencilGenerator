using System.Drawing;
using System.Xml;
using System.Xml.Linq;
using Svg;
using Svg.Pathing;
using Svg.Transforms;

namespace LatteShotStencilGenerator.Svg;

public enum SvgFillRule { NonZero, EvenOdd }
public readonly record struct SvgPoint(double X, double Y);
public abstract record VectorSegment(SvgPoint End);
public sealed record VectorLineSegment(SvgPoint End) : VectorSegment(End);
public sealed record VectorCubicBezierSegment(SvgPoint FirstControlPoint, SvgPoint SecondControlPoint, SvgPoint End) : VectorSegment(End);
public sealed record VectorContour(SvgPoint Start, IReadOnlyList<VectorSegment> Segments, SvgFillRule FillRule, bool IsClosed);
public sealed record VectorArtwork(IReadOnlyList<VectorContour> Contours);
public sealed record SvgContour(IReadOnlyList<SvgPoint> Points, SvgFillRule FillRule);
public sealed record SvgImportResult(string SourceFileName, VectorArtwork Artwork)
{
    // Inspection projection; placement keeps the unflattened Artwork until its physical scale is known.
    public IReadOnlyList<SvgContour> Contours => VectorFlattener.Flatten(Artwork, .05, int.MaxValue);
    public int FlattenedSegmentCount => Contours.Sum(x => x.Points.Count - 1);
}
public sealed record SvgImportError(string Message);
public sealed record SvgImportOutcome(SvgImportResult? Value, SvgImportError? Error)
{ public bool IsSuccess => Value is not null; public static SvgImportOutcome Success(SvgImportResult value) => new(value, null); public static SvgImportOutcome Failure(string message) => new(null, new SvgImportError(message)); }
public interface ISvgImportAdapter { SvgImportOutcome Import(string? sourceFileName, ReadOnlyMemory<byte> content, int maximumFlattenedSegments); }

/// <summary>Safe, fill-only adapter over Svg.Custom; third-party types do not cross its boundary.</summary>
public sealed class SvgImportAdapter : ISvgImportAdapter
{
    public const int MaximumFileBytes = 5 * 1024 * 1024, MaximumSourceShapes = 5_000;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string SodipodiNamespace = "http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd";
    private static readonly HashSet<string> ShapeNames = ["path", "rect", "circle", "ellipse", "polygon", "polyline"];
    private static readonly HashSet<string> AllowedNames = ["svg", "g", "path", "rect", "circle", "ellipse", "polygon", "polyline", "title", "desc"];
    public SvgImportOutcome Import(string? sourceFileName, ReadOnlyMemory<byte> content, int maximumFlattenedSegments)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName)) return SvgImportOutcome.Failure("Select an SVG file to import.");
        if (content.IsEmpty) return SvgImportOutcome.Failure("The SVG file is empty.");
        if (content.Length > MaximumFileBytes) return SvgImportOutcome.Failure("SVG files must be 5 MB or smaller.");
        if (maximumFlattenedSegments <= 0) return SvgImportOutcome.Failure("The flattened SVG segment limit must be a positive whole number.");
        XDocument xml;
        try { using var s = new MemoryStream(content.ToArray()); using var r = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }); xml = XDocument.Load(r); }
        catch (Exception e) when (e is XmlException or InvalidOperationException) { return SvgImportOutcome.Failure("The file is not valid, safe SVG XML."); }
        var root = xml.Root;
        if (root is null || root.Name.LocalName != "svg") return SvgImportOutcome.Failure("The document must have an svg root element.");
        RemoveNonRenderingMetadata(root);
        if (root.DescendantsAndSelf().Any(Unsafe)) return SvgImportOutcome.Failure("SVG external resources, scripts, entities, and URL references are not allowed.");
        NormalizeInlineFillStyles(root);
        RemoveVendorAttributes(root);
        var unsupported = root.DescendantsAndSelf().FirstOrDefault(x => !AllowedNames.Contains(x.Name.LocalName));
        if (unsupported is not null) return SvgImportOutcome.Failure($"SVG element '{unsupported.Name.LocalName}' is not supported for stencil artwork.");
        if (root.Descendants().Count(x => ShapeNames.Contains(x.Name.LocalName)) > MaximumSourceShapes) return SvgImportOutcome.Failure("SVG artwork cannot contain more than 5,000 source shapes or paths.");
        SvgDocument document;
        try { document = SvgDocument.FromSvg<SvgDocument>(xml.ToString(SaveOptions.DisableFormatting)); }
        catch { return SvgImportOutcome.Failure("The file is not valid SVG for import."); }
        try
        {
            var result = new List<VectorContour>(); var strokeOnly = false; Visit(document, Affine.Identity, SvgFillRule.NonZero, result, ref strokeOnly);
            if (result.Count == 0 && strokeOnly) return SvgImportOutcome.Failure("This artwork uses strokes only. Convert strokes to filled paths, then import it again.");
            // Exact linear contours have scale-independent segment counts and can be rejected immediately.
            if (result.All(c => c.Segments.All(s => s is VectorLineSegment)) && VectorFlattener.Flatten(new VectorArtwork(result), .05, maximumFlattenedSegments).Count >= 0) { }
            return result.Count == 0 ? SvgImportOutcome.Failure("The SVG does not contain usable closed filled contours.") : SvgImportOutcome.Success(new SvgImportResult(sourceFileName, new VectorArtwork(result)));
        }
        catch (SvgFlattenLimitException exception) { return SvgImportOutcome.Failure(exception.Message); }
        catch (FormatException) { return SvgImportOutcome.Failure("The SVG contains malformed or unsupported geometry."); }
    }
    private static void RemoveNonRenderingMetadata(XElement root)
    {
        foreach (var element in root.DescendantsAndSelf().Where(IsNonRenderingMetadata).ToArray())
            element.Remove();
    }

    private static bool IsNonRenderingMetadata(XElement element)
    {
        var namespaceName = element.Name.NamespaceName;
        return element.Name.LocalName == "metadata" && (namespaceName.Length == 0 || namespaceName == SvgNamespace)
            || element.Name.LocalName == "namedview" && namespaceName == SodipodiNamespace;
    }

    private static void RemoveVendorAttributes(XElement root)
    {
        foreach (var attribute in root.DescendantsAndSelf().Attributes()
                     .Where(a => !a.IsNamespaceDeclaration
                         && a.Name.NamespaceName.Length != 0
                         && a.Name.NamespaceName != XNamespace.Xml.NamespaceName)
                     .ToArray())
            attribute.Remove();
    }

    private static void NormalizeInlineFillStyles(XElement root)
    {
        foreach (var attribute in root.DescendantsAndSelf().Attributes().Where(a => a.Name.LocalName.Equals("style", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var declarations = attribute.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (declarations.Length != 1)
                continue;

            var parts = declarations[0].Split(':', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !parts[0].Equals("fill", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(parts[1]))
                continue;

            attribute.Parent!.SetAttributeValue("fill", parts[1]);
            attribute.Remove();
        }
    }

    private static bool Unsafe(XElement element)
    {
        if (element.Name.LocalName is "script" or "style" or "use") return true;
        return element.Attributes().Any(UnsafeAttribute);
    }

    private static bool UnsafeAttribute(XAttribute attribute)
    {
        if (attribute.IsNamespaceDeclaration) return false;

        var name = attribute.Name.LocalName;
        return name.Equals("href", StringComparison.OrdinalIgnoreCase)
            || name.Equals("src", StringComparison.OrdinalIgnoreCase)
            || name.Equals("style", StringComparison.OrdinalIgnoreCase) && !IsSafeInlineFillStyle(attribute.Value)
            || name.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            || attribute.Value.Contains("url(", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeInlineFillStyle(string value)
    {
        var declarations = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (declarations.Length != 1) return false;
        var parts = declarations[0].Split(':', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts[0].Equals("fill", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parts[1]);
    }
    private static void Visit(SvgElement e, Affine parent, SvgFillRule inherited, List<VectorContour> output, ref bool strokeOnly)
    {
        var transform = parent * TransformOf(e.Transforms); var rule = e.FillRule == global::Svg.SvgFillRule.EvenOdd ? SvgFillRule.EvenOdd : inherited;
        if (e is SvgPath or SvgRectangle or SvgCircle or SvgEllipse or SvgPolygon or SvgPolyline) { if (NoPaint(e.Fill)) { strokeOnly |= !NoPaint(e.Stroke); return; } foreach (var c in Shape(e, rule)) output.Add(Transform(c, transform)); return; }
        foreach (var child in e.Children.OfType<SvgElement>()) Visit(child, transform, rule, output, ref strokeOnly);
    }
    private static bool NoPaint(SvgPaintServer? p) => p is null || string.Equals(p.ToString(), "none", StringComparison.OrdinalIgnoreCase);
    private static IEnumerable<VectorContour> Shape(SvgElement e, SvgFillRule rule) => e switch
    {
        SvgPath p => Paths(p.PathData, rule),
        SvgRectangle r => [Polygon([P(r.X.Value,r.Y.Value),P(r.X.Value+r.Width.Value,r.Y.Value),P(r.X.Value+r.Width.Value,r.Y.Value+r.Height.Value),P(r.X.Value,r.Y.Value+r.Height.Value)],rule)],
        SvgCircle c => [Ellipse(c.CenterX.Value,c.CenterY.Value,c.Radius.Value,c.Radius.Value,rule)],
        SvgEllipse x => [Ellipse(x.CenterX.Value,x.CenterY.Value,x.RadiusX.Value,x.RadiusY.Value,rule)],
        SvgPolyline p when p.Points.Count >= 6 && Same(P(p.Points[0].Value,p.Points[1].Value),P(p.Points[^2].Value,p.Points[^1].Value)) => [Polygon(Points(p.Points),rule)],
        SvgPolygon p => [Polygon(Points(p.Points),rule)], _ => throw new FormatException()
    };
    private static IEnumerable<VectorContour> Paths(SvgPathSegmentList path, SvgFillRule rule)
    {
        var all=new List<VectorContour>(); Builder? b=null; var current=new SvgPoint(); SvgPoint? lastC=null,lastQ=null;
        foreach(var s in path) switch(s)
        {
            case SvgMoveToSegment m: if(b?.Closed==true)all.Add(b.Build(rule)); current=Resolve(m.End,m.IsRelative,current);b=new Builder(current);lastC=lastQ=null;break;
            case SvgLineSegment l when b is not null: current=LineEnd(l,current);b.Line(current);lastC=lastQ=null;break;
            case SvgCubicCurveSegment c when b is not null: var c1=float.IsNaN(c.FirstControlPoint.X)?Reflect(lastC,current):Resolve(c.FirstControlPoint,c.IsRelative,current);var c2=Resolve(c.SecondControlPoint,c.IsRelative,current);current=Resolve(c.End,c.IsRelative,current);b.Cubic(c1,c2,current);lastC=c2;lastQ=null;break;
            case SvgQuadraticCurveSegment q when b is not null: var qc=float.IsNaN(q.ControlPoint.X)?Reflect(lastQ,current):Resolve(q.ControlPoint,q.IsRelative,current);var end=Resolve(q.End,q.IsRelative,current);b.Cubic(new(current.X+(qc.X-current.X)*2/3d,current.Y+(qc.Y-current.Y)*2/3d),new(end.X+(qc.X-end.X)*2/3d,end.Y+(qc.Y-end.Y)*2/3d),end);current=end;lastQ=qc;lastC=null;break;
            case SvgArcSegment a when b is not null: var ae=Resolve(a.End,a.IsRelative,current);Arc(b,current,ae,a.RadiusX,a.RadiusY,a.Angle,a.Size==SvgArcSize.Large,a.Sweep==SvgArcSweep.Positive);current=ae;lastC=lastQ=null;break;
            case SvgClosePathSegment when b is not null: b.Close();current=b.Start;lastC=lastQ=null;break;
            default: throw new FormatException();
        }
        if(b?.Closed==true)all.Add(b.Build(rule));return all;
    }
    private static void Arc(Builder b,SvgPoint s,SvgPoint e,double rx,double ry,double deg,bool large,bool sweep)
    { if(Same(s,e))return;rx=Math.Abs(rx);ry=Math.Abs(ry);if(rx<=0||ry<=0){b.Line(e);return;}var p=deg*Math.PI/180;var co=Math.Cos(p);var si=Math.Sin(p);var dx=(s.X-e.X)/2;var dy=(s.Y-e.Y)/2;var x=co*dx+si*dy;var y=-si*dx+co*dy;var l=x*x/(rx*rx)+y*y/(ry*ry);if(l>1){var z=Math.Sqrt(l);rx*=z;ry*=z;}var den=rx*rx*y*y+ry*ry*x*x;var f=den==0?0:(large==sweep?-1:1)*Math.Sqrt(Math.Max(0,rx*rx*ry*ry-rx*rx*y*y-ry*ry*x*x)/den);var cxp=f*rx*y/ry;var cyp=-f*ry*x/rx;var cx=co*cxp-si*cyp+(s.X+e.X)/2;var cy=si*cxp+co*cyp+(s.Y+e.Y)/2;var t=Math.Atan2((y-cyp)/ry,(x-cxp)/rx);var d=Math.Atan2(((x-cxp)/rx)*((-y-cyp)/ry)-((y-cyp)/ry)*((-x-cxp)/rx),((x-cxp)/rx)*((-x-cxp)/rx)+((y-cyp)/ry)*((-y-cyp)/ry));if(!sweep&&d>0)d-=2*Math.PI;if(sweep&&d<0)d+=2*Math.PI;var n=(int)Math.Ceiling(Math.Abs(d)/(Math.PI/2));for(var i=0;i<n;i++){var a=t+d*i/n;var z=t+d*(i+1)/n;var k=4d/3d*Math.Tan((z-a)/4);var p0=EP(cx,cy,rx,ry,co,si,a);var p3=EP(cx,cy,rx,ry,co,si,z);var v0=ED(rx,ry,co,si,a);var v3=ED(rx,ry,co,si,z);b.Cubic(new(p0.X+k*v0.X,p0.Y+k*v0.Y),new(p3.X-k*v3.X,p3.Y-k*v3.Y),p3);}}
    private static SvgPoint EP(double cx,double cy,double rx,double ry,double co,double si,double a)=>new(cx+rx*Math.Cos(a)*co-ry*Math.Sin(a)*si,cy+rx*Math.Cos(a)*si+ry*Math.Sin(a)*co); private static SvgPoint ED(double rx,double ry,double co,double si,double a)=>new(-rx*Math.Sin(a)*co-ry*Math.Cos(a)*si,-rx*Math.Sin(a)*si+ry*Math.Cos(a)*co);
    private static VectorContour Ellipse(double x,double y,double rx,double ry,SvgFillRule r){if(rx<=0||ry<=0)throw new FormatException();var b=new Builder(new(x+rx,y));var k=.5522847498307936;b.Cubic(new(x+rx,y+ry*k),new(x+rx*k,y+ry),new(x,y+ry));b.Cubic(new(x-rx*k,y+ry),new(x-rx,y+ry*k),new(x-rx,y));b.Cubic(new(x-rx,y-ry*k),new(x-rx*k,y-ry),new(x,y-ry));b.Cubic(new(x+rx*k,y-ry),new(x+rx,y-ry*k),new(x+rx,y));b.Close();return b.Build(r);}
    private static VectorContour Polygon(IReadOnlyList<SvgPoint> p,SvgFillRule r){if(p.Count<3)throw new FormatException();var b=new Builder(p[0]);foreach(var x in p.Skip(1))b.Line(x);b.Close();return b.Build(r);}private static IReadOnlyList<SvgPoint> Points(SvgPointCollection p)=>p.Count<6||p.Count%2!=0?throw new FormatException():Enumerable.Range(0,p.Count/2).Select(i=>P(p[i*2].Value,p[i*2+1].Value)).ToArray();
    private static SvgPoint Resolve(PointF p,bool rel,SvgPoint c)=>rel?new(c.X+p.X,c.Y+p.Y):P(p.X,p.Y);private static SvgPoint LineEnd(SvgLineSegment l,SvgPoint c)=>float.IsNaN(l.End.X)?new(c.X,l.IsRelative?c.Y+l.End.Y:l.End.Y):float.IsNaN(l.End.Y)?new(l.IsRelative?c.X+l.End.X:l.End.X,c.Y):Resolve(l.End,l.IsRelative,c);private static SvgPoint Reflect(SvgPoint? p,SvgPoint c)=>p is { } x?new(2*c.X-x.X,2*c.Y-x.Y):c;private static SvgPoint P(double x,double y)=>double.IsFinite(x)&&double.IsFinite(y)?new(x,y):throw new FormatException();private static bool Same(SvgPoint a,SvgPoint b)=>Math.Abs(a.X-b.X)<1e-9&&Math.Abs(a.Y-b.Y)<1e-9;
    private static VectorContour Transform(VectorContour c,Affine m)=>c with {Start=m.Apply(c.Start),Segments=c.Segments.Select(s=>(VectorSegment)(s switch{VectorLineSegment l=>new VectorLineSegment(m.Apply(l.End)),VectorCubicBezierSegment b=>new VectorCubicBezierSegment(m.Apply(b.FirstControlPoint),m.Apply(b.SecondControlPoint),m.Apply(b.End)),_=>throw new InvalidOperationException()})).ToArray()};
    private static Affine TransformOf(SvgTransformCollection? ts){var result=Affine.Identity;if(ts is null)return result;foreach(var t in ts)result=result*(t switch{SvgTranslate x=>new Affine(1,0,0,1,x.X,x.Y),SvgScale x=>new Affine(x.X,0,0,x.Y,0,0),SvgRotate x=>Affine.T(x.CenterX,x.CenterY)*Affine.R(x.Angle)*Affine.T(-x.CenterX,-x.CenterY),SvgMatrix x when x.Points.Count==6=>new Affine(x.Points[0],x.Points[1],x.Points[2],x.Points[3],x.Points[4],x.Points[5]),SvgSkew x=>new Affine(1,Math.Tan(x.AngleY*Math.PI/180),Math.Tan(x.AngleX*Math.PI/180),1,0,0),_=>throw new FormatException()});return result;}
    private sealed class Builder(SvgPoint start){public SvgPoint Start{get;}=start;public bool Closed{get;private set;}private readonly List<VectorSegment> xs=[];public void Line(SvgPoint p){if(!Same(xs.Count==0?Start:xs[^1].End,p))xs.Add(new VectorLineSegment(p));}public void Cubic(SvgPoint a,SvgPoint b,SvgPoint e)=>xs.Add(new VectorCubicBezierSegment(a,b,e));public void Close(){Line(Start);Closed=true;}public VectorContour Build(SvgFillRule r)=>new(Start,xs.ToArray(),r,Closed);}
}
public readonly record struct Affine(double A,double B,double C,double D,double E,double F){public static Affine Identity=>new(1,0,0,1,0,0);public SvgPoint Apply(SvgPoint p)=>new(A*p.X+C*p.Y+E,B*p.X+D*p.Y+F);public static Affine T(double x,double y)=>new(1,0,0,1,x,y);public static Affine R(double d){var r=d*Math.PI/180;return new(Math.Cos(r),Math.Sin(r),-Math.Sin(r),Math.Cos(r),0,0);}public static Affine operator*(Affine x,Affine y)=>new(x.A*y.A+x.C*y.B,x.B*y.A+x.D*y.B,x.A*y.C+x.C*y.D,x.B*y.C+x.D*y.D,x.A*y.E+x.C*y.F+x.E,x.B*y.E+x.D*y.F+x.F);}
public static class VectorFlattener
{const int MaxDepth=20;const double E=.000000001;public static IReadOnlyList<SvgContour> Flatten(VectorArtwork a,double tolerance,int limit){if(tolerance<=0||!double.IsFinite(tolerance))throw new ArgumentOutOfRangeException(nameof(tolerance));var all=new List<SvgContour>();var count=0;foreach(var c in a.Contours.Where(x=>x.IsClosed)){var ps=new List<SvgPoint>{c.Start};var start=c.Start;foreach(var s in c.Segments){if(s is VectorLineSegment){if(Add(ps,s.End)&&++count>limit)throw new SvgFlattenLimitException(limit);}else if(s is VectorCubicBezierSegment b)Flat(ps,start,b.FirstControlPoint,b.SecondControlPoint,b.End,tolerance,0,ref count,limit);start=s.End;}if(ps.Count>2){if(!Same(ps[0],ps[^1])&&Add(ps,ps[0])&&++count>limit)throw new SvgFlattenLimitException(limit);if(ps.Count>3)all.Add(new SvgContour(ps,c.FillRule));}}return all;}static void Flat(List<SvgPoint> p,SvgPoint a,SvgPoint b,SvgPoint c,SvgPoint d,double t,int depth,ref int count,int limit){if(depth>=MaxDepth)throw new InvalidOperationException("SVG curve subdivision exceeded the configured complexity limit.");if(Math.Max(Dist(b,a,d),Dist(c,a,d))<=t){if(Add(p,d)&&++count>limit)throw new SvgFlattenLimitException(limit);return;}var ab=Mid(a,b);var bc=Mid(b,c);var cd=Mid(c,d);var abc=Mid(ab,bc);var bcd=Mid(bc,cd);var m=Mid(abc,bcd);Flat(p,a,ab,abc,m,t,depth+1,ref count,limit);Flat(p,m,bcd,cd,d,t,depth+1,ref count,limit);}static double Dist(SvgPoint p,SvgPoint a,SvgPoint b){var x=b.X-a.X;var y=b.Y-a.Y;var n=Math.Sqrt(x*x+y*y);return n<E?Math.Sqrt((p.X-a.X)*(p.X-a.X)+(p.Y-a.Y)*(p.Y-a.Y)):Math.Abs(y*p.X-x*p.Y+b.X*a.Y-b.Y*a.X)/n;}static SvgPoint Mid(SvgPoint a,SvgPoint b)=>new((a.X+b.X)/2,(a.Y+b.Y)/2);static bool Add(List<SvgPoint> p,SvgPoint x){if(Same(p[^1],x))return false;p.Add(x);return true;}static bool Same(SvgPoint a,SvgPoint b)=>Math.Abs(a.X-b.X)<E&&Math.Abs(a.Y-b.Y)<E;}
public sealed class SvgFlattenLimitException(int limit) : Exception($"SVG artwork exceeds the configured flattened segment limit of {limit:N0}; too many flattened segments were produced. Simplify the artwork or raise the limit.");
