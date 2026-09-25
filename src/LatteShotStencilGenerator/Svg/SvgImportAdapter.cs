using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace LatteShotStencilGenerator.Svg;

public enum SvgFillRule { NonZero, EvenOdd }

public readonly record struct SvgPoint(double X, double Y);

public sealed record SvgContour(IReadOnlyList<SvgPoint> Points, SvgFillRule FillRule);

public sealed record SvgImportResult(string SourceFileName, IReadOnlyList<SvgContour> Contours)
{
    public int FlattenedSegmentCount => Contours.Sum(contour => contour.Points.Count - 1);
}

public sealed record SvgImportError(string Message);

public sealed record SvgImportOutcome(SvgImportResult? Value, SvgImportError? Error)
{
    public bool IsSuccess => Value is not null;
    public static SvgImportOutcome Success(SvgImportResult value) => new(value, null);
    public static SvgImportOutcome Failure(string message) => new(null, new SvgImportError(message));
}

/// <summary>Project-owned boundary for replaceable SVG readers.</summary>
public interface ISvgImportAdapter
{
    SvgImportOutcome Import(string? sourceFileName, ReadOnlyMemory<byte> content, int maximumFlattenedSegments);
}

/// <summary>Reads the deliberately small, fill-only SVG subset used by the stencil engine.</summary>
public sealed class SvgImportAdapter : ISvgImportAdapter
{
    public const int MaximumFileBytes = 5 * 1024 * 1024;
    public const int MaximumSourceShapes = 5_000;
    private static readonly HashSet<string> ShapeNames = ["path", "rect", "circle", "ellipse", "polygon", "polyline"];
    private static readonly HashSet<string> AllowedNames = ["svg", "g", "path", "rect", "circle", "ellipse", "polygon", "polyline", "title", "desc"];
    private const double Epsilon = 0.000001;

    public SvgImportOutcome Import(string? sourceFileName, ReadOnlyMemory<byte> content, int maximumFlattenedSegments)
    {
        if (string.IsNullOrWhiteSpace(sourceFileName)) return SvgImportOutcome.Failure("Select an SVG file to import.");
        if (content.IsEmpty) return SvgImportOutcome.Failure("The SVG file is empty.");
        if (content.Length > MaximumFileBytes) return SvgImportOutcome.Failure("SVG files must be 5 MB or smaller.");
        if (maximumFlattenedSegments <= 0) return SvgImportOutcome.Failure("The flattened SVG segment limit must be a positive whole number.");

        XDocument document;
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (Exception exception) when (exception is XmlException or InvalidOperationException)
        {
            return SvgImportOutcome.Failure("The file is not valid, safe SVG XML.");
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "svg") return SvgImportOutcome.Failure("The document must have an svg root element.");
        if (root.DescendantsAndSelf().Any(HasUnsafeDependency)) return SvgImportOutcome.Failure("SVG external resources, scripts, entities, and URL references are not allowed.");
        var unsupported = root.DescendantsAndSelf().FirstOrDefault(e => !AllowedNames.Contains(e.Name.LocalName));
        if (unsupported is not null) return SvgImportOutcome.Failure($"SVG element '{unsupported.Name.LocalName}' is not supported for stencil artwork.");

        var shapes = document.Descendants().Where(e => ShapeNames.Contains(e.Name.LocalName)).ToArray();
        if (shapes.Length > MaximumSourceShapes) return SvgImportOutcome.Failure("SVG artwork cannot contain more than 5,000 source shapes or paths.");

        var contours = new List<SvgContour>();
        var sawStrokeOnly = false;
        try { Visit(root, Matrix.Identity, SvgFillRule.NonZero, contours, ref sawStrokeOnly, maximumFlattenedSegments); }
        catch (SvgSegmentLimitExceededException)
        {
            return SvgImportOutcome.Failure($"SVG artwork exceeds the configured flattened segment limit of {maximumFlattenedSegments:N0}. Simplify the artwork or raise the limit.");
        }
        catch (FormatException) { return SvgImportOutcome.Failure("The SVG contains malformed geometry or a malformed transform."); }

        if (contours.Count == 0 && sawStrokeOnly)
            return SvgImportOutcome.Failure("This artwork uses strokes only. Convert strokes to filled paths, then import it again.");
        if (contours.Count == 0) return SvgImportOutcome.Failure("The SVG does not contain usable closed filled contours.");
        return SvgImportOutcome.Success(new SvgImportResult(sourceFileName, contours));
    }

    private static bool HasUnsafeDependency(XElement element)
    {
        if (element.Name.LocalName is "script" or "style" or "use") return true;
        return element.Attributes().Any(a =>
            a.Name.LocalName is "href" or "src" or "style" ||
            a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
            a.Value.Contains("url(", StringComparison.OrdinalIgnoreCase) ||
            a.Value.Contains("://", StringComparison.Ordinal));
    }

