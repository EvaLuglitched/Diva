using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Procedural meshes for the cute fantasy layer, saved as assets so scenes can reference them.</summary>
public static class DivaMeshes
{
    const string Folder = "Assets/DivaGame/Meshes";

    static Mesh Save(string name, Mesh mesh)
    {
        Directory.CreateDirectory(Folder);
        string path = Folder + "/" + name + ".asset";
        mesh.name = name;
        mesh.RecalculateBounds();
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing)
        {
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>Box of the given size, centred on the origin, with UVs in metres so tiled textures keep their scale.</summary>
    public static Mesh MetricBox(Vector3 size)
    {
        string name = $"Diva Box {size.x:0.##}x{size.y:0.##}x{size.z:0.##}";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/" + name + ".asset");
        if (existing) return existing;
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
        Vector3 h = size / 2;
        var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
        for (int a = 0; a < 3; a++)
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 normal = axes[a] * s, u = axes[(a + 1) % 3], w = axes[(a + 2) % 3];
            float hu = Vector3.Dot(h, u), hw = Vector3.Dot(h, w), hn = Vector3.Dot(h, axes[a]);
            int b = v.Count;
            foreach (var (du, dw) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) })
            {
                Vector3 p = normal * hn + u * du * hu + w * dw * hw;
                v.Add(p); n.Add(normal);
                uv.Add(new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, w)));
            }
            // Unity treats Cross(b - a, c - a) as the front: keep it pointing outwards.
            bool flip = Vector3.Dot(Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]), normal) < 0;
            if (flip) t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            else t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
        }
        var mesh = new Mesh();
        mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateTangents();
        return Save(name, mesh);
    }
}
