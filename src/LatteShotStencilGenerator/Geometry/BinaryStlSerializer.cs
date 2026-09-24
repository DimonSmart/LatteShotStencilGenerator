using System.Text;

namespace LatteShotStencilGenerator.Geometry;

public static class BinaryStlSerializer
{
    public const int HeaderLength = 80;
    public const int TriangleRecordLength = 50;

    public static byte[] Serialize(Mesh mesh, string header = "Latte Card Generator")
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var validation = MeshValidator.Validate(mesh);
        if (!validation.IsValid) throw new ArgumentException(validation.Message, nameof(mesh));

        using var stream = new MemoryStream(HeaderLength + sizeof(uint) + mesh.Triangles.Count * TriangleRecordLength);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var headerBytes = Encoding.ASCII.GetBytes(header);
        writer.Write(headerBytes, 0, Math.Min(headerBytes.Length, HeaderLength));
        writer.Write(new byte[HeaderLength - Math.Min(headerBytes.Length, HeaderLength)]);
        writer.Write((uint)mesh.Triangles.Count);
        foreach (var triangle in mesh.Triangles)
        {
            WriteVector(writer, triangle.Normal);
            WriteVector(writer, triangle.A);
            WriteVector(writer, triangle.B);
            WriteVector(writer, triangle.C);
            writer.Write((ushort)0);
        }
        return stream.ToArray();
    }

    private static void WriteVector(BinaryWriter writer, System.Numerics.Vector3 vector)
    {
        writer.Write(vector.X); writer.Write(vector.Y); writer.Write(vector.Z);
    }
}
