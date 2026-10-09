// 给 DigiPhant 大象装上 D.Va 风格机甲。
// 数据来自 Source~/build_mech.py（Blender 建模后导出的 Data/diva_mech.bytes），坐标是 Blender 的静止姿势空间。
// 这里不假设任何坐标轴约定：用大象网格上 UV 相同的点把两边对齐（最小二乘仿射），
// 再用 sharedMesh.bindposes 把零件烘焙进对应骨骼的局部空间，作为骨骼子物体刚性跟随。
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Diva.EditorTools
{
    public static class DivaMechBuilder
    {
        const string Root = "Assets/eva大象";
        const string DataPath = Root + "/Data/diva_mech.bytes";
        const string GeneratedDir = Root + "/Generated";
        const string PartPrefix = "DivaMech ";
        const string AnchorBone = "elephant_Spine2_bone";

        class MatData { public string name; public Color color, emission; public float rough, metal, strength, alpha; }
        class Group { public string bone; public Vector3[] pos, nrm; public float[] shade; public List<(int mat, int[] idx)> subs = new List<(int, int[])>(); }
        class Emitter { public string name, bone; public Vector3 pos, dir; }

        [MenuItem("Diva/Add D.Va Mech to Elephant")]
        public static void AddMenu() => Build(true);

        [MenuItem("Diva/Remove D.Va Mech")]
        public static void RemoveMenu()
        {
            var smr = FindElephant();
            int n = RemoveParts(smr);
            var toggle = smr.transform.root.GetComponentInChildren<DivaMechToggle>(true);
            if (toggle && toggle.skin && toggle.originalSkin) { Undo.RecordObject(toggle.skin, "Diva mech"); toggle.skin.sharedMaterial = toggle.originalSkin; }
            if (toggle) Undo.DestroyObjectImmediate(toggle);
            foreach (var c in new Component[] { smr.transform.root.GetComponent<DivaMechGestureLink>(), smr.transform.root.GetComponent<DivaBoosters>(), smr.transform.root.GetComponent<DivaBubbleCannons>() })
                if (c) Undo.DestroyObjectImmediate(c);
            EditorSceneManager.MarkSceneDirty(smr.gameObject.scene);
            Debug.Log("DIVA_MECH_REMOVED " + n + " parts");
        }

        public static void Build(bool save) => Build(save, true);

        public static void Build(bool save, bool backup)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play before adding the mech.");
            var smr = FindElephant();
            var scene = smr.gameObject.scene;
            bool firstInstall = !smr.transform.root.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith(PartPrefix, StringComparison.Ordinal));
            if (save && !string.IsNullOrEmpty(scene.path))
            {
                if (scene.isDirty) throw new InvalidOperationException("Save the scene before adding the mech.");
                if (firstInstall && backup)
                {
                    string backupPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(scene.path, null) + "_BeforeDivaMech.unity");
                    if (!AssetDatabase.CopyAsset(scene.path, backupPath)) throw new IOException("Could not back up the scene.");
                    Debug.Log("DIVA_MECH_BACKUP " + backupPath);
                }
            }

            ReadData(out var mats, out var refs, out var groups, out var emitters);
            var mesh = smr.sharedMesh;
            Matrix4x4 align = SolveAlignment(mesh, refs, out double rms, out int pairs);
            Debug.Log($"DIVA_MECH_ALIGN pairs {pairs} rms {rms:F6} det {align.determinant:F4} scale {Mathf.Pow(Mathf.Abs(align.determinant), 1f / 3):F4}");
            if (pairs < 500 || rms > 0.01 * Mathf.Pow(Mathf.Abs(align.determinant), 1f / 3) * 5)
                throw new InvalidOperationException("Could not line the mech up with this elephant mesh (pairs " + pairs + ", rms " + rms + ").");

            RemoveParts(smr);
            if (!AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.CreateFolder(Root, "Generated");
            // 不透明零件共用一个调色板材质（每根骨骼 1 次绘制），只有玻璃单独一个材质
            var atlas = MakeAtlas(mats);
            var materials = mats.Select(d => d.alpha < 1 ? MakeMaterial(d) : atlas).ToArray();
            RemoveOldMaterials(mats);

            var bones = smr.bones;
            var bindposes = mesh.bindposes;
            var parts = new List<GameObject>();
            foreach (var g in groups)
            {
                int bi = Array.FindIndex(bones, b => b && b.name == g.bone);
                if (bi < 0) { Debug.LogWarning("DIVA_MECH_MISSING_BONE " + g.bone); continue; }
                Matrix4x4 m = bindposes[bi] * align;
                var part = MakePart(g, m, mats, materials, atlas, bones[bi], smr.gameObject.layer);
                parts.Add(part);
            }

            var host = smr.transform.root.gameObject;
            Transform ground = null;
            var cores = new List<ParticleSystem>(); var glows = new List<ParticleSystem>(); var bubbleSystems = new List<ParticleSystem>();
            foreach (var e in emitters)
            {
                int bi = Array.FindIndex(bones, b => b && b.name == e.bone);
                if (bi < 0) { Debug.LogWarning("DIVA_MECH_MISSING_BONE " + e.bone); continue; }
                if (!ground) ground = MakeGround(host.transform);
                var em = bindposes[bi] * align;
                if (e.name.StartsWith("Thruster", StringComparison.Ordinal))
                {
                    var go = MakeFlame(e, em, bones[bi], smr.gameObject.layer, out var core, out var glow);
                    cores.Add(core); glows.Add(glow); parts.Add(go);
                }
                else if (e.name.StartsWith("Bubble", StringComparison.Ordinal))
                {
                    var go = MakeBubbleGun(e, em, bones[bi], ground, smr.gameObject.layer, out var ps);
                    bubbleSystems.Add(ps); parts.Add(go);
                }
                else parts.Add(MakeBlaster(e, em, bones[bi], ground, smr.gameObject.layer));
            }
            var boosters = SetUpBoosters(host, cores, glows);
            SetUpBubbles(host, bubbleSystems);
            SetUpGestureLink(host);
            SetUpAudio(host, smr, parts, boosters);
            var toggle = host.GetComponentInChildren<DivaMechToggle>(true);
            if (!toggle) toggle = Undo.AddComponent<DivaMechToggle>(host);
            Undo.RecordObject(toggle, "Diva mech");
            toggle.parts = parts.ToArray();
            toggle.showMech = true;
            SetUpCandySkin(toggle, smr);
            toggle.Apply();
            EditorUtility.SetDirty(toggle);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (save && !string.IsNullOrEmpty(scene.path)) EditorSceneManager.SaveScene(scene);
            int drawCalls = parts.Sum(p => { var r = p.GetComponent<MeshRenderer>(); return r ? r.sharedMaterials.Length : 0; });
            Debug.Log($"DIVA_MECH_BUILT {parts.Count} parts on '{host.name}', {groups.Sum(x => x.subs.Sum(s => s.idx.Length)) / 3} triangles, {drawCalls} mesh draw calls");
        }

        /// <summary>只按数据文件更新材质（不改场景）。</summary>
        [MenuItem("Diva/Refresh D.Va Mech Materials")]
        public static void RefreshMaterials()
        {
            ReadData(out var mats, out _, out _, out _);
            MakeAtlas(mats);
            foreach (var m in mats) if (m.alpha < 1) MakeMaterial(m);
            AssetDatabase.SaveAssets();
            Debug.Log("DIVA_MECH_MATERIALS " + mats.Count);
        }

        /// <summary>
        /// Batch mode: Unity -batchmode -projectPath P -executeMethod Diva.EditorTools.DivaMechBuilder.BuildFromCommandLine
        ///   -divaScene Assets/DigiPhant/Scenes/Diva.unity [-divaShots /abs/folder] -quit
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string scene = Arg("-divaScene") ?? "Assets/DigiPhant/Scenes/Diva.unity";
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            Build(true, false);
            string shots = Arg("-divaShots");
            if (!string.IsNullOrEmpty(shots)) Capture(shots);
            Debug.Log("DIVA_MECH_CLI_OK " + scene);
        }

        /// <summary>
        /// Only (re)wire the gesture link on an elephant that already has the mech, without rebuilding the parts,
        /// so the scene diff stays small. Menu: Diva > Link D.Va Mech to Diva Gestures.
        /// </summary>
        [MenuItem("Diva/Link D.Va Mech to Diva Gestures")]
        public static void LinkGesturesMenu()
        {
            var smr = FindElephant();
            SetUpGestureLink(smr.transform.root.gameObject);
            EditorSceneManager.MarkSceneDirty(smr.gameObject.scene);
        }

        public static void LinkGesturesFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-divaScene");
            string scene = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Assets/DigiPhant/Scenes/Diva.unity";
            EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            LinkGesturesMenu();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("DIVA_MECH_LINK_OK " + scene);
        }

        static SkinnedMeshRenderer FindElephant()
        {
            bool IsElephant(SkinnedMeshRenderer r) => r && r.sharedMesh && r.bones.Any(b => b && b.name == AnchorBone);
            var selected = Selection.activeGameObject ? Selection.activeGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(IsElephant).ToArray() : new SkinnedMeshRenderer[0];
            if (selected.Length == 1) return selected[0];
            var all = SceneManager.GetActiveScene().GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<SkinnedMeshRenderer>(true)).Where(IsElephant).ToArray();
            if (all.Length == 1) return all[0];
            throw new InvalidOperationException(all.Length == 0 ? "No DigiPhant elephant found in the open scene."
                : "Several elephants found; select one in the Hierarchy and try again.");
        }

        static int RemoveParts(SkinnedMeshRenderer smr)
        {
            var old = smr.transform.root.GetComponentsInChildren<Transform>(true)
                .Where(t => t && t.name.StartsWith(PartPrefix, StringComparison.Ordinal)).Select(t => t.gameObject).ToArray();
            foreach (var go in old) Undo.DestroyObjectImmediate(go);
            return old.Length;
        }

        // ------------------------------------------------------------------ data
        static void ReadData(out List<MatData> mats, out List<(Vector3 p, Vector2 uv)> refs, out List<Group> groups, out List<Emitter> emitters)
        {
            var bytes = File.ReadAllBytes(DataPath);
            using var r = new BinaryReader(new MemoryStream(bytes));
            string magic = Encoding.ASCII.GetString(r.ReadBytes(4));
            if (magic != "DVM1" && magic != "DVM2") throw new InvalidDataException("Not a Diva mech file: " + DataPath);
            bool hasShade = magic == "DVM2";
            string Str() => Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
            mats = new List<MatData>();
            int nm = r.ReadInt32();
            for (int i = 0; i < nm; i++)
            {
                var m = new MatData { name = Str() };
                m.color = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m.rough = r.ReadSingle(); m.metal = r.ReadSingle();
                m.emission = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m.strength = r.ReadSingle(); m.alpha = r.ReadSingle(); r.ReadSingle();
                mats.Add(m);
            }
            refs = new List<(Vector3, Vector2)>();
            int nr = r.ReadInt32();
            for (int i = 0; i < nr; i++)
                refs.Add((new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()), new Vector2(r.ReadSingle(), r.ReadSingle())));
            groups = new List<Group>();
            int ng = r.ReadInt32();
            for (int gi = 0; gi < ng; gi++)
            {
                var g = new Group { bone = Str() };
                int nv = r.ReadInt32();
                g.pos = new Vector3[nv]; g.nrm = new Vector3[nv];
                for (int i = 0; i < nv; i++) g.pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < nv; i++) g.nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                if (hasShade) { g.shade = new float[nv]; for (int i = 0; i < nv; i++) g.shade[i] = r.ReadSingle(); }
                int ns = r.ReadInt32();
                for (int s = 0; s < ns; s++)
                {
                    int mi = r.ReadInt32(), n = r.ReadInt32();
                    var idx = new int[n];
                    for (int i = 0; i < n; i++) idx[i] = r.ReadInt32();
                    g.subs.Add((mi, idx));
                }
                groups.Add(g);
            }
            emitters = new List<Emitter>();
            if (r.BaseStream.Position < r.BaseStream.Length)
            {
                int ne = r.ReadInt32();
                for (int i = 0; i < ne; i++)
                    emitters.Add(new Emitter { name = Str(), bone = Str(),
                        pos = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                        dir = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()) });
            }
        }

        // ------------------------------------------------------------------ alignment
        // 同一个 UV 在两边就是同一个点；用这些点对解出 Blender 空间 -> 大象网格空间的仿射矩阵。
        static Matrix4x4 SolveAlignment(Mesh mesh, List<(Vector3 p, Vector2 uv)> refs, out double rms, out int pairs)
        {
            const float Q = 8192f;
            var byUv = new Dictionary<(int, int), Vector3>();
            foreach (var (p, uv) in refs)
                byUv[((int)Math.Round(uv.x * Q), (int)Math.Round(uv.y * Q))] = p;
            var verts = mesh.vertices; var uvs = mesh.uv;
            var src = new List<Vector3>(); var dst = new List<Vector3>();
            for (int i = 0; i < verts.Length && i < uvs.Length; i++)
            {
                var key = ((int)Math.Round(uvs[i].x * Q), (int)Math.Round(uvs[i].y * Q));
                if (byUv.TryGetValue(key, out var p)) { src.Add(p); dst.Add(verts[i]); }
            }
            pairs = src.Count;
            var ata = new double[4, 4]; var atb = new double[4, 3];
            for (int k = 0; k < src.Count; k++)
            {
                double[] x = { src[k].x, src[k].y, src[k].z, 1 };
                double[] y = { dst[k].x, dst[k].y, dst[k].z };
                for (int i = 0; i < 4; i++)
                {
                    for (int j = 0; j < 4; j++) ata[i, j] += x[i] * x[j];
                    for (int c = 0; c < 3; c++) atb[i, c] += x[i] * y[c];
                }
            }
            var sol = Solve(ata, atb);
            var m = Matrix4x4.identity;
            for (int row = 0; row < 3; row++)
                m.SetRow(row, new Vector4((float)sol[0, row], (float)sol[1, row], (float)sol[2, row], (float)sol[3, row]));
            double err = 0;
            for (int k = 0; k < src.Count; k++) err += (m.MultiplyPoint3x4(src[k]) - dst[k]).sqrMagnitude;
            rms = src.Count > 0 ? Math.Sqrt(err / src.Count) : double.MaxValue;
            return m;
        }

        static double[,] Solve(double[,] a, double[,] b)
        {
            int n = 4, m = 3;
            a = (double[,])a.Clone(); b = (double[,])b.Clone();
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
                for (int k = 0; k < n; k++) (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
                for (int k = 0; k < m; k++) (b[c, k], b[piv, k]) = (b[piv, k], b[c, k]);
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double f = a[r, c] / a[c, c];
                    for (int k = 0; k < n; k++) a[r, k] -= f * a[c, k];
                    for (int k = 0; k < m; k++) b[r, k] -= f * b[c, k];
                }
            }
            var x = new double[n, m];
            for (int r = 0; r < n; r++) for (int k = 0; k < m; k++) x[r, k] = b[r, k] / a[r, r];
            return x;
        }

        // ------------------------------------------------------------------ assets
        static Material MakeMaterial(MatData d)
        {
            string path = GeneratedDir + "/" + d.name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            var c = d.color; c.a = d.alpha;
            mat.SetColor("_BaseColor", c);
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1 - d.rough));
            mat.SetFloat("_Metallic", d.metal);
            if (d.strength > 0)
            {
                mat.EnableKeyword("_EMISSION");
                // 没有 Bloom 时强度太高会发白；1.2 左右保持青色，核心更亮
                mat.SetColor("_EmissionColor", d.emission * Mathf.Min(d.strength * .2f, 2f));
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            if (d.alpha < 1)
            {
                // URP Lit 透明（玻璃罩）
                mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ palette atlas
        // 一张小贴图：每种材质占一列（4 像素宽），竖向是明暗渐变。顶点 UV = (所在列, 导出的明暗值)，
        // 凹处和下部偏暗、凸起边缘偏亮（掉漆感），不需要展 UV。金属度/光滑度和发光各一张同样布局的贴图。
        const int SwatchW = 4, AtlasW = 128, AtlasH = 32;
        const float EmissionScale = 2f;

        static float SwatchU(int i) => (i * SwatchW + SwatchW * .5f) / AtlasW;

        static Color Shade(MatData d, float v)
        {
            var c = d.color;
            if (d.strength > 0 || d.alpha < 1) return c;
            if (v < .5f)
            {
                float t = (.5f - v) / .5f;
                var dark = new Color(c.r * .5f + .05f, c.g * .42f + .04f, c.b * .55f + .07f);   // 阴影偏紫
                return Color.Lerp(c, dark, t * .85f);
            }
            float u = (v - .5f) / .5f;
            return Color.Lerp(c, new Color(.97f, .94f, .89f), u * .45f);                      // 边缘磨白
        }

        static Texture2D AtlasTexture(string name, bool linear)
        {
            string path = GeneratedDir + "/" + name + ".asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex && (tex.width != AtlasW || tex.height != AtlasH)) { AssetDatabase.DeleteAsset(path); tex = null; }
            if (!tex)
            {
                tex = new Texture2D(AtlasW, AtlasH, TextureFormat.RGBA32, false, linear) { name = name };
                AssetDatabase.CreateAsset(tex, path);
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        static Material MakeAtlas(List<MatData> mats)
        {
            if (!AssetDatabase.IsValidFolder(GeneratedDir)) AssetDatabase.CreateFolder(Root, "Generated");
            if (mats.Count * SwatchW > AtlasW) throw new InvalidOperationException("Too many materials for the palette atlas.");
            var baseTex = AtlasTexture("Mech Palette", false);
            var mgTex = AtlasTexture("Mech Palette MetalGloss", true);
            var emTex = AtlasTexture("Mech Palette Emission", false);
            var basePx = new Color[AtlasW * AtlasH]; var mgPx = new Color[AtlasW * AtlasH]; var emPx = new Color[AtlasW * AtlasH];
            for (int i = 0; i < AtlasW * AtlasH; i++) { basePx[i] = Color.magenta; mgPx[i] = Color.clear; emPx[i] = Color.black; }
            for (int m = 0; m < mats.Count; m++)
            {
                var d = mats[m];
                for (int y = 0; y < AtlasH; y++)
                {
                    float v = (y + .5f) / AtlasH;
                    var c = Shade(d, v); c.a = 1;
                    float smooth = Mathf.Clamp01(1 - d.rough) * (1 - .25f * Mathf.Abs(v - .5f) * 2);
                    var mg = new Color(d.metal, 0, 0, smooth);
                    var em = d.strength > 0 ? d.emission * (Mathf.Min(d.strength * .2f, 2f) / EmissionScale) : Color.black;
                    em.a = 1;
                    for (int x = m * SwatchW; x < (m + 1) * SwatchW; x++)
                    {
                        basePx[y * AtlasW + x] = c; mgPx[y * AtlasW + x] = mg; emPx[y * AtlasW + x] = em;
                    }
                }
            }
            baseTex.SetPixels(basePx); baseTex.Apply(false);
            mgTex.SetPixels(mgPx); mgTex.Apply(false);
            emTex.SetPixels(emPx); emTex.Apply(false);
            EditorUtility.SetDirty(baseTex); EditorUtility.SetDirty(mgTex); EditorUtility.SetDirty(emTex);

            string path = GeneratedDir + "/Mech Atlas.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_MetallicGlossMap", mgTex);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetFloat("_Smoothness", 1);
            mat.SetFloat("_SmoothnessTextureChannel", 0);
            mat.SetTexture("_EmissionMap", emTex);
            mat.SetColor("_EmissionColor", Color.white * EmissionScale);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // 换成调色板之后，旧的单色材质不再使用
        static void RemoveOldMaterials(List<MatData> mats)
        {
            foreach (var d in mats)
                if (d.alpha >= 1)
                    AssetDatabase.DeleteAsset(GeneratedDir + "/" + d.name + ".mat");
        }

        static GameObject MakePart(Group g, Matrix4x4 m, List<MatData> mats, Material[] materials, Material atlas, Transform bone, int layer)
        {
            var nm = m.inverse.transpose;
            bool flip = m.determinant < 0;
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
            var map = new Dictionary<long, int>();
            var opaque = new List<int>();
            var separate = new List<(Material mat, List<int> idx)>();
            foreach (var (mi, idx) in g.subs)
            {
                bool own = materials[mi] != atlas;
                var list = own ? new List<int>() : opaque;
                foreach (int i in idx)
                {
                    // 同一个顶点在不同颜色的面上要拆开，因为 UV 不同
                    long key = (long)i * 64 + (own ? 0 : mi + 1);
                    if (!map.TryGetValue(key, out int ni))
                    {
                        ni = verts.Count; map[key] = ni;
                        verts.Add(m.MultiplyPoint3x4(g.pos[i]));
                        norms.Add(nm.MultiplyVector(g.nrm[i]).normalized);
                        uvs.Add(new Vector2(SwatchU(mi), g.shade != null ? g.shade[i] : .5f));
                    }
                    list.Add(ni);
                }
                if (own) separate.Add((materials[mi], list));
            }
            var subs = new List<(Material mat, List<int> idx)>();
            if (opaque.Count > 0) subs.Add((atlas, opaque));
            subs.AddRange(separate);
            var mesh = new Mesh { name = PartPrefix + g.bone };
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++)
            {
                var idx = subs[s].idx.ToArray();
                if (flip) for (int i = 0; i < idx.Length; i += 3) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
                mesh.SetTriangles(idx, s);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            string path = GeneratedDir + "/" + mesh.name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); mesh = existing; EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(mesh, path);

            var go = new GameObject(mesh.name) { layer = layer };
            Undo.RegisterCreatedObjectUndo(go, "Diva mech");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = subs.Select(x => x.mat).ToArray();
            mr.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        // ------------------------------------------------------------------ trunk water blaster
        // 水平的落地平面，挂在会移动的大象根物体上（根物体只绕 Y 轴转），水滴碰到它就溅起水花
        static Transform MakeGround(Transform host)
        {
            var go = new GameObject(PartPrefix + "Water Ground");
            Undo.RegisterCreatedObjectUndo(go, "Diva mech");
            go.transform.SetParent(host, false);
            go.transform.position = host.position;
            go.transform.rotation = Quaternion.identity;
            return go.transform;
        }

        static GameObject MakeBlaster(Emitter e, Matrix4x4 m, Transform bone, Transform ground, int layer)
        {
            var go = new GameObject(PartPrefix + e.name) { layer = layer };
            Undo.RegisterCreatedObjectUndo(go, "Diva mech");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = m.MultiplyPoint3x4(e.pos);
            var d = m.MultiplyVector(e.dir).normalized;
            var up = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
            go.transform.localRotation = Quaternion.LookRotation(d, up);
            // 骨骼可能带缩放；粒子用 Local 缩放模式，按世界单位设置
            var water = WaterMaterial();
            var waterColor = new ParticleSystem.MinMaxGradient(new Color(.86f, .98f, 1f, .97f), new Color(.50f, .82f, 1f, .92f));

            var splash = NewSystem("Splash", go.transform, layer, water, ParticleSystemRenderMode.Billboard);
            {
                var main = splash.main; main.loop = false; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .45f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(.04f, .09f);
                main.startColor = waterColor; main.gravityModifier = 2f; main.maxParticles = 600;
                var em = splash.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 2, 5) });
                var sh = splash.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = .03f;
                sh.rotation = new Vector3(-90, 0, 0);
                Shrink(splash);
            }
            var jet = go.AddComponent<ParticleSystem>();
            jet.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            {
                var main = jet.main; main.duration = 1; main.loop = true; main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, .9f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(8.7f, 10.3f);
                main.startSize = new ParticleSystem.MinMaxCurve(.12f, .20f);
                main.startColor = waterColor; main.gravityModifier = 1f;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Local; main.maxParticles = 900;
                var em = jet.emission; em.rateOverTime = 0;
                var sh = jet.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 2.5f; sh.radius = .035f;
                var sol = jet.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .8f, 1, 1.5f));
                Fade(jet, 1f);
                var col = jet.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes;
                col.SetPlane(0, ground); col.lifetimeLoss = 1; col.bounce = 0; col.dampen = 1; col.radiusScale = .5f;
                var sub = jet.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .035f; r.lengthScale = 1.8f;
                r.sharedMaterial = water; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                r.sortMode = ParticleSystemSortMode.Distance;
            }
            var mist = NewSystem("Mist", go.transform, layer, water, ParticleSystemRenderMode.Billboard);
            {
                var main = mist.main; main.loop = true; main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .55f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(.10f, .26f);
                main.startColor = new Color(.88f, .97f, 1f, .32f); main.gravityModifier = .15f; main.maxParticles = 200;
                var em = mist.emission; em.rateOverTime = 0;
                var sh = mist.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 16; sh.radius = .05f;
                var sol = mist.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 2.2f));
                Fade(mist, 1f);
            }
            var lightGo = new GameObject("Muzzle Light") { layer = layer };
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0, 0, .08f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point; light.color = new Color(.55f, 1f, .95f); light.range = 1.6f;
            light.intensity = 0; light.shadows = LightShadows.None;

            var blaster = go.AddComponent<DivaTrunkBlaster>();
            blaster.jet = jet; blaster.mist = mist; blaster.muzzleLight = light;
            blaster.jetRate = 260; blaster.mistRate = 60;
            blaster.loopSource = NewAudio(go, Clip("water_spray_loop"), true);
            blaster.fxSource = NewAudio(go, null, false);
            blaster.startClip = Clip("water_spray_start");
            blaster.stopClip = Clip("water_spray_stop");
            return go;
        }

        static ParticleSystem NewSystem(string name, Transform parent, int layer, Material mat, ParticleSystemRenderMode mode)
        {
            var go = new GameObject(name) { layer = layer };
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = mode; r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return ps;
        }

        static void Fade(ParticleSystem ps, float startAlpha)
        {
            var c = ps.colorOverLifetime; c.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(startAlpha, 0), new GradientAlphaKey(startAlpha * .85f, .7f), new GradientAlphaKey(0, 1) });
            c.color = g;
        }

        static void Shrink(ParticleSystem ps)
        {
            var s = ps.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .3f));
            Fade(ps, 1f);
        }


        // ------------------------------------------------------------------ thruster flames, bubble cannons, candy skin
        static GameObject PlaceEmitter(Emitter e, Matrix4x4 m, Transform bone, int layer)
        {
            var go = new GameObject(PartPrefix + e.name) { layer = layer };
            Undo.RegisterCreatedObjectUndo(go, "Diva mech");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = m.MultiplyPoint3x4(e.pos);
            var d = m.MultiplyVector(e.dir).normalized;
            var up = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > .95f ? Vector3.forward : Vector3.up;
            go.transform.localRotation = Quaternion.LookRotation(d, up);
            return go;
        }

        static GameObject MakeFlame(Emitter e, Matrix4x4 m, Transform bone, int layer, out ParticleSystem core, out ParticleSystem glow)
        {
            var go = PlaceEmitter(e, m, bone, layer);
            var flame = EffectMaterial("Mech Flame", GlowTexture(), true);
            core = go.AddComponent<ParticleSystem>();
            core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            {
                var main = core.main; main.loop = true; main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.22f, .38f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(5, 7);
                main.startSize = new ParticleSystem.MinMaxCurve(.22f, .34f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startColor = Color.white;
                main.simulationSpace = ParticleSystemSimulationSpace.World;   // 跑起来时火焰拖在后面
                main.scalingMode = ParticleSystemScalingMode.Local; main.maxParticles = 400;
                var em = core.emission; em.rateOverTime = 0;
                var sh = core.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 7; sh.radius = .12f;
                var col = core.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                // 白热中心 -> 青色 -> 洋红尾巴（D.Va 推进器的感觉）
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(.45f, .95f, 1f), .25f),
                                  new GradientColorKey(new Color(.85f, .35f, .95f), .6f), new GradientColorKey(new Color(.6f, .2f, .8f), 1) },
                          new[] { new GradientAlphaKey(.95f, 0), new GradientAlphaKey(.85f, .25f), new GradientAlphaKey(.45f, .6f), new GradientAlphaKey(0, 1) });
                col.color = g;
                var sol = core.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .25f));
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .05f; r.lengthScale = 1.4f;
                r.sharedMaterial = flame; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            }
            glow = NewSystem("Glow", go.transform, layer, flame, ParticleSystemRenderMode.Billboard);
            {
                var main = glow.main; main.loop = true; main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.08f, .14f);
                main.startSpeed = .5f; main.startSize = new ParticleSystem.MinMaxCurve(.7f, .9f);
                main.startColor = new Color(.4f, .9f, 1f, .35f); main.maxParticles = 60;
                var em = glow.emission; em.rateOverTime = 0;
                var sh = glow.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .05f;
            }
            return go;
        }

        static GameObject MakeBubbleGun(Emitter e, Matrix4x4 m, Transform bone, Transform ground, int layer, out ParticleSystem bubbles)
        {
            var go = PlaceEmitter(e, m, bone, layer);
            var bubbleMat = EffectMaterial("Mech Bubble", BubbleTexture(), false);
            var pop = NewSystem("Pop", go.transform, layer, WaterMaterial(), ParticleSystemRenderMode.Billboard);
            {
                var main = pop.main; main.loop = false; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.15f, .3f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(.02f, .04f);
                main.startColor = new Color(1, 1, 1, .8f); main.gravityModifier = 1; main.maxParticles = 300;
                var em = pop.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 4, 7) });
                var sh = pop.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .02f;
                Fade(pop, .8f);
            }
            bubbles = go.AddComponent<ParticleSystem>();
            bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            {
                var main = bubbles.main; main.loop = true; main.playOnAwake = true;
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 3f);
                main.startSize = new ParticleSystem.MinMaxCurve(.12f, .34f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .9f, 1f, .95f), new Color(.85f, .96f, 1f, .95f));
                main.gravityModifier = -.03f;   // 慢慢往上飘
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Local; main.maxParticles = 300;
                var em = bubbles.emission; em.rateOverTime = 0;
                var sh = bubbles.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12; sh.radius = .12f;
                var lim = bubbles.limitVelocityOverLifetime; lim.enabled = true; lim.limit = .6f; lim.dampen = .08f;
                var noise = bubbles.noise; noise.enabled = true; noise.strength = .35f; noise.frequency = .4f; noise.scrollSpeed = .3f; noise.damping = true;
                var sol = bubbles.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .55f), new Keyframe(.15f, 1f), new Keyframe(1, 1.1f)));
                var col = bubbles.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .05f), new GradientAlphaKey(.9f, .85f), new GradientAlphaKey(0, 1) });
                col.color = g;
                var c = bubbles.collision; c.enabled = true; c.type = ParticleSystemCollisionType.Planes;
                c.SetPlane(0, ground); c.lifetimeLoss = 1; c.bounce = 0; c.dampen = 1; c.radiusScale = .5f;
                var sub = bubbles.subEmitters; sub.enabled = true;
                sub.AddSubEmitter(pop, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing);
                sub.AddSubEmitter(pop, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing);
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Billboard; r.sharedMaterial = bubbleMat;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.sortMode = ParticleSystemSortMode.Distance;
            }
            return go;
        }

        static DivaBoosters SetUpBoosters(GameObject host, List<ParticleSystem> cores, List<ParticleSystem> glows)
        {
            var b = host.GetComponent<DivaBoosters>();
            if (!b) b = Undo.AddComponent<DivaBoosters>(host);
            Undo.RecordObject(b, "Diva mech boosters");
            b.cores = cores.ToArray(); b.glows = glows.ToArray();
            b.coreRate = 230; b.glowRate = 50; b.flameSize = .5f; b.flameSpeed = 6;
            b.locomotion = host.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DigiPhant.DigiPhantLocomotion>(true)).FirstOrDefault();
            EditorUtility.SetDirty(b);
            return b;
        }

        static void SetUpBubbles(GameObject host, List<ParticleSystem> systems)
        {
            var b = host.GetComponent<DivaBubbleCannons>();
            if (!b) b = Undo.AddComponent<DivaBubbleCannons>(host);
            Undo.RecordObject(b, "Diva mech bubbles");
            b.bubbles = systems.ToArray();
            b.blowClip = Clip("bubble_blow");
            if (systems.Count > 0) b.source = NewAudio(systems[0].gameObject, null, false);
            EditorUtility.SetDirty(b);
        }

        // Diva 三人手势 -> 水枪和泡泡（场景里没有 DivaDemo 时组件也会加上，但什么都不做）
        static void SetUpGestureLink(GameObject host)
        {
            var link = host.GetComponent<DivaMechGestureLink>();
            if (!link) link = Undo.AddComponent<DivaMechGestureLink>(host);
            Undo.RecordObject(link, "Diva mech gestures");
            link.demo = host.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                .FirstOrDefault(m => m && m.GetType().Name == "DivaDemo");
            link.waterGun = host.GetComponentInChildren<DivaTrunkBlaster>(true);
            link.bubbles = host.GetComponent<DivaBubbleCannons>();
            EditorUtility.SetDirty(link);
            Debug.Log("DIVA_MECH_GESTURES " + (link.demo ? "linked to " + link.demo.name : "no DivaDemo in this scene"));
        }

        // 大象本身只叠一层很淡的糖果粉（原材质不改，另存一份）
        static readonly Color CandyTint = new Color(1.12f, .90f, 1.0f, 1);

        static void SetUpCandySkin(DivaMechToggle toggle, SkinnedMeshRenderer smr)
        {
            string path = GeneratedDir + "/Elephant Candy.mat";
            var original = toggle.originalSkin ? toggle.originalSkin : smr.sharedMaterial;
            if (original && original.name == "Elephant Candy") original = toggle.originalSkin;
            if (!original) return;
            var candy = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!candy) { candy = new Material(original) { name = "Elephant Candy" }; AssetDatabase.CreateAsset(candy, path); }
            else candy.CopyPropertiesFromMaterial(original);
            // 有皮肤专用的糖果粉贴图就用它（象牙和眼睛保持原色），否则整体叠一层淡粉
            var candyTex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/Elephant_D_Candy.jpg");
            if (candyTex)
            {
                candy.SetTexture("_BaseMap", candyTex);
                if (candy.HasProperty("_MainTex")) candy.SetTexture("_MainTex", candyTex);
                candy.SetColor("_BaseColor", Color.white);
            }
            else candy.SetColor("_BaseColor", CandyTint);
            EditorUtility.SetDirty(candy);
            toggle.skin = smr; toggle.originalSkin = original; toggle.candyMaterial = candy;
            Undo.RecordObject(smr, "Diva candy skin");
        }

        static Material EffectMaterial(string name, Texture2D tex, bool additive)
        {
            string path = GeneratedDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", additive ? 2 : 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Texture2D MakeTexture(string name, int n, Func<float, float, Color> f)
        {
            string path = GeneratedDir + "/" + name + ".asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex) return tex;
            tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = f((x + .5f) / n * 2 - 1, (y + .5f) / n * 2 - 1);
            tex.SetPixels(px); tex.Apply();
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static Texture2D GlowTexture() => MakeTexture("Soft Glow", 64, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            float a = Mathf.Pow(Mathf.Clamp01(1 - r), 2.2f);
            return new Color(1, 1, 1, a);
        });

        // 肥皂泡：几乎透明的内部，彩虹薄膜边缘，一个高光点
        static Texture2D BubbleTexture() => MakeTexture("Soap Bubble", 128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v);
            if (r > 1) return new Color(1, 1, 1, 0);
            float hue = Mathf.Repeat(Mathf.Atan2(v, u) / (Mathf.PI * 2) + r * .35f, 1);
            var film = Color.HSVToRGB(hue, .45f, 1);
            float rim = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.78f, .97f, r)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.97f, 1f, r)));
            float a = .06f + .65f * rim;
            float hl = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(u, v), new Vector2(-.38f, .42f)) / .16f);
            var c = Color.Lerp(Color.Lerp(new Color(.9f, .95f, 1f), film, rim), Color.white, hl);
            return new Color(c.r, c.g, c.b, Mathf.Max(a, hl * .9f));
        });

        static AudioClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/" + name + ".wav");
            if (!clip) Debug.LogWarning("DIVA_MECH_MISSING_SOUND " + name);
            return clip;
        }

        static AudioSource NewAudio(GameObject go, AudioClip clip, bool loop)
        {
            var a = go.AddComponent<AudioSource>();
            a.clip = clip; a.loop = loop; a.playOnAwake = false; a.volume = loop ? 0 : 1;
            a.spatialBlend = .8f; a.minDistance = 2; a.maxDistance = 40; a.dopplerLevel = 0;
            return a;
        }

        // 脚步声（四只脚的骨骼）和背甲上的喷射口嗡鸣
        static void SetUpAudio(GameObject host, SkinnedMeshRenderer smr, List<GameObject> parts, DivaBoosters boosters)
        {
            var audio = host.GetComponent<DivaMechAudio>();
            if (!audio) audio = Undo.AddComponent<DivaMechAudio>(host);  // Unity 的假 null 不能用 ??
            Undo.RecordObject(audio, "Diva mech audio");
            string[] feet = { "elephant_l_Paw_bone", "elephant_r_Paw_bone", "elephant_l_Toe_bone", "elephant_r_Toe_bone" };
            audio.feet = feet.Select(n => smr.bones.FirstOrDefault(b => b && b.name == n)).Where(b => b).ToArray();
            audio.stepClips = Enumerable.Range(1, 4).Select(i => Clip("mech_step_" + i)).Where(c => c).ToArray();
            audio.boosters = boosters;
            var shell = parts.FirstOrDefault(p => p.name == PartPrefix + "elephant_Spine2_bone");
            if (shell)
            {
                var hum = NewAudio(shell, Clip("thruster_hum_loop"), true);
                hum.playOnAwake = true; hum.volume = audio.humVolume;
                audio.humSource = hum;
            }
            EditorUtility.SetDirty(audio);
            Debug.Log($"DIVA_MECH_AUDIO feet {audio.feet.Length} steps {audio.stepClips.Length} hum {(audio.humSource ? "yes" : "no")}");
        }

        static Material WaterMaterial()
        {
            string texPath = GeneratedDir + "/Water Drop.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (!tex)
            {
                const int N = 64;
                tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Water Drop", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + .5f) / N * 2 - 1, v = (y + .5f) / N * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.SmoothStep(0, 1, Mathf.Clamp01((1 - r) / .45f));
                    float hl = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(u, v), new Vector2(-.3f, .3f)) / .35f);
                    float c = Mathf.Lerp(.82f, 1f, hl);
                    px[y * N + x] = new Color(c, c, c, a);
                }
                tex.SetPixels(px); tex.Apply();
                AssetDatabase.CreateAsset(tex, texPath);
            }
            string path = GeneratedDir + "/Mech Water.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ preview shots
        [MenuItem("Diva/Capture D.Va Mech Screenshots")]
        public static void CaptureMenu() => Capture(Path.GetFullPath(Path.Combine(Application.dataPath, "../work/diva-mech-shots")));

        public static void Capture(string dir)
        {
            Directory.CreateDirectory(dir);
            var smr = FindElephant();
            var b = smr.bounds;
            var root = smr.transform.root;
            Vector3 fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            // 大象朝向：用骨骼找头和尾，比假设物体朝向可靠
            var head = smr.bones.FirstOrDefault(t => t && t.name == "elephant_Head_bone");
            var tail = smr.bones.FirstOrDefault(t => t && t.name == "elephant_Tail1_bone");
            if (head && tail) fwd = Vector3.ProjectOnPlane(head.position - tail.position, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            float size = b.size.magnitude;
            var views = new (string name, Vector3 dir, float h)[]
            {
                ("hero", (fwd * .9f - right * .75f).normalized, .35f),
                ("front", fwd, .15f), ("side_left", -right, .12f), ("back", -fwd, .25f),
            };
            var camGo = new GameObject("DivaMechShotCamera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35; cam.nearClipPlane = .05f; cam.farClipPlane = 500;
            cam.clearFlags = CameraClearFlags.Skybox;
            var rt = new RenderTexture(1280, 860, 24, RenderTextureFormat.ARGB32);
            // 编辑模式下直接改骨骼时皮肤不会自动重算；强制每次渲染都重算，截图才和 Play 时一致
            bool forceSkin = smr.forceMatrixRecalculationPerRender;
            smr.forceMatrixRecalculationPerRender = true;
            try
            {
                foreach (var v in views)
                {
                    Shoot(cam, rt, b.center, v.dir, v.h, size * 1.25f, Path.Combine(dir, v.name + ".png"));
                }
                // 走路动画中的姿势：用 AnimationMode 采样，不改动场景
                // DigiPhantLocomotion 在另一个根物体（DigiPhant Controls）上，所以搜整个场景
                var loco = smr.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MonoBehaviour>(true))
                    .FirstOrDefault(c => c && c.GetType().FullName == "DigiPhant.DigiPhantLocomotion");
                Animator animator = null;
                AnimationClip walk = null;
                if (loco)
                {
                    var so = new SerializedObject(loco);
                    walk = so.FindProperty("walk")?.objectReferenceValue as AnimationClip;
                    animator = so.FindProperty("elephantAnimator")?.objectReferenceValue as Animator;
                }
                if (!animator) animator = smr.GetComponentInParent<Animator>();
                if (!walk || !animator) Debug.LogWarning("DIVA_MECH_SHOTS no walk clip or Animator found; skipped walk shots");
                if (walk && animator)
                {
                    AnimationMode.StartAnimationMode();
                    try
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(animator.gameObject, walk, walk.length * k / 3f);
                            AnimationMode.EndSampling();
                            SceneView.RepaintAll();
                            Shoot(cam, rt, smr.bounds.center, (fwd * .5f - right).normalized, .2f, size * 1.25f, Path.Combine(dir, $"walk_{k}.png"));
                        }
                    }
                    finally { AnimationMode.StopAnimationMode(); }
                }
                // 控制器会转动的骨骼：前后腿抬起、头转、耳朵张开，各转到最大角度看是否穿模
                var tests = new (string bone, Vector3 axis, float deg)[]
                {
                    ("elephant_l_Humerus_bone", Vector3.right, -25), ("elephant_r_Femur_bone", Vector3.right, -25),
                    ("elephant_Head_bone", Vector3.up, 25), ("elephant_l_Ear1_bone", Vector3.up, 25), ("elephant_r_Ear1_bone", Vector3.up, -25),
                };
                var saved = new List<(Transform, Quaternion)>();
                try
                {
                    foreach (var t in tests)
                    {
                        var bone = smr.bones.FirstOrDefault(x => x && x.name == t.bone);
                        if (!bone) continue;
                        saved.Add((bone, bone.localRotation));
                        var axis = bone.InverseTransformDirection(root.TransformDirection(t.axis)).normalized;
                        bone.localRotation *= Quaternion.AngleAxis(t.deg, axis);
                    }
                    Shoot(cam, rt, smr.bounds.center, (fwd * .8f - right).normalized, .25f, size * 1.25f, Path.Combine(dir, "controls_max.png"));
                }
                finally { foreach (var (t, q) in saved) t.localRotation = q; }
                CaptureSpray(dir, cam, rt, smr, root, fwd, right, size);
                var boost = root.GetComponent<DivaBoosters>();
                if (boost)
                {
                    boost.Apply(1);
                    foreach (var c in boost.cores) if (c) c.Simulate(.6f, true, true, true);
                    Shoot(cam, rt, smr.bounds.center - fwd * size * .35f, (-right * 1f - fwd * .25f).normalized, .12f, size * 1.05f, Path.Combine(dir, "boost.png"));
                    foreach (var c in boost.cores) if (c) c.Clear(true);
                    boost.Apply(0);
                }
                CaptureGestures(dir, cam, rt, smr, root, fwd, right, size);
                var bub = root.GetComponent<DivaBubbleCannons>();
                if (bub)
                {
                    bub.Apply(1);
                    foreach (var b2 in bub.bubbles) if (b2) b2.Simulate(3f, true, true, true);
                    Shoot(cam, rt, smr.bounds.center + fwd * size * .35f, (fwd * .7f - right * .7f).normalized, .15f, size * 1.15f, Path.Combine(dir, "bubbles.png"));
                    foreach (var b2 in bub.bubbles) if (b2) b2.Clear(true);
                    bub.Apply(0);
                }
            }
            finally
            {
                smr.forceMatrixRecalculationPerRender = forceSkin;
                UnityEngine.Object.DestroyImmediate(camGo);
                rt.Release();
            }
            Debug.Log("DIVA_MECH_SHOTS " + dir);
        }

        // 喷水预览：默认姿势、鼻子卷到 +1 和 -1（模拟 DigiPhantController 的 Trunk curl），以及一组动图帧
        static void CaptureSpray(string dir, Camera cam, RenderTexture rt, SkinnedMeshRenderer smr, Transform root, Vector3 fwd, Vector3 right, float size)
        {
            var blaster = root.GetComponentInChildren<DivaTrunkBlaster>(true);
            if (!blaster || !blaster.jet) { Debug.LogWarning("DIVA_MECH_SHOTS no trunk blaster"); return; }
            var ctrl = smr.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DigiPhant.DigiPhantController>(true)).FirstOrDefault();
            var trunk = ctrl ? ctrl.controls.FirstOrDefault(c => c.label == "Trunk curl") : null;
            var head = smr.bones.FirstOrDefault(t => t && t.name == "elephant_Head_bone");
            Vector3 focus = (head ? head.position : smr.bounds.center) + fwd * size * .25f - Vector3.up * size * .05f;
            Vector3 view = (-right * 1f + fwd * .25f).normalized;
            foreach (var (label, value) in new[] { ("idle", 0f), ("curl_plus", 1f), ("curl_minus", -1f) })
            {
                var saved = new List<(Transform, Quaternion)>();
                try
                {
                    if (trunk != null && value != 0)
                        foreach (var b in trunk.bones)
                            if (b) { saved.Add((b, b.localRotation)); b.localRotation *= Quaternion.AngleAxis(value * trunk.degrees, trunk.localAxis.normalized); }
                    blaster.Apply(1, .3f);
                    blaster.jet.Simulate(1.2f, true, true, true);
                    Shoot(cam, rt, focus, view, .12f, size * .95f, Path.Combine(dir, "spray_" + label + ".png"));
                    if (label == "idle")
                    {
                        string gif = Path.Combine(dir, "spray_frames");
                        Directory.CreateDirectory(gif);
                        for (int k = 0; k < 36; k++)
                        {
                            float t = .03f + k * (1f / 30);
                            blaster.Apply(Mathf.Clamp01(t / blaster.rampSeconds), t);
                            blaster.jet.Simulate(t, true, true, true);
                            Shoot(cam, rt, focus, view, .12f, size * .95f, Path.Combine(gif, $"f{k:000}.png"));
                        }
                    }
                }
                finally
                {
                    foreach (var (t, q) in saved) t.localRotation = q;
                    blaster.jet.Clear(true);
                    blaster.Apply(0, 0);
                }
            }
        }

        // 手势预览：按 DivaDemo 的姿势摆鼻子（喷水时上抬 12°/节并按瞄准左右偏，喝水时上抬 18°/节），
        // 喷水用 DivaMechGestureLink 的弹道，喝水时冒泡泡。拍完全部还原，不改场景。
        static void CaptureGestures(string dir, Camera cam, RenderTexture rt, SkinnedMeshRenderer smr, Transform root, Vector3 fwd, Vector3 right, float size)
        {
            var link = root.GetComponent<DivaMechGestureLink>();
            if (!link || !link.demo || !link.waterGun || !link.waterGun.jet) return;
            var demoType = link.demo.GetType();
            var trunkBones = demoType.GetField("trunkBones")?.GetValue(link.demo) as Transform[];
            float aimDegrees = demoType.GetField("trunkAimDegrees")?.GetValue(link.demo) is float f ? f : 35;
            var loco = link.demo.GetComponent<DigiPhant.DigiPhantLocomotion>();
            var travel = loco ? loco.travelRoot : root;
            var gun = link.waterGun;
            var main = gun.jet.main;
            var gravity = main.gravityModifier; var life = main.startLifetime;
            var gunRot = gun.transform.localRotation; float gunSpeed = gun.jetSpeed;
            void Pose(float pitch, float aim, List<(Transform, Quaternion)> saved)
            {
                if (trunkBones == null) return;
                foreach (var bone in trunkBones)
                {
                    if (!bone) continue;
                    saved.Add((bone, bone.localRotation));
                    var pitchAxis = bone.InverseTransformDirection(travel.right).normalized;
                    var yawAxis = bone.InverseTransformDirection(Vector3.up).normalized;
                    bone.localRotation *= Quaternion.AngleAxis(pitch, pitchAxis) * Quaternion.AngleAxis(aim * aimDegrees / Mathf.Max(1, trunkBones.Length), yawAxis);
                }
            }
            var head = smr.bones.FirstOrDefault(t => t && t.name == "elephant_Head_bone");
            Vector3 focus = (head ? head.position : smr.bounds.center) + fwd * size * .55f - Vector3.up * size * .08f;
            foreach (var (label, aim) in new[] { ("left", -1f), ("center", 0f), ("right", 1f) })
            {
                var saved = new List<(Transform, Quaternion)>();
                try
                {
                    Pose(12, aim, saved);
                    main.gravityModifier = 2.5f / 9.81f;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.5f);
                    link.PreviewAim(aim);
                    gun.Apply(1, .3f);
                    gun.jet.Simulate(1.3f, true, true, true);
                    Shoot(cam, rt, focus, (-fwd * .55f - right * .55f + Vector3.up * .25f).normalized, .35f, size * 1.25f, Path.Combine(dir, "gesture_spray_" + label + ".png"));
                }
                finally
                {
                    foreach (var (t, q) in saved) t.localRotation = q;
                    gun.jet.Clear(true); gun.Apply(0, 0);
                    main.gravityModifier = gravity; main.startLifetime = life;
                    gun.transform.localRotation = gunRot; gun.jetSpeed = gunSpeed;
                }
            }
            var bub = root.GetComponent<DivaBubbleCannons>();
            if (bub)
            {
                var saved = new List<(Transform, Quaternion)>();
                try
                {
                    Pose(18, 0, saved);
                    bub.Apply(1);
                    foreach (var b2 in bub.bubbles) if (b2) b2.Simulate(2.5f, true, true, true);
                    Shoot(cam, rt, smr.bounds.center + fwd * size * .3f, (fwd * .7f - right * .7f).normalized, .15f, size * 1.15f, Path.Combine(dir, "gesture_drink_bubbles.png"));
                }
                finally
                {
                    foreach (var (t, q) in saved) t.localRotation = q;
                    foreach (var b2 in bub.bubbles) if (b2) b2.Clear(true);
                    bub.Apply(0);
                }
            }
        }

        static void Shoot(Camera cam, RenderTexture rt, Vector3 center, Vector3 dir, float h, float dist, string file)
        {
            cam.transform.position = center + dir * dist + Vector3.up * dist * h;
            cam.transform.LookAt(center);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
