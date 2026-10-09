using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates the night-in-space sky: a 4096x2048 latitude-longitude panorama with a deep indigo-to-violet
/// gradient, a soft pink/cyan nebula, a starry band, thousands of stars, cute four-point sparkle stars, a candy
/// ringed planet and a small moon. Deterministic (fixed seed); written once to Textures/, delete it to regenerate.
/// </summary>
public static class DivaSpaceSky
{
    public const string TexturePath = "Assets/DivaGame/Textures/Diva Space Sky.png";
    const int W = 4096, H = 2048, NW = 1024, NH = 512;

    /// <summary>The horizon colour; the scene fog uses it too, so far buildings fade into the sky.</summary>
    public static readonly Color Horizon = new Color(.44f, .29f, .6f);
    static readonly Color Zenith = new Color(.03f, .02f, .09f);
    static readonly Color Mid = new Color(.12f, .06f, .26f);
    static readonly Color Glow = new Color(.82f, .45f, .78f);   // thin candy glow right at the horizon
    static readonly Color Below = new Color(.16f, .1f, .24f);

    public static Texture2D Get()
    {
        if (!File.Exists(TexturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            var t = new Texture2D(W, H, TextureFormat.RGB24, false);
            t.SetPixels(Generate());
            t.Apply();
            File.WriteAllBytes(TexturePath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(TexturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.mipmapEnabled = false;   // mips make a visible seam where the panorama wraps
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;   // keeps small stars crisp
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    // ---------- Panorama ----------

    static Vector3 Dir(float u, float v)
    {
        // Unity's lat-long mapping: u = 0..1 around, v = 0 (down) .. 1 (up).
        float lon = (u - .5f) * 2 * Mathf.PI, lat = (v - .5f) * Mathf.PI;
        return new Vector3(Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Cos(lon));
    }

    static Vector2 UV(Vector3 d)
    {
        d.Normalize();
        return new Vector2(Mathf.Atan2(d.x, d.z) / (2 * Mathf.PI) + .5f, Mathf.Asin(Mathf.Clamp(d.y, -1, 1)) / Mathf.PI + .5f);
    }

    static Color[] Generate()
    {
        var px = new Color[W * H];
        // A starry band (a candy Milky Way) along a tilted great circle.
        Vector3 bandNormal = Quaternion.Euler(28, 0, 22) * Vector3.up;

        // Nebula at quarter resolution, sampled on the sphere so it wraps seamlessly.
        var neb = new Color[NW * NH];
        for (int y = 0; y < NH; y++)
            for (int x = 0; x < NW; x++)
            {
                Vector3 d = Dir((x + .5f) / NW, (y + .5f) / NH);
                float band = Mathf.Exp(-Mathf.Pow(Vector3.Dot(d, bandNormal) / .32f, 2));
                float n1 = Fbm(d * 2.2f, 5, 11), n2 = Fbm(d * 3.1f + Vector3.one * 7, 4, 23);
                float cloud = Mathf.Clamp01((n1 - .45f) * 2.4f) * (.35f + .65f * band);
                float wisps = Mathf.Clamp01((n2 - .5f) * 3f) * band;
                Color pink = new Color(.95f, .32f, .72f), cyan = new Color(.25f, .75f, 1), violet = new Color(.5f, .3f, .95f);
                Color c = Color.Lerp(violet, pink, Mathf.Clamp01(n2 * 1.6f - .3f)) * cloud * .55f + cyan * wisps * .28f;
                float up = Mathf.Clamp01(d.y * 3 + .2f);   // nebula fades out into the horizon glow
                c.a = band;
                neb[y * NW + x] = c * new Color(up, up, up, 1);
            }

        for (int y = 0; y < H; y++)
        {
            float v = (y + .5f) / H, h = Mathf.Sin((v - .5f) * Mathf.PI);   // sine of elevation
            Color sky;
            if (h >= 0)
            {
                sky = Color.Lerp(Horizon, Mid, Mathf.Pow(Mathf.Clamp01(h / .35f), .7f));
                sky = Color.Lerp(sky, Zenith, Mathf.Clamp01((h - .3f) / .7f));
                sky += Glow * .35f * Mathf.Exp(-h / .035f);
            }
            else sky = Color.Lerp(Horizon, Below, Mathf.Clamp01(-h / .15f));
            for (int x = 0; x < W; x++)
            {
                Color n = SampleNebula(neb, (x + .5f) / W, v);
                px[y * W + x] = sky + new Color(n.r, n.g, n.b);
            }
        }

        uint seed = 20261008;
        float Rand() { seed = unchecked(seed * 1664525u + 1013904223u); return (seed >> 8) / 16777216f; }
        Vector3 RandomDir()
        {
            float z = Rand() * 2 - 1, a = Rand() * 2 * Mathf.PI, r = Mathf.Sqrt(1 - z * z);
            return new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
        }
        Color[] tints = { Color.white, new Color(.8f, .9f, 1), new Color(1, .85f, .95f), new Color(1, .95f, .8f), new Color(.8f, 1, .95f) };

        // Field stars, denser in the band; none below the horizon.
        for (int i = 0; i < 9000; i++)
        {
            Vector3 d = RandomDir();
            float band = Mathf.Exp(-Mathf.Pow(Vector3.Dot(d, bandNormal) / .25f, 2));
            if (d.y < .02f || (band < .3f && Rand() < .45f)) continue;
            float b = Mathf.Pow(Rand(), 3.2f);
            float fade = Mathf.Clamp01((d.y - .02f) / .12f);
            Star(px, d, .55f + b * 1.3f, tints[(int)(Rand() * tints.Length) % tints.Length] * ((.35f + b * .9f) * fade));
        }
        // Extra faint dust in the band.
        for (int i = 0; i < 14000; i++)
        {
            Vector3 d = RandomDir();
            if (Mathf.Abs(Vector3.Dot(d, bandNormal)) > .2f * Rand() + .05f || d.y < .05f) continue;
            Star(px, d, .5f, new Color(.85f, .8f, 1) * (.18f + Rand() * .25f));
        }
        // Cute four-point sparkles.
        Color[] sparkle = { new Color(1, .75f, .92f), new Color(.7f, .95f, 1), new Color(1, .95f, .7f), Color.white };
        for (int i = 0; i < 46; i++)
        {
            Vector3 d = RandomDir();
            if (d.y < .12f) { i--; continue; }
            Sparkle(px, d, 1.2f + Rand() * 1.4f, 7 + Rand() * 12, sparkle[i % sparkle.Length] * (.9f + Rand() * .4f));
        }

        Planet(px, Quaternion.Euler(-24, 35, 0) * Vector3.forward, 6.5f);
        Moon(px, Quaternion.Euler(-40, 215, 0) * Vector3.forward, 2.6f);
        return px;
    }

    static Color SampleNebula(Color[] neb, float u, float v)
    {
        float fx = u * NW - .5f, fy = Mathf.Clamp(v * NH - .5f, 0, NH - 1.001f);
        int x0 = Mathf.FloorToInt(fx), y0 = (int)fy;
        float tx = fx - x0, ty = fy - y0;
        int xa = (x0 % NW + NW) % NW, xb = (xa + 1) % NW;
        Color a = Color.Lerp(neb[y0 * NW + xa], neb[y0 * NW + xb], tx);
        Color b = Color.Lerp(neb[(y0 + 1) * NW + xa], neb[(y0 + 1) * NW + xb], tx);
        return Color.Lerp(a, b, ty);
    }

    // ---------- Stars ----------

    /// <summary>Adds a value at pixel offset (dx, dy) from a direction, wrapping around in longitude.</summary>
    static void Add(Color[] px, int cx, int cy, int dx, int dy, Color c)
    {
        int y = cy + dy;
        if (y < 0 || y >= H) return;
        int x = ((cx + dx) % W + W) % W;
        px[y * W + x] += c;
    }

    static void Star(Color[] px, Vector3 d, float radius, Color c)
    {
        Vector2 uv = UV(d);
        int cx = (int)(uv.x * W), cy = (int)(uv.y * H);
        float stretch = 1 / Mathf.Max(.15f, Mathf.Cos((uv.y - .5f) * Mathf.PI));   // lat-long widens near the poles
        int ry = Mathf.CeilToInt(radius * 2.5f), rx = Mathf.CeilToInt(radius * 2.5f * stretch);
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
            {
                float ex = dx / stretch, r2 = (ex * ex + dy * dy) / (radius * radius);
                if (r2 < 6.25f) Add(px, cx, cy, dx, dy, c * Mathf.Exp(-r2 * 1.4f));
            }
    }

    static void Sparkle(Color[] px, Vector3 d, float core, float ray, Color c)
    {
        Vector2 uv = UV(d);
        int cx = (int)(uv.x * W), cy = (int)(uv.y * H);
        float stretch = 1 / Mathf.Max(.15f, Mathf.Cos((uv.y - .5f) * Mathf.PI));
        int ry = Mathf.CeilToInt(ray * 1.6f), rx = Mathf.CeilToInt(ray * 1.6f * stretch);
        for (int dy = -ry; dy <= ry; dy++)
            for (int dx = -rx; dx <= rx; dx++)
            {
                float ex = Mathf.Abs(dx / stretch), ey = Mathf.Abs(dy);
                float centre = Mathf.Exp(-(ex * ex + ey * ey) / (core * core));
                float rays = Mathf.Max(Mathf.Exp(-ey / .55f) * Mathf.Exp(-ex / ray * 2.2f), Mathf.Exp(-ex / .55f) * Mathf.Exp(-ey / ray * 2.2f));
                float halo = .25f * Mathf.Exp(-(ex * ex + ey * ey) / (core * core * 9));
                float v = centre + rays * .85f + halo;
                if (v > .004f) Add(px, cx, cy, dx, dy, c * v);
            }
    }

    // ---------- Planet and moon (drawn in a flat projection around their centre, so they stay round) ----------

    static void Around(Vector3 centre, float angularRadius, System.Action<int, int, float, float> pixel)
    {
        Vector3 c = centre.normalized;
        Vector3 right = Vector3.Cross(Vector3.up, c).normalized, up = Vector3.Cross(c, right);
        float rho = Mathf.Tan(angularRadius * Mathf.Deg2Rad);
        Vector2 uv = UV(c);
        float extent = angularRadius * 3.2f / 180f;   // generous box in uv
        int y0 = Mathf.Max(0, (int)((uv.y - extent) * H)), y1 = Mathf.Min(H - 1, (int)((uv.y + extent) * H));
        float stretch = 1 / Mathf.Max(.15f, Mathf.Cos((uv.y - .5f) * Mathf.PI));
        int xr = (int)(extent * .5f * W * stretch);
        int cx = (int)(uv.x * W);
        for (int y = y0; y <= y1; y++)
            for (int dx = -xr; dx <= xr; dx++)
            {
                int x = ((cx + dx) % W + W) % W;
                Vector3 d = Dir((x + .5f) / W, (y + .5f) / H);
                float dc = Vector3.Dot(d, c);
                if (dc <= 0) continue;
                pixel(x, y, Vector3.Dot(d, right) / dc / rho, Vector3.Dot(d, up) / dc / rho);
            }
    }

    static void Planet(Color[] px, Vector3 centre, float angularRadius)
    {
        Vector3 light = new Vector3(-.55f, .45f, .7f).normalized;
        float tilt = -18 * Mathf.Deg2Rad, ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
        Color ringA = new Color(1, .85f, .6f), ringB = new Color(.75f, .95f, 1);
        Around(centre, angularRadius, (x, y, sx, sy) =>
        {
            int i = y * W + x;
            float r = Mathf.Sqrt(sx * sx + sy * sy);
            // Ring in the planet's tilted frame: an ellipse; its far half is hidden behind the planet.
            float rx = sx * ct - sy * st, ry = sx * st + sy * ct;
            float e = Mathf.Sqrt(rx * rx + (ry / .28f) * (ry / .28f));
            float ringMask = Mathf.Clamp01((e - 1.3f) * 30) * Mathf.Clamp01((2.15f - e) * 30) * (.55f + .45f * Mathf.Sin(e * 38));
            Color ring = Color.Lerp(ringA, ringB, Mathf.Clamp01((e - 1.3f) / .85f));
            bool front = ry < 0;
            if (r < 1)
            {
                float z = Mathf.Sqrt(1 - r * r);
                // Soft pastel shading (no dark muddy side) plus a pink rim light.
                float lit = .62f + .45f * Mathf.Max(0, sx * light.x + sy * light.y + z * light.z);
                float rim = Mathf.Pow(1 - z, 3) * .5f;
                float bands = Mathf.Sin(sy * 9 + Mathf.Sin(sx * 3) * .6f) * .5f + .5f;
                Color body = Color.Lerp(new Color(1, .62f, .84f), new Color(.72f, .6f, 1), bands);
                body = Color.Lerp(body, new Color(1, .9f, .95f), .25f * Mathf.Pow(z, 4));
                float edge = Mathf.Clamp01((1 - r) * 60);   // anti-aliased rim
                Color c = Color.Lerp(px[i], body * lit + new Color(1, .7f, .95f) * rim, edge);
                if (front && ringMask > 0) c = Color.Lerp(c, ring * (.6f + .4f * lit), ringMask * .9f);
                px[i] = c;
            }
            else
            {
                Color c = px[i] + new Color(1, .55f, .85f) * .35f * Mathf.Exp(-(r - 1) / .08f);   // soft atmosphere
                if (ringMask > 0) c = Color.Lerp(c, ring * .9f, ringMask * .9f);
                px[i] = c;
            }
        });
    }

    static void Moon(Color[] px, Vector3 centre, float angularRadius)
    {
        Vector3 light = new Vector3(.6f, .3f, .74f).normalized;
        Around(centre, angularRadius, (x, y, sx, sy) =>
        {
            int i = y * W + x;
            float r = Mathf.Sqrt(sx * sx + sy * sy);
            if (r < 1)
            {
                float z = Mathf.Sqrt(1 - r * r);
                float lit = .12f + .88f * Mathf.Max(0, sx * light.x + sy * light.y + z * light.z);
                float crater = Mathf.Clamp01(Fbm(new Vector3(sx, sy, z) * 4, 3, 5) * 1.6f - .5f) * .18f;
                px[i] = Color.Lerp(px[i], new Color(1, .96f, .86f) * (lit - crater), Mathf.Clamp01((1 - r) * 40));
            }
            else px[i] += new Color(1, .95f, .8f) * .3f * Mathf.Exp(-(r - 1) / .15f);
        });
    }

    // ---------- 3D value noise (seamless on the sphere) ----------

    static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 2246822519);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
        }
    }

    static float Noise(Vector3 p, int seed)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float fx = p.x - x, fy = p.y - y, fz = p.z - z;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float L(int a, int b, int c) => Hash(x + a, y + b, z + c, seed);
        float x00 = Mathf.Lerp(L(0, 0, 0), L(1, 0, 0), fx), x10 = Mathf.Lerp(L(0, 1, 0), L(1, 1, 0), fx);
        float x01 = Mathf.Lerp(L(0, 0, 1), L(1, 0, 1), fx), x11 = Mathf.Lerp(L(0, 1, 1), L(1, 1, 1), fx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz);
    }

    static float Fbm(Vector3 p, int octaves, int seed)
    {
        float sum = 0, amp = .5f, norm = 0;
        for (int i = 0; i < octaves; i++) { sum += Noise(p, seed + i) * amp; norm += amp; amp *= .5f; p *= 2.03f; }
        return sum / norm;
    }
}
