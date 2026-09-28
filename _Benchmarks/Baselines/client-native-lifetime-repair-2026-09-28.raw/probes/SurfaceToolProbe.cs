// Compiles the actual pinned/patched GodotMeshGeometry.cs against deterministic
// test doubles. This tests C# ownership on return/throw, not Godot rendering.
using System;
using System.Collections.Generic;
using UltraSim.Client.Godot;

internal static class Program
{
    private static int Main()
    {
        int passed = 0, failed = 0;
        foreach (string factory in new[] { "box", "wedge" })
        foreach (string boundary in new[] { "success", "Begin", "SetMaterial", "AddVertex", "Commit" })
        {
            Godot.SurfaceTool.Reset(boundary);
            var borrowedMaterial = new Godot.Material();
            Godot.ArrayMesh? result = null;
            Exception? observed = null;
            try
            {
                result = factory == "box"
                    ? GodotMeshGeometry.CreateGroundedBox(new(1, 2, 3), borrowedMaterial)
                    : GodotMeshGeometry.CreateGroundedPointerWedge(1, 2, 3, 4, borrowedMaterial);
            }
            catch (Exception error) { observed = error; }
            string? errorMessage = null;
            if (Godot.SurfaceTool.Instances.Count != 1 || Godot.SurfaceTool.Instances[0].Disposals != 1)
                errorMessage = "SurfaceTool was not disposed exactly once";
            else if (borrowedMaterial.Disposals != 0)
                errorMessage = "borrowed material was disposed";
            else if (boundary != "success" && !ReferenceEquals(observed, Godot.SurfaceTool.Failure))
                errorMessage = "original injected failure did not propagate";
            else if (boundary == "success" && (observed != null || result == null || result.Disposals != 0 ||
                     result.VertexCount != (factory == "box" ? 36 : 24)))
                errorMessage = "returned mesh ownership or geometry count changed";
            if (errorMessage == null) { passed++; Console.WriteLine($"PASS {factory}/{boundary}"); }
            else { failed++; Console.WriteLine($"FAIL {factory}/{boundary}: {errorMessage}"); }
            result?.Dispose();
            borrowedMaterial.Dispose();
        }
        Console.WriteLine($"{passed}/10 passed; {failed}/10 failed (test doubles; no native rendering)");
        return failed == 0 ? 0 : 1;
    }
}

namespace Godot
{
    public readonly record struct Vector3(float X, float Y, float Z)
    {
        public bool IsFinite() => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X-b.X, a.Y-b.Y, a.Z-b.Z);
        public Vector3 Cross(Vector3 b) => new(Y*b.Z-Z*b.Y, Z*b.X-X*b.Z, X*b.Y-Y*b.X);
        public Vector3 Normalized()
        {
            float length = MathF.Sqrt(X*X+Y*Y+Z*Z);
            return new(X/length, Y/length, Z/length);
        }
    }
    public class Material : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }
    public class Mesh { public enum PrimitiveType { Triangles } }
    public sealed class ArrayMesh : Mesh, IDisposable
    {
        public int VertexCount, Disposals;
        public void Dispose() => Disposals++;
    }
    public sealed class SurfaceTool : IDisposable
    {
        public static readonly List<SurfaceTool> Instances = new();
        public static readonly Exception Failure = new InvalidOperationException("injected native-boundary failure");
        private static string _boundary = "success";
        private int _vertices;
        public int Disposals;
        public SurfaceTool() => Instances.Add(this);
        public static void Reset(string boundary) { Instances.Clear(); _boundary = boundary; }
        private static void Check(string boundary) { if (_boundary == boundary) throw Failure; }
        public void Begin(Mesh.PrimitiveType type) => Check(nameof(Begin));
        public void SetMaterial(Material material) => Check(nameof(SetMaterial));
        public void SetNormal(Vector3 normal) { }
        public void AddVertex(Vector3 vertex) { Check(nameof(AddVertex)); _vertices++; }
        public ArrayMesh Commit() { Check(nameof(Commit)); return new() { VertexCount = _vertices }; }
        public void Dispose() => Disposals++;
    }
}