    private static void Visit(XElement element, Matrix parent, SvgFillRule inheritedRule, List<SvgContour> output, ref bool sawStrokeOnly, int maximumFlattenedSegments)
    {
        var matrix = parent * ParseTransform((string?)element.Attribute("transform"));
        var fillRule = string.Equals((string?)element.Attribute("fill-rule"), "evenodd", StringComparison.OrdinalIgnoreCase) ? SvgFillRule.EvenOdd : inheritedRule;
        if (!ShapeNames.Contains(element.Name.LocalName))
        {
            foreach (var child in element.Elements()) Visit(child, matrix, fillRule, output, ref sawStrokeOnly, maximumFlattenedSegments);
            return;
        }
        var fill = ((string?)element.Attribute("fill"))?.Trim();
        var stroke = ((string?)element.Attribute("stroke"))?.Trim();
        if (string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(fill) && !string.IsNullOrEmpty(stroke))
        {
            sawStrokeOnly |= !string.IsNullOrEmpty(stroke) && !string.Equals(stroke, "none", StringComparison.OrdinalIgnoreCase);
            return;
        }
        foreach (var contour in ShapeContours(element))
        {
            if (element.Name.LocalName == "polyline" && !Same(contour[0], contour[^1])) continue;
            var normalized = Normalize(contour.Select(matrix.Apply).ToList());
            if (normalized.Count >= 4 && Same(normalized[0], normalized[^1]))
            {
                var segmentCount = normalized.Count - 1;
                if (output.Sum(existing => existing.Points.Count - 1) + segmentCount > maximumFlattenedSegments)
                    throw new SvgSegmentLimitExceededException();
                output.Add(new SvgContour(normalized, fillRule));
            }
        }
    }

    private sealed class SvgSegmentLimitExceededException : Exception;

    private static List<List<SvgPoint>> ShapeContours(XElement element) => element.Name.LocalName switch
    {
        "rect" => [Rectangle(N(element, "x", 0), N(element, "y", 0), N(element, "width"), N(element, "height"))],
        "circle" => [Ellipse(N(element, "cx", 0), N(element, "cy", 0), N(element, "r"), N(element, "r"))],
        "ellipse" => [Ellipse(N(element, "cx", 0), N(element, "cy", 0), N(element, "rx"), N(element, "ry"))],
        "polygon" or "polyline" => [Points((string?)element.Attribute("points") ?? throw new FormatException())],
        "path" => Path((string?)element.Attribute("d") ?? throw new FormatException()),
        _ => throw new FormatException()
    };

    private static double N(XElement e, string name, double fallback = double.NaN)
    {
        var value = (string?)e.Attribute(name);
        if (value is null && !double.IsNaN(fallback)) return fallback;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : throw new FormatException();
    }

