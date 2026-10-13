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
        [Range(0, 1)] public float vignetteMax = .5f;

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
                if (p.name.Contains("Under Trim") || p.name.Contains("Arm Trim")) m = glowC;   // 仪表台下沿、扶手饰条走 RGB
                // 顶上那道粗"眉"看着压抑（Carl），不装；换成下面细细的发光环框
                if (p.name.StartsWith("Brow")) continue;
                Part(root, p.name, mesh, m);
            }
            Resources.UnloadAsset(json);
            return true;
        }
        float vignetteNow, lastYaw, lastSpeed, accelLevel;
        bool haveYaw;
        // 发光边：跑得越快越亮，冲刺时脉动，命中闪白，大招整圈爆亮（参考守望先锋的打击反馈）
        Color baseA = new Color(1f, .4f, .8f) * 2.2f, baseB = new Color(.3f, .95f, .85f) * 2.2f;
        float flash;
        DivaShowDirector show;
        DivaDemo demo;
        bool blender;
        Transform cockpitRoot; float rumble;
        Material glowC; Light cabinLight;
        bool onFire;
        /// <summary>燃烧状态：灯条烧成橙红；FireLevel（0–1）是火焰条，越满灯条越暖。</summary>
        public void SetFire(bool on) => onFire = on;
        public float FireLevel { get; set; }
        /// <summary>教学演示接管表盘（速度 / 能量 / 水量，0–1；null = 按游戏状态）。</summary>
        [System.NonSerialized] public float? DemoSpeed, DemoCharge, DemoWater;
        /// <summary>驾驶舱抖一下（大招、冲刺、命中）：只抖驾驶舱的壳，不抖人的视角。</summary>
        public void Rumble(float amount) => rumble = Mathf.Max(rumble, amount);
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
            // RGB 氛围条：青 → 蓝 → 紫 → 粉来回流转（D.Va 的色系，不是整圈彩虹）
            glowC = Unlit(new Color(.5f, .9f, 1f) * 2f);
            var root = new GameObject("Cockpit").transform;
            root.SetParent(transform, false);
            cockpitRoot = root;
            screen = Unlit(new Color(.05f, .22f, .28f));
            if (blender = BuildFromBlender(root))
            {
                // 细环框：眼睛上方 0.36 米一圈很细的发光弧，左右各 58°，只是暗示座舱的轮廓
                Part(root, "Halo", Sector(.705f, .72f, -58, 58, eye + .36f, eye + .375f, 24), glowC);
                Part(root, "Halo Shadow", Sector(.70f, .725f, -58, 58, eye + .345f, eye + .36f, 24), body);
                // 顶盖骨架：两根肋从仪表台两侧的柱顶弯过头顶到身后，中间两道横梁发光，身后一圈靠背和一道发光饰条——抬头有东西看，又不挡视线
                Vector3 RibPoint(int sign, float t) => new Vector3(sign * Mathf.Lerp(.42f, .30f, t), eye + .40f + .55f * Mathf.Sin(Mathf.PI * t * .92f), Mathf.Lerp(.60f, -.50f, t));
                foreach (int sign in new[] { -1, 1 })
                    for (int i = 0; i < 10; i++) Tube(root, "Rib", RibPoint(sign, i / 10f), RibPoint(sign, (i + 1) / 10f), .013f, body);
                Tube(root, "Beam Glow", RibPoint(-1, .45f), RibPoint(1, .45f), .008f, glowB);
                Tube(root, "Beam", RibPoint(-1, .75f), RibPoint(1, .75f), .012f, body);
                Tube(root, "Beam Glow 2", RibPoint(-1, .76f), RibPoint(1, .76f), .006f, glowC);
                Part(root, "Seat Back", Sector(.60f, .68f, 135, 225, eye - .52f, eye - .12f, 12), body);
                Part(root, "Seat Trim", Sector(.595f, .605f, 135, 225, eye - .13f, eye - .11f, 12), glowC);
                // 舱内氛围灯：跟着 RGB 条变色，把光洒在仪表台和扶手上
                var lamp = new GameObject("Cabin RGB Light");
                lamp.transform.SetParent(root, false); lamp.transform.localPosition = new Vector3(0, eye - .25f, .45f);
                cabinLight = lamp.AddComponent<Light>(); cabinLight.type = LightType.Point; cabinLight.range = 1.8f; cabinLight.intensity = 1.1f; cabinLight.shadows = LightShadows.None;
                BuildDetails(root, eye);
                if (skins) { skins.Changed += OnSkin; OnSkin(skins.Current); }
                BuildVignette(); return;
            }

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

        // 驾驶舱灯条就在眼前：命中、大招只让它亮一点点、慢慢退（以前一下变白，打靶时一闪一闪）
        void OnHit(DivaTarget t) => flash = Mathf.Max(flash, .3f);
        void OnPhase(ShowPhase p) { if (p == ShowPhase.Ult) flash = .7f; else if (p == ShowPhase.Finish || p == ShowPhase.Countdown) flash = .5f; }

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
            if (cockpitRoot)
            {
                rumble = Mathf.MoveTowards(rumble, 0, Time.unscaledDeltaTime * 1.6f);
                float a = rumble * rumble * .006f, tt = Time.unscaledTime * 27;
                cockpitRoot.localPosition = new Vector3(Mathf.PerlinNoise(tt, .3f) - .5f, Mathf.PerlinNoise(.7f, tt) - .5f, 0) * a;
            }
            if (!vignette) return;
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            float yaw = transform.eulerAngles.y;
            float turnRate = haveYaw ? Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)) / dt : 0;
            lastYaw = yaw; haveYaw = true;
            // 只在转弯时（视野在转，最容易晕）收一点边；直着跑不收。以前跑起来就 0.5、一转弯到 0.8，
            // 比赛里 5 秒一次一明一暗，视野边上像在闪
            float target = Mathf.InverseLerp(12, 40, turnRate) * vignetteMax;
            // 加减速时也轻轻收一点边（前倾控速：身体一动世界就动，这时最容易晕）；匀速跑不收
            if (!loco) loco = FindAnyObjectByType<DigiPhant.DigiPhantLocomotion>();
            if (loco)
            {
                float speed = Mathf.Abs(loco.CurrentSpeed), accel = Mathf.Abs(speed - lastSpeed) / dt; lastSpeed = speed;
                accelLevel = Mathf.Max(accelLevel - dt * .6f, Mathf.InverseLerp(1.5f, 5f, accel));
                target = Mathf.Max(target, accelLevel * vignetteMax * .45f);
            }
            // 头真的穿进树冠、拱门这些看得见的物体时才变暗（VR 游戏的通常做法），看不到模型里面。
            // 以前离任何碰撞体（包括看不见的镜头挡板）45 厘米内就一下暗到一半，骑过树下、拱门下就一暗一暗的
            float inside = HeadInside();
            if (inside > 0) target = Mathf.Max(target, inside);
            vignetteNow = Mathf.MoveTowards(vignetteNow, target, dt * (target > vignetteNow ? (inside > 0 ? 3f : .7f) : .45f));
            var c = vignette.color; c.a = vignetteNow; vignette.color = c;
            vignette.SetColor("_BaseColor", c);
        }

        void UpdateGlow()
        {
            float dt = Time.unscaledDeltaTime;
            flash = Mathf.MoveTowards(flash, 0, dt * 1.2f);
            float speed = loco ? Mathf.Clamp01(Mathf.Abs(loco.CurrentSpeed) / Mathf.Max(.1f, loco.runSpeed)) : 0;
            bool boost = show && show.Boosting;
            // 冲刺时灯条更亮、慢慢呼吸（以前每秒两下明暗，像频闪）
            float k = .7f + .5f * speed + (boost ? .3f + .08f * Mathf.Sin(Time.unscaledTime * 5) : 0);
            float f = 1 + .6f * Mathf.Clamp01(flash);
            // RGB 流转：色相在 0.5（青）到 0.92（粉）之间来回，12 秒一个来回
            float hue = onFire ? Mathf.Lerp(.02f, .1f, .5f + .5f * Mathf.Sin(Time.unscaledTime * 6f)) : Mathf.Lerp(.5f, .92f, .5f + .5f * Mathf.Sin(Time.unscaledTime * .52f));
            var rgb = Color.HSVToRGB(hue, .75f, 1f);
            if (!onFire && FireLevel > 0) rgb = Color.Lerp(rgb, new Color(1f, .45f, .1f), FireLevel * .6f);
            if (glowC) { var cc = rgb * (1.8f + .3f * Mathf.Sin(Time.unscaledTime * 2.1f)); glowC.color = cc; glowC.SetColor("_BaseColor", cc); }
            if (cabinLight) cabinLight.color = rgb;
            // 表盘指针：速度、能量、水量（-60° 空 → +60° 满）；状态灯慢慢闪
            float water = demo ? demo.State.Water : 1, charge = show ? show.Charge / 100f : 0;
            needleValues[0] = DemoSpeed ?? speed; needleValues[1] = DemoCharge ?? charge; needleValues[2] = DemoWater ?? water;
            for (int i = 0; i < 3; i++) if (needles[i]) needles[i].localRotation = Quaternion.Euler(0, Mathf.Lerp(-60, 60, Mathf.Clamp01(needleValues[i])), 0);
            foreach (var (m, c, phase) in blinkers) { var cc = c * (.55f + .45f * Mathf.Max(0, Mathf.Sin(Time.unscaledTime * 1.7f + phase))); m.color = cc; m.SetColor("_BaseColor", cc); }
            if (glowA) { glowA.color = baseA * k * f; glowA.SetColor("_BaseColor", glowA.color); }
            if (glowB) { glowB.color = baseB * k * f; glowB.SetColor("_BaseColor", glowB.color); }
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

        int lastKmh = -1, lastComboShown = -1, lastWaterPct = -1;
        readonly float[] needleValues = new float[3];

        // 侧屏的字只在数值变了才写（整公里数、整百分比、连击数）：每帧重写两块世界画布，头显上是实实在在的 CPU
        void UpdateScreens()
        {
            if (!screensBuilt && DivaUi.Font != null && Time.frameCount > 2) BuildScreens();
            if (!speedText) return;
            float speed = loco ? Mathf.Abs(loco.CurrentSpeed) : 0;
            int kmh = Mathf.RoundToInt(speed * 3.6f);
            if (kmh != lastKmh) { lastKmh = kmh; speedText.text = kmh + "<size=40%> KM/H</size>"; }
            speedBar.localScale = new Vector3(loco ? Mathf.Clamp01(speed / Mathf.Max(.1f, loco.runSpeed)) : 0, 1, 1);
            float water = demo ? demo.State.Water : 1;
            waterBar.localScale = new Vector3(Mathf.Clamp01(water), 1, 1);
            int combo = show ? show.Combo : 0, pct = Mathf.RoundToInt(water * 100);
            if (combo != lastComboShown || (combo < 2 && pct != lastWaterPct))
            {
                lastComboShown = combo; lastWaterPct = pct;
                comboText.text = combo >= 2 ? "x" + combo + "<size=40%> COMBO</size>" : pct + "<size=40%> %</size>";
            }
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
            int n = Physics.OverlapSphereNonAlloc(head.position, headProbe.radius, near, ~0, QueryTriggerInteraction.Ignore);
            float best = 0;
            for (int i = 0; i < n; i++)
            {
                var c = near[i];
                if (!c || c == headProbe || (loco && c.transform.IsChildOf(loco.travelRoot)) || c.transform.IsChildOf(transform)) continue;
                // 只算看得见的东西（自己或子物体有开着的渲染器）；看不见的挡板、触发区不算
                var r = c.GetComponentInChildren<Renderer>();
                if (!r || !r.enabled) continue;
                if (Physics.ComputePenetration(headProbe, head.position, head.rotation, c, c.transform.position, c.transform.rotation, out _, out _))
                    best = .9f;
            }
            return best;
        }

        // ---------- 细节：表盘、按钮、拨杆、舱底板、踏板、安全带、铆钉
        Transform[] needles = new Transform[3];
        readonly System.Collections.Generic.List<(Material m, Color c, float phase)> blinkers = new System.Collections.Generic.List<(Material, Color, float)>();
        Material metal;

        void BuildDetails(Transform root, float eye)
        {
            metal = Lit(new Color(.78f, .78f, .88f));
            metal.SetFloat("_Metallic", .85f); metal.SetFloat("_Smoothness", .72f);
            var dark = Lit(new Color(.08f, .07f, .12f));
            // 三个表盘：仪表台顶面上，斜着对着眼睛。左：速度，中：能量，右：水量
            for (int i = 0; i < 3; i++)
            {
                float a = (i - 1) * 21f;
                var dial = new GameObject("Dial " + i).transform;
                dial.SetParent(root, false);
                dial.localPosition = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * .60f, eye - .352f, Mathf.Cos(a * Mathf.Deg2Rad) * .60f);
                dial.localRotation = Quaternion.Euler(-58, a, 0);
                Disc(dial, "Bezel", .048f, .006f, metal);
                Disc(dial, "Face", .040f, .007f, screen);
                Disc(dial, "Ring", .043f, .002f, glowC, .0045f);   // 细细一圈发光，外面露出金属表圈
                for (int t = 0; t < 7; t++)
                {
                    float ta = -60 + t * 20;
                    var tick = Box(dial, "Tick", new Vector3(Mathf.Sin(ta * Mathf.Deg2Rad) * .032f, .008f, Mathf.Cos(ta * Mathf.Deg2Rad) * .032f), new Vector3(.003f, .002f, .007f), t % 3 == 0 ? glowA : metal);
                    tick.localRotation = Quaternion.Euler(0, ta, 0);
                }
                var pivot = new GameObject("Needle").transform; pivot.SetParent(dial, false); pivot.localPosition = new Vector3(0, .009f, 0);
                Box(pivot, "Hand", new Vector3(0, 0, .016f), new Vector3(.004f, .002f, .034f), glowA);
                Disc(pivot, "Cap", .007f, .004f, metal);
                needles[i] = pivot;
            }
            // 状态按钮（左边一排，三个会慢慢闪）和拨杆开关（右边）
            for (int i = 0; i < 6; i++)
            {
                float a = -42 + i * 4.5f;
                var m = new Material(i % 2 == 0 ? glowA : glowB);
                var btn = Box(root, "Button " + i, new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * .62f, eye - .352f, Mathf.Cos(a * Mathf.Deg2Rad) * .62f), new Vector3(.016f, .008f, .016f), m);
                btn.localRotation = Quaternion.Euler(0, a, 0);
                if (i < 3) blinkers.Add((m, m.color, i * 1.3f));
            }
            for (int i = 0; i < 3; i++)
            {
                float a = 34 + i * 5f;
                var basePos = new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * .62f, eye - .352f, Mathf.Cos(a * Mathf.Deg2Rad) * .62f);
                Disc(root, "Switch Base", .009f, .004f, metal).localPosition = basePos;
                var sw = Tube2(root, "Switch", basePos, basePos + Quaternion.Euler(0, a, 0) * new Vector3(0, .02f, i == 1 ? -.012f : .012f), .003f, metal);
                Disc(root, "Switch Tip", .0045f, .0045f, i == 1 ? glowA : glowB).localPosition = basePos + Quaternion.Euler(0, a, 0) * new Vector3(0, .022f, i == 1 ? -.013f : .013f);
            }
            // 舱底板（座位高度，脚下）+ 两条 RGB 条 + 踏板
            Part(root, "Floor", Sector(.18f, .62f, -70, 70, -.025f, -.005f, 16), dark);
            Part(root, "Floor Strip L", Sector(.20f, .60f, -66, -62, -.004f, .000f, 3), glowC);
            Part(root, "Floor Strip R", Sector(.20f, .60f, 62, 66, -.004f, .000f, 3), glowC);
            foreach (int s in new[] { -1, 1 })
            {
                var pedal = Box(root, s < 0 ? "Pedal L" : "Pedal R", new Vector3(s * .13f, .03f, .46f), new Vector3(.08f, .012f, .12f), metal);
                pedal.localRotation = Quaternion.Euler(-25, 0, 0);
            }
            // 安全带：肩后到腿前两根带子
            foreach (int s in new[] { -1, 1 })
            {
                Tube2(root, "Harness", new Vector3(s * .16f, eye - .08f, -.14f), new Vector3(s * .13f, eye - .46f, .16f), .014f, dark);
                Disc(root, "Harness Buckle", .02f, .01f, metal).localPosition = new Vector3(s * .13f, eye - .46f, .16f);
            }
            // 肋骨上的铆钉
            Vector3 RibPoint(int sign, float t) => new Vector3(sign * Mathf.Lerp(.42f, .30f, t), eye + .40f + .55f * Mathf.Sin(Mathf.PI * t * .92f), Mathf.Lerp(.60f, -.50f, t));
            foreach (int sign in new[] { -1, 1 })
                for (int i = 1; i < 10; i += 2)
                {
                    var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var c = b.GetComponent<Collider>(); if (c) Destroy(c);
                    b.name = "Rivet"; b.transform.SetParent(root, false); b.transform.localPosition = RibPoint(sign, i / 10f); b.transform.localScale = Vector3.one * .028f;
                    var r = b.GetComponent<MeshRenderer>(); r.sharedMaterial = metal; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
        }

        static Transform Disc(Transform parent, string name, float radius, float height, Material m, float y = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, y, 0); go.transform.localScale = new Vector3(radius * 2, height * .5f, radius * 2);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        static Transform Box(Transform parent, string name, Vector3 pos, Vector3 size, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        static Transform Tube2(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m) { Tube(parent, name, a, b, radius, m); return parent.GetChild(parent.childCount - 1); }

        /// <summary>两点之间一根圆管。</summary>
        static void Tube(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = (a + b) * .5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            go.transform.localScale = new Vector3(radius * 2, (b - a).magnitude * .5f, radius * 2);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
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
