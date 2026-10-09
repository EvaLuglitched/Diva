using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedural tileable textures for the town: a near-white detail albedo (tinted by each material's
/// colour) plus a matching normal map. Deterministic; regenerated only when missing.
/// One texture tile covers TileMetres metres, so materials set their tiling from it.
/// </summary>
public static class DivaTextures
{
    public const string Folder = "Assets/DivaGame/Textures";
    const int Size = 512;

    public enum Kind { Plaster, Brick, RoofTile, Pavers, Asphalt, MetalPanel, Wood, Concrete, Hazard }

    public static float TileMetres(Kind kind) => kind switch
    {
        Kind.Brick => 2, Kind.RoofTile => 2, Kind.Pavers => 3, Kind.MetalPanel => 3, Kind.Wood => 2,
        Kind.Hazard => 1, Kind.Asphalt => 6, Kind.Concrete => 4, _ => 4,
    };

    public static (Texture2D albedo, Texture2D normal) Get(Kind kind)
    {
        Directory.CreateDirectory(Folder);
        string a = $"{Folder}/{kind}_Albedo.png", n = $"{Folder}/{kind}_Normal.png";
        if (!File.Exists(a) || !File.Exists(n))
        {
            var (albedo, height) = Generate(kind);
            File.WriteAllBytes(a, ToPng(albedo, true));
            File.WriteAllBytes(n, ToPng(NormalFrom(height, kind == Kind.Asphalt ? 1.2f : 3f), false));
            AssetDatabase.ImportAsset(a);
            AssetDatabase.ImportAsset(n);
            var ni = (TextureImporter)AssetImporter.GetAtPath(n);
            ni.textureType = TextureImporterType.NormalMap;
            ni.SaveAndReimport();
        }
        return (AssetDatabase.LoadAssetAtPath<Texture2D>(a), AssetDatabase.LoadAssetAtPath<Texture2D>(n));
    }

    // ---------- Noise ----------

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }
    }

    /// <summary>Tileable value noise with `cells` cells across the texture.</summary>
    static float Noise(float u, float v, int cells, int seed)
    {
        float x = u * cells, y = v * cells;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int W(int i) => ((i % cells) + cells) % cells;
        float a = Hash(W(x0), W(y0), seed), b = Hash(W(x0 + 1), W(y0), seed);
        float c = Hash(W(x0), W(y0 + 1), seed), d = Hash(W(x0 + 1), W(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    static float Fbm(float u, float v, int cells, int seed, int octaves = 4)
    {
        float sum = 0, amp = .5f;
        for (int o = 0; o < octaves; o++) { sum += Noise(u, v, cells << o, seed + o * 17) * amp; amp *= .5f; }
        return sum;
    }

    // ---------- Patterns: return (albedo grey 0..1, height 0..1) per pixel ----------

    static (Color[] albedo, float[] height) Generate(Kind kind)
    {
        var albedo = new Color[Size * Size];
        var height = new float[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float u = (x + .5f) / Size, v = (y + .5f) / Size;
            float grain = Fbm(u, v, 8, (int)kind);
            float g, h;
            Color tint = Color.white;
            switch (kind)
            {
                case Kind.Plaster:
                    g = .86f + grain * .16f - Mathf.Pow(Fbm(u, v, 4, 91), 3) * .12f;
                    h = grain;
                    break;
                case Kind.Brick:
                {
                    const int rows = 8, cols = 4;
                    float ry = v * rows; int row = Mathf.FloorToInt(ry);
                    float rx = u * cols + (row % 2) * .5f; int col = Mathf.FloorToInt(rx);
                    float fx = rx - col, fy = ry - row;
                    bool mortar = fx < .04f || fx > .96f || fy < .08f || fy > .92f;
                    float brick = .72f + Hash(((col % cols) + cols) % cols, row, 5) * .22f;
                    g = mortar ? .97f : brick + grain * .1f;
                    h = mortar ? .1f : .75f + grain * .25f;
                    break;
                }
                case Kind.RoofTile:
                {
                    const int rows = 8, cols = 6;
                    float ry = v * rows; int row = Mathf.FloorToInt(ry);
                    float rx = u * cols + (row % 2) * .5f; int col = Mathf.FloorToInt(rx);
                    float fx = rx - col - .5f, fy = ry - row;
                    float edge = .18f + Mathf.Sqrt(Mathf.Max(0, .25f - fx * fx)) * .5f;  // rounded tile bottoms
                    bool gap = fy < edge * .35f;
                    float shade = .7f + fy * .25f + Hash(((col % cols) + cols) % cols, row, 7) * .12f;
                    g = gap ? .45f : shade + grain * .08f;
                    h = gap ? 0 : .3f + fy * .7f;
                    break;
                }
                case Kind.Pavers:
                {
                    const int cells = 6;
                    float px = u * cells, py = v * cells;
                    int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
                    // Jittered stones: nearest of the 3x3 neighbouring seeds (tileable Voronoi).
                    float best = 9, second = 9; int id = 0;
                    for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int gx = cx + i, gy = cy + j, wx = ((gx % cells) + cells) % cells, wy = ((gy % cells) + cells) % cells;
                        float sx = gx + .2f + Hash(wx, wy, 11) * .6f, sy = gy + .2f + Hash(wx, wy, 12) * .6f;
                        float d = (px - sx) * (px - sx) + (py - sy) * (py - sy);
                        if (d < best) { second = best; best = d; id = wx * 31 + wy; } else if (d < second) second = d;
                    }
                    float edgeDist = Mathf.Sqrt(second) - Mathf.Sqrt(best);
                    bool joint = edgeDist < .07f;
                    g = joint ? .55f : .82f + Hash(id, 1, 13) * .14f + grain * .08f;
                    h = joint ? 0 : Mathf.Clamp01(edgeDist * 3) * .8f + grain * .2f;
                    break;
                }
                case Kind.Asphalt:
                    g = .8f + grain * .2f + (Hash(x, y, 3) > .985f ? .15f : 0) - (Hash(x, y, 4) > .99f ? .2f : 0);
                    h = grain;
                    break;
                case Kind.MetalPanel:
                {
                    float px = u * 2 % 1, py = v * 2 % 1;
                    bool seam = px < .012f || py < .012f;
                    bool rivet = (Mathf.Abs(px - .05f) < .012f || Mathf.Abs(px - .95f) < .012f) && (Mathf.Abs(py - .05f) < .012f || Mathf.Abs(py - .95f) < .012f);
                    g = seam ? .55f : .86f + Fbm(u, v, 16, 21, 3) * .1f + (rivet ? .1f : 0);
                    h = seam ? 0 : rivet ? 1 : .6f;
                    break;
                }
                case Kind.Wood:
                {
                    const int planks = 6;
                    float px = u * planks; int p = Mathf.FloorToInt(px);
                    float f = px - p;
                    float stripes = Mathf.Sin((v * 40 + Noise(u, v, 4, 31 + p) * 6) * Mathf.PI) * .5f + .5f;
                    g = (f < .04f ? .5f : .72f + stripes * .12f + Hash(p, 0, 33) * .14f);
                    h = f < .04f ? 0 : .6f + stripes * .2f;
                    break;
                }
                case Kind.Concrete:
                    g = .82f + grain * .14f - (Mathf.Abs(u - .5f) < .004f || Mathf.Abs(v - .5f) < .004f ? .2f : 0);
                    h = grain;
                    break;
                default: // Hazard: diagonal candy-cane stripes
                {
                    bool yellow = ((u + v) * 4 % 1) < .5f;
                    tint = yellow ? new Color(1, .42f, .7f) : new Color(1, .97f, .99f);  // candy-cane pink and white
                    g = .88f + grain * .12f;
                    h = yellow ? .6f : .5f;
                    break;
                }
            }
            albedo[y * Size + x] = new Color(tint.r * g, tint.g * g, tint.b * g, 1);
            height[y * Size + x] = h;
        }
        return (albedo, height);
    }

    static Color[] NormalFrom(float[] height, float strength)
    {
        var n = new Color[Size * Size];
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            float H(int i, int j) => height[((j + Size) % Size) * Size + (i + Size) % Size];
            float dx = (H(x + 1, y) - H(x - 1, y)) * strength, dy = (H(x, y + 1) - H(x, y - 1)) * strength;
            var normal = new Vector3(-dx, -dy, 1).normalized;
            n[y * Size + x] = new Color(normal.x * .5f + .5f, normal.y * .5f + .5f, normal.z * .5f + .5f, 1);
        }
        return n;
    }

    static byte[] ToPng(Color[] pixels, bool srgb)
    {
        var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !srgb);
        t.SetPixels(pixels);
        t.Apply();
        var png = t.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(t);
        return png;
    }
}