    private static List<SvgPoint> Rectangle(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) throw new FormatException();
        return [new(x,y), new(x+width,y), new(x+width,y+height), new(x,y+height), new(x,y)];
    }
    private static List<SvgPoint> Ellipse(double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) throw new FormatException();
        var points = new List<SvgPoint>(); for (var i=0;i<=32;i++) { var a = 2 * Math.PI * i / 32; points.Add(new(cx + rx*Math.Cos(a), cy + ry*Math.Sin(a))); } return points;
    }
    private static List<SvgPoint> Points(string value)
    {
        var numbers = Numbers(value).ToArray(); if (numbers.Length < 4 || numbers.Length % 2 != 0) throw new FormatException();
        return numbers.Chunk(2).Select(n => new SvgPoint(n[0], n[1])).ToList();
    }
    private static List<SvgPoint> Normalize(List<SvgPoint> points)
    {
        var result = new List<SvgPoint>(); foreach (var point in points) if (result.Count == 0 || !Same(result[^1], point)) result.Add(point);
        if (result.Count > 1 && !Same(result[0], result[^1])) result.Add(result[0]); return result;
    }
    private static bool Same(SvgPoint a, SvgPoint b) => Math.Abs(a.X-b.X) < Epsilon && Math.Abs(a.Y-b.Y) < Epsilon;

    private static List<List<SvgPoint>> Path(string d)
    {
        var tokens = new PathTokens(d); var contours = new List<List<SvgPoint>>(); List<SvgPoint>? current = null; var p = new SvgPoint(); var start = new SvgPoint(); char command = '\0';
        while (tokens.More)
        {
            if (tokens.IsCommand) command = tokens.Command(); else if (command == '\0') throw new FormatException();
            var relative = char.IsLower(command); var op = char.ToUpperInvariant(command);
            SvgPoint Point() { var x=tokens.Number(); var y=tokens.Number(); return relative ? new(p.X+x,p.Y+y) : new(x,y); }
            if (op == 'M') { p=Point(); start=p; current=[p]; contours.Add(current); command=relative?'l':'L'; }
            else if (op == 'L') { if (current is null) throw new FormatException(); p=Point(); current.Add(p); }
            else if (op == 'H') { if (current is null) throw new FormatException(); var x=tokens.Number(); p=relative?new(p.X+x,p.Y):new(x,p.Y); current.Add(p); }
            else if (op == 'V') { if (current is null) throw new FormatException(); var y=tokens.Number(); p=relative?new(p.X,p.Y+y):new(p.X,y); current.Add(p); }
            else if (op == 'Z') { if (current is null) throw new FormatException(); current.Add(start); p=start; command='\0'; }
            else throw new FormatException();
        }
        return contours;
    }

    private static IEnumerable<double> Numbers(string text)
    {
        var index=0; while(index<text.Length) { while(index<text.Length && (char.IsWhiteSpace(text[index]) || text[index]==',')) index++; if(index==text.Length) yield break; var start=index; if(text[index]=='+'||text[index]=='-') index++; while(index<text.Length && char.IsDigit(text[index])) index++; if(index<text.Length&&text[index]=='.'){index++;while(index<text.Length&&char.IsDigit(text[index]))index++;} if(index<text.Length&&(text[index]=='e'||text[index]=='E')){index++;if(index<text.Length&&(text[index]=='+'||text[index]=='-'))index++;while(index<text.Length&&char.IsDigit(text[index]))index++;} if(start==index||!double.TryParse(text[start..index],NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value))throw new FormatException(); yield return value; }
    }

    private readonly record struct Matrix(double A,double B,double C,double D,double E,double F)
    {
        public static Matrix Identity => new(1,0,0,1,0,0);
        public SvgPoint Apply(SvgPoint p) => new(A*p.X+C*p.Y+E, B*p.X+D*p.Y+F);
        public static Matrix operator *(Matrix x, Matrix y) => new(x.A*y.A+x.C*y.B,x.B*y.A+x.D*y.B,x.A*y.C+x.C*y.D,x.B*y.C+x.D*y.D,x.A*y.E+x.C*y.F+x.E,x.B*y.E+x.D*y.F+x.F);
    }
    private static Matrix ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Matrix.Identity; var result=Matrix.Identity; var rest=value.AsSpan().Trim();
        while(!rest.IsEmpty) { var open=rest.IndexOf('('); var close=rest.IndexOf(')'); if(open<=0||close<open)throw new FormatException(); var name=rest[..open].ToString().Trim(); var nums=Numbers(rest[(open+1)..close].ToString()).ToArray(); Matrix m = name switch { "translate" when nums.Length is 1 or 2 => new(1,0,0,1,nums[0],nums.Length==2?nums[1]:0), "scale" when nums.Length is 1 or 2 => new(nums[0],0,0,nums.Length==2?nums[1]:nums[0],0,0), "rotate" when nums.Length is 1 => Rotate(nums[0],0,0), "rotate" when nums.Length==3 => Rotate(nums[0],nums[1],nums[2]), "matrix" when nums.Length==6 => new(nums[0],nums[1],nums[2],nums[3],nums[4],nums[5]), _ => throw new FormatException() }; result=result*m; rest=rest[(close+1)..].TrimStart(); }
        return result;
    }
    private static Matrix Rotate(double degrees,double x,double y) { var r=degrees*Math.PI/180; var c=Math.Cos(r); var s=Math.Sin(r); return new Matrix(1,0,0,1,x,y)*new Matrix(c,s,-s,c,0,0)*new Matrix(1,0,0,1,-x,-y); }
    private sealed class PathTokens(string input)
    {
        private int _i; public bool More { get { Skip(); return _i<input.Length; } } public bool IsCommand { get { Skip(); return _i<input.Length && char.IsLetter(input[_i]); } }
        public char Command(){ Skip(); return input[_i++]; }
        public double Number(){ Skip(); var start=_i; if(_i<input.Length&&(input[_i]=='+'||input[_i]=='-'))_i++;while(_i<input.Length&&char.IsDigit(input[_i]))_i++;if(_i<input.Length&&input[_i]=='.'){_i++;while(_i<input.Length&&char.IsDigit(input[_i]))_i++;}if(_i<input.Length&&(input[_i]=='e'||input[_i]=='E')){_i++;if(_i<input.Length&&(input[_i]=='+'||input[_i]=='-'))_i++;while(_i<input.Length&&char.IsDigit(input[_i]))_i++;} if(start==_i||!double.TryParse(input[start.._i],NumberStyles.Float,CultureInfo.InvariantCulture,out var n)||!double.IsFinite(n))throw new FormatException(); return n; }
        private void Skip(){while(_i<input.Length&&(char.IsWhiteSpace(input[_i])||input[_i]==','))_i++;}
    }
}
