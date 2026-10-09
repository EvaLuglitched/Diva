using DigiPhant;
using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 驾驶舱框架和舒适暗角。
    /// 框架：面前一道弧形仪表台（顶边一条发光线）、左右两根舱盖支柱（内侧发光），颜色跟着涂装变。
    /// 它们和驾驶舱一起动、不跟头动，是眼睛里一直不动的参照物，外面的世界在动时人不容易晕。
    /// 暗角：跑得越快、转得越快，视野外圈越暗（贴在眼前的一圈渐变），停下来就消失。
    /// 都放在仪表盘面板（正前方 ±25°、1.7 米）的外面，不挡界面。
    /// </summary>
    public class DivaVRCockpit : MonoBehaviour
    {
        [Tooltip("舒适暗角最深多暗（0 = 关）。")]
        [Range(0, 1)] public float vignetteMax = .6f;

        DivaVRRig rig;
        DigiPhantLocomotion loco;
        DivaMechSkins skins;
        Material body, glowA, glowB, screen, vignette;

        [System.Serializable] class CockpitPart { public string name, material; public float[] v, n; public int[] t; }
        [System.Serializable] class CockpitData { public CockpitPart[] parts; }

        /// <summary>读 Blender 做的驾驶舱（Assets/DivaVR/Source~/build_cockpit.py 导出到 Resources/DivaVR Cockpit.json）。</summary>
        bool BuildFromBlender(Transform root)
        {
            var json = Resources.Load<TextAsset>("DivaVR Cockpit");
            if (!json) return false;
            var data = JsonUtility.FromJson<CockpitData>(json.text);
            if (data?.parts == null || data.parts.Length == 0) return false;
            foreach (var p in data.parts)
            {
                int count = p.v.Length / 3;
                var verts = new Vector3[count];
                var norms = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    verts[i] = new Vector3(p.v[i * 3], p.v[i * 3 + 1], p.v[i * 3 + 2]);
                    norms[i] = new Vector3(p.n[i * 3], p.n[i * 3 + 1], p.n[i * 3 + 2]);
                }
                var mesh = new Mesh { name = p.name, indexFormat = count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.vertices = verts; mesh.normals = norms; mesh.triangles = p.t;
                mesh.RecalculateBounds();
                var m = p.material == "glowA" ? glowA : p.material == "glowB" ? glowB : p.material == "screen" ? screen : body;
                Part(root, p.name, mesh, m);
            }
            Resources.UnloadAsset(json);
            return true;
        }
        float vignetteNow, lastYaw;
        bool haveYaw;
        // 发光边：跑得越快越亮，冲刺时脉动，命中闪白，大招整圈爆亮（参考守望先锋的打击反馈）
        Color baseA = new Color(1f, .4f, .8f) * 2.2f, baseB = new Color(.3f, .95f, .85f) * 2.2f;
        float flash;
        DivaShowDirector show;
        DivaDemo demo;
        bool blender;
        // 仪表台两块侧屏：左边速度，右边水量和连击
        TextMeshProUGUI speedText, comboText;
        RectTransform speedBar, waterBar;
        bool screensBuilt;

        void Start()
        {
            rig = GetComponent<DivaVRRig>();
            show = FindAnyObjectByType<DivaShowDirector>();
            demo = FindAnyObjectByType<DivaDemo>();
            DivaTarget.Hit += OnHit;
            DivaShowDirector.PhaseChanged += OnPhase;
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            skins = FindAnyObjectByType<DivaMechSkins>();
            float eye = rig ? rig.eyeHeight : .78f;
            body = Lit(new Color(.12f, .1f, .18f));
            glowA = Unlit(new Color(1f, .4f, .8f) * 2f);
            glowB = Unlit(new Color(.3f, .95f, .85f) * 2f);
            var root = new GameObject("Cockpit").transform;
            root.SetParent(transform, false);
            screen = Unlit(new Color(.05f, .22f, .28f));
            if (blender = BuildFromBlender(root)) { if (skins) { skins.Changed += OnSkin; OnSkin(skins.Current); } BuildVignette(); return; }

            // 没有 Blender 导出的模型时的简易框架
            // 仪表台：半径 0.52–0.70 米、左右各 55°，比眼睛低 0.36–0.52 米
            Part(root, "Dash", Sector(.52f, .70f, -55, 55, eye - .52f, eye - .36f, 28), body);
            Part(root, "Dash Glow", Sector(.515f, .535f, -55, 55, eye - .365f, eye - .35f, 28), glowA);
            // 舱盖支柱：左右 62° 方向，从仪表台高度到头顶，顶上微微往里弯
            foreach (float side in new[] { -1f, 1f })
            {
                var post = Sector(.70f, .76f, side * 62 - 2.2f, side * 62 + 2.2f, eye - .5f, eye + .42f, 2);
                Part(root, side < 0 ? "Post L" : "Post R", post, body);
                Part(root, side < 0 ? "Post Glow L" : "Post Glow R", Sector(.695f, .705f, side * 62 - .8f, side * 62 + .8f, eye - .45f, eye + .4f, 1), glowB);
                Part(root, side < 0 ? "Brow L" : "Brow R", Sector(.62f, .76f, side * 62 - 2.2f, side * 62 + 2.2f, eye + .42f, eye + .47f, 2), body);
            }
            if (skins) { skins.Changed += OnSkin; OnSkin(skins.Current); }
            BuildVignette();
        }

        void OnDestroy()
        {
            if (skins) skins.Changed -= OnSkin;
            DivaTarget.Hit -= OnHit;
            DivaShowDirector.PhaseChanged -= OnPhase;
        }

        void OnHit(DivaTarget t) => flash = Mathf.Max(flash, .55f);
        void OnPhase(ShowPhase p) { if (p == ShowPhase.Ult) flash = 1.6f; else if (p == ShowPhase.Finish || p == ShowPhase.Countdown) flash = 1f; }

        void OnSkin(int index)
        {
            var skin = skins ? skins.CurrentSkin : null;
            if (skin == null) return;
            body.color = Color.Lerp(skin.secondary, new Color(.1f, .08f, .15f), .55f);
            body.SetColor("_BaseColor", body.color);
            baseA = skin.primary * 2.2f;
            baseB = skin.accent * 2.2f;
        }

        void BuildVignette()
        {
            var head = DivaVR.Head;
            if (!head || vignetteMax <= 0) return;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Diva VR Vignette" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + .5f) / size * 2 - 1, v = (y + .5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.38f, .85f, r));
                    px[y * size + x] = new Color32(8, 6, 16, (byte)(a * 255));
                }
            tex.SetPixels32(px); tex.Apply();
            vignette = Unlit(new Color(1, 1, 1, 0), true);
            vignette.mainTexture = tex;
            vignette.SetTexture("_BaseMap", tex);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Comfort Vignette";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(head, false);
            quad.transform.localPosition = new Vector3(0, 0, .3f);
            quad.transform.localScale = new Vector3(1.4f, 1.4f, 1);
            var r2 = quad.GetComponent<MeshRenderer>();
            r2.sharedMaterial = vignette;
            r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r2.receiveShadows = false;
        }

        void LateUpdate()
        {
            UpdateGlow();
            UpdateScreens();
            if (!vignette) return;
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            float yaw = transform.eulerAngles.y;
            float turnRate = haveYaw ? Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / dt : 0;
            lastYaw = yaw; haveYaw = true;
            float speed = loco ? Mathf.Abs(loco.CurrentSpeed) / Mathf.Max(.1f, loco.runSpeed) : 0;
            float target = Mathf.Clamp01(Mathf.InverseLerp(.35f, 1f, speed) * .6f + Mathf.InverseLerp(8, 45, turnRate) * .8f) * vignetteMax;
            // 头穿进树、柱子这些物体时变暗（VR 游戏的通常做法），看不到模型里面；靠近时先暗一点
            float inside = HeadInside();
            if (inside > 0) target = Mathf.Max(target, inside);
            // 变暗快一点、变亮慢一点，避免一闪一闪
            vignetteNow = Mathf.MoveTowards(vignetteNow, target, dt * (target > vignetteNow ? (inside > 0 ? 6f : 1.6f) : .6f));
            var c = vignette.color; c.a = vignetteNow; vignette.color = c;
            vignette.SetColor("_BaseColor", c);
        }

        void UpdateGlow()
        {
            float dt = Time.unscaledDeltaTime;
            flash = Mathf.MoveTowards(flash, 0, dt * 3f);
            float speed = loco ? Mathf.Clamp01(Mathf.Abs(loco.CurrentSpeed) / Mathf.Max(.1f, loco.runSpeed)) : 0;
            bool boost = show && show.Boosting;
            float k = .7f + .5f * speed + (boost ? .35f + .35f * Mathf.Sin(Time.unscaledTime * 14) : 0);
            var white = Color.white * 2.8f;
            if (glowA) { glowA.color = Color.Lerp(baseA * k, white, Mathf.Clamp01(flash)); glowA.SetColor("_BaseColor", glowA.color); }
            if (glowB) { glowB.color = Color.Lerp(baseB * k, white, Mathf.Clamp01(flash * .8f)); glowB.SetColor("_BaseColor", glowB.color); }
            if (screen) screen.SetColor("_BaseColor", Color.Lerp(new Color(.05f, .22f, .28f), new Color(.2f, .5f, .6f), Mathf.Clamp01(flash)));
        }

        // 侧屏：Blender 驾驶舱的两块屏在左右 31°、半径 0.62 米、仪表台顶面上；画面斜着正对眼睛
        void BuildScreens()
        {
            screensBuilt = true;
            if (!blender || DivaUi.Font == null) return;
            float eye = rig ? rig.eyeHeight : .78f;
            foreach (int side in new[] { -1, 1 })
            {
                float a = side * 31 * Mathf.Deg2Rad;
                var pos = new Vector3(Mathf.Sin(a) * .6f, eye - .345f, Mathf.Cos(a) * .6f);
                var go = new GameObject(side < 0 ? "Screen Speed" : "Screen Water", typeof(RectTransform), typeof(Canvas));
                go.layer = 5;
                var rt = (RectTransform)go.transform;
                rt.SetParent(transform, false);
                rt.sizeDelta = new Vector2(400, 220);
                rt.localPosition = pos;
                rt.localRotation = Quaternion.LookRotation(pos - new Vector3(0, eye, 0), Vector3.up);
                rt.localScale = Vector3.one * (.12f / 400f);
                var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.sortingOrder = 51;
                var label = DivaUi.Text(rt, "Label", new Vector2(0, 1), new Vector2(110, -28), new Vector2(200, 40), 30, new Color(.6f, 1f, .95f, .85f), TextAlignmentOptions.Left);
                label.text = side < 0 ? "SPEED" : "WATER";
                var back = DivaUi.Panel(rt, "Bar Back", new Vector2(.5f, 0), new Vector2(0, 34), new Vector2(340, 22), new Color(0, 0, 0, .5f), new Color(0, 0, 0, .5f), 8);
                var bar = DivaUi.Panel(rt, "Bar", new Vector2(.5f, 0), new Vector2(-170, 34), new Vector2(340, 22), Color.white, Color.white, 8);
                bar.rectTransform.pivot = new Vector2(0, .5f);
                var big = DivaUi.Text(rt, "Value", new Vector2(.5f, .5f), new Vector2(0, 10), new Vector2(380, 110), 92, Color.white);
                if (side < 0) { speedText = big; speedBar = bar.rectTransform; bar.color = new Color(1f, .5f, .85f); }
                else { comboText = big; waterBar = bar.rectTransform; bar.color = new Color(.4f, .85f, 1f); }
            }
        }

        void UpdateScreens()
        {
            if (!screensBuilt && DivaUi.Font != null && Time.frameCount > 2) BuildScreens();
            if (!speedText) return;
            float speed = loco ? Mathf.Abs(loco.CurrentSpeed) : 0;
            speedText.text = Mathf.RoundToInt(speed * 3.6f) + "<size=40%> KM/H</size>";
            speedBar.localScale = new Vector3(loco ? Mathf.Clamp01(speed / Mathf.Max(.1f, loco.runSpeed)) : 0, 1, 1);
            float water = demo ? demo.State.Water : 1;
            waterBar.localScale = new Vector3(Mathf.Clamp01(water), 1, 1);
            int combo = show ? show.Combo : 0;
            comboText.text = combo >= 2 ? "x" + combo + "<size=40%> COMBO</size>" : Mathf.RoundToInt(water * 100) + "<size=40%> %</size>";
        }

        static readonly Collider[] near = new Collider[8];
        SphereCollider headProbe;

        /// <summary>
        /// 0 = 头附近没东西；离物体表面 0.45 米以内 = .55；头穿进物体 = .97。驾驶舱和大象自己不算。
        /// 重叠检测按真实网格算（拱门中间可以正常穿过），穿进去用一个 12 厘米的小球判断。
        /// </summary>
        float HeadInside()
        {
            var head = DivaVR.Head;
            if (!head) return 0;
            if (!headProbe)
            {
                var go = new GameObject("Head Probe") { layer = 2 };
                go.transform.SetParent(head, false);
                headProbe = go.AddComponent<SphereCollider>();
                headProbe.isTrigger = true; headProbe.radius = .12f;
            }
            int n = Physics.OverlapSphereNonAlloc(head.position, .45f, near, ~0, QueryTriggerInteraction.Ignore);
            float best = 0;
            for (int i = 0; i < n; i++)
            {
                var c = near[i];
                if (!c || c == headProbe || (loco && c.transform.IsChildOf(loco.travelRoot)) || c.transform.IsChildOf(transform)) continue;
                bool inside = Physics.ComputePenetration(headProbe, head.position, head.rotation, c, c.transform.position, c.transform.rotation, out _, out _);
                best = Mathf.Max(best, inside ? .97f : .55f);
            }
            return best;
        }

        static void Part(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>围着座位的一段弧形实体：半径 r0–r1，角度 a0–a1（0 = 正前方，度），高度 y0–y1。</summary>
        static Mesh Sector(float r0, float r1, float a0, float a1, float y0, float y1, int segments)
        {
            segments = Mathf.Max(1, segments);
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            Vector3 P(float r, float a, float y) { float rad = a * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(rad) * r, y, Mathf.Cos(rad) * r); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
            for (int s = 0; s < segments; s++)
            {
                float a = Mathf.Lerp(a0, a1, s / (float)segments), b = Mathf.Lerp(a0, a1, (s + 1) / (float)segments);
                Quad(P(r0, a, y1), P(r0, b, y1), P(r1, b, y1), P(r1, a, y1));   // 顶
                Quad(P(r0, b, y0), P(r0, a, y0), P(r1, a, y0), P(r1, b, y0));   // 底
                Quad(P(r0, a, y0), P(r0, b, y0), P(r0, b, y1), P(r0, a, y1));   // 朝里（朝着人）
                Quad(P(r1, b, y0), P(r1, a, y0), P(r1, a, y1), P(r1, b, y1));   // 朝外
            }
            Quad(P(r0, a0, y0), P(r0, a0, y1), P(r1, a0, y1), P(r1, a0, y0));    // 两端
            Quad(P(r1, a1, y0), P(r1, a1, y1), P(r0, a1, y1), P(r0, a1, y0));
            var mesh = new Mesh { name = "Cockpit Sector" };
            mesh.SetVertices(verts); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // 材质来自 Assets/DivaVR/Resources（打包时着色器变体才会进包），复制一份改颜色
        static Material Lit(Color c) => Load("DivaVR Cockpit Body", "Universal Render Pipeline/Lit", c);
        static Material Unlit(Color c, bool transparent = false) =>
            transparent ? Load("DivaVR Vignette", "Universal Render Pipeline/Unlit", c) : Load("DivaVR Cockpit Glow", "Universal Render Pipeline/Unlit", c);

        static Material Load(string resource, string fallbackShader, Color c)
        {
            var asset = Resources.Load<Material>(resource);
            var m = asset ? new Material(asset) : new Material(Shader.Find(fallbackShader));
            m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
