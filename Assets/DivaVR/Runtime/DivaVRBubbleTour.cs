using System.Collections.Generic;
using System.Linq;
using DigiPhant;
using Diva.Show;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>
    /// 泡泡开场（每次打开 app 演一次，完全沉浸）：
    /// 1. 一睁眼在一片粉彩梦境里：漂着泡泡和星光。眼前一颗光点："捏一下 出发"。
    /// 2. 捏 → 光点炸开变成一只大泡泡把你裹住，前方开出一道彩虹门，门里是空中俯瞰的糖果镇。
    /// 3. 泡泡载着你穿过门，像无人机一样绕镇子飞一圈（9 米高、慢、不颠簸，泡泡本身就是眼前不动的参照物，不晕）；
    ///    经过每个任务点时它亮起来并浮出名字——这一圈就是在预告赛道。
    /// 4. 泡泡降到起点，大象抬头看你，短短地"嘟"一声扬一下鼻子，泡泡落在它背上"啵"地破掉：你已经坐在驾驶舱里，进入选涂装。
    /// 不需要做任何手势（只有开头捏一下；没捏 20 秒后自己出发）。
    /// </summary>
    [DefaultExecutionOrder(1600)]   // 在 Diva Show（1500）之后、驾驶舱（2500）之前：这里每帧摆泡泡（驾驶舱）的位置
    public class DivaVRBubbleTour : MonoBehaviour
    {
        [Tooltip("飞行高度（米）、最快多快（米/秒）、绕多大一圈（0 = 按任务点算）。")]
        public float height = 10, cruise = 6.5f, lapRadius = 0;
        [Tooltip("泡泡半径（米）。")]
        public float bubbleRadius = 1.7f;

        enum Stage { Dream, Burst, Fly, Land, Pop, Done }
        Stage stage = Stage.Dream;
        float stageTime;

        /// <summary>开场还在接管操作吗（DivaVRInput 问）：开场里人的手势不交给游戏。</summary>
        public bool Driving => stage != Stage.Done || Time.unscaledTime < releaseAt;
        float releaseAt;

        static readonly int PortalCenter = Shader.PropertyToID("_PortalCenter"), PortalNormal = Shader.PropertyToID("_PortalNormal"), PortalRadius = Shader.PropertyToID("_PortalRadius");
        static readonly int BubbleAlpha = Shader.PropertyToID("_Alpha"), BubbleShift = Shader.PropertyToID("_Shift"), Dissolve = Shader.PropertyToID("_Dissolve");
        static readonly Color[] Candy = { new Color(1f, .45f, .8f), new Color(.45f, .95f, .9f), new Color(1f, .85f, .4f), new Color(.7f, .55f, 1f), new Color(.55f, 1f, .6f) };

        DivaVRRig rig;
        DivaVRInput input;
        DivaVRSimulator simulator;
        DivaShowDirector show;
        DivaShowSound sound;
        DivaShowFx fx;
        DigiPhantLocomotion loco;
        DivaDemo demo;
        DivaGameManager game;
        Camera cam;
        bool autopilot, aligned, lastPinch;

        Transform world, fxRoot, dream, bubble, bubbleFloor, portal, ring, ringInner, spark, sparkHalo, title;
        Material dreamMat, bubbleMat, ringMat, innerMat, haloMat, floorMat, titleMat;
        ParticleSystem dreamBubbles, sparkles, trail;
        TrailRenderer sparkTrail;
        AudioSource hum, wind;
        float openness, flash, sparkleNext, dissolve;
        bool portalDone, portalPlaced;

        // 路线：穿门 → 绕一圈 → 收到起点上空 → 降到座位
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> pathLength = new List<float>();
        float traveled, speed, yaw, yawVelocity, landT;
        Vector3 start, forward, center, seatWorld, standPos, flyStart, flyDir;
        Quaternion startRotation;

        // 任务点的光柱和名字
        class Beacon { public Transform pillar, label; public Material mat; public float shown; public Vector3 at; public string text; }
        readonly List<Beacon> beacons = new List<Beacon>();
        bool courseShown;

        // 大象：抬头看人、扬一下鼻子
        Transform root, head;
        Transform[] eyes = new Transform[0], trunk = new Transform[0];
        Vector3 headRest;
        Vector3[] eyeRest = new Vector3[0];
        Quaternion headTurn = Quaternion.identity;
        Quaternion[] eyeTurn = new Quaternion[0];
        float lookWeight, greetTime = -1;
        bool greeted;

        readonly List<GameObject> cockpitParts = new List<GameObject>();
        readonly List<Behaviour> paused = new List<Behaviour>();

        // 提示面板
        Transform prompt;
        CanvasGroup promptGroup;
        TextMeshProUGUI promptTitle, promptSub;
        string promptKey;
        float promptShownAt;

        void Start()
        {
            rig = GetComponent<DivaVRRig>();
            input = GetComponent<DivaVRInput>();
            simulator = GetComponent<DivaVRSimulator>();
            show = FindAnyObjectByType<DivaShowDirector>();
            demo = FindAnyObjectByType<DivaDemo>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            game = FindAnyObjectByType<DivaGameManager>();
            cam = rig ? rig.Cam : null;
            autopilot = DivaVRAutopilot.On;
            if (!rig || !cam || !show || !show.enabled || !demo || !loco || !loco.travelRoot)
            {
                Debug.LogWarning("DIVA_VR tour: missing rig / show / elephant; skipping the opening.");
                Finish();
                return;
            }
            sound = show.Sound;
            root = loco.travelRoot;
            start = root.position;
            startRotation = root.rotation;
            forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            center = start;
            seatWorld = root.TransformPoint(rig.seat);

            world = new GameObject("Diva VR Tour").transform;
            fxRoot = new GameObject("Diva VR Tour FX").transform;
            fx = new DivaShowFx(fxRoot, show.glowMaterial, show.bubbleMaterial);
            MakeTextures();
            rig.Stand(Vector3.zero, Quaternion.identity);   // 先算出站着的眼睛高度，路线的终点要用
            BuildPath();
            FindElephantParts();
            BuildDream();
            BuildBubble();
            BuildPortal();
            BuildSpark();
            BuildPrompt();
            BuildBeacons();

            // 开场时驾驶舱、仪表、全息模型都收起来：人在泡泡里
            ShowCockpit(false);
            yaw = Mathf.Atan2(flyDir.x, flyDir.z) * Mathf.Rad2Deg;
            rig.Stand(standPos, Quaternion.LookRotation(flyDir));
            if (rig.PoseReady) Align();
            Debug.Log($"DIVA_VR tour start: stand {standPos} lap radius {lapRadius:F1} height {height} path {pathLength[pathLength.Count - 1]:F0} m seat {seatWorld}");
            Capture("tour_dream", 2.5f);
        }

        void OnDestroy() { if (stage != Stage.Done) Restore(); }

        // ------------------------------------------------------------------ path
        /// <summary>
        /// 路线（折线，按弧长走）：起点在镇子边上的空中，先直飞穿门，再绕镇子中心（大象起点）飞 300°，
        /// 然后往里收到起点上空，最后竖直降到座位。绕圈半径按任务点算：最远的任务点再往外 8 米。
        /// </summary>
        void BuildPath()
        {
            float r = lapRadius;
            if (r <= 0)
            {
                r = 14;
                if (game != null)
                    foreach (var t in game.tasks)
                        if (t != null && t.landmark) r = Mathf.Max(r, Vector3.ProjectOnPlane(t.landmark.position - center, Vector3.up).magnitude + 4);
                r = Mathf.Clamp(r, 14, 24);
            }
            lapRadius = r;
            // 角度 a 处逆时针（角度减小）的切线是 (cos a, 0, -sin a)；在大象的侧后方 a1 处切线正好等于它的正前方，
            // 圈就在那里结束，然后一边往里收一边再转 90°，最后从它身后沿着正前方到达它头顶（朝向不用再转）
            float behind = Mathf.Atan2(-forward.x, -forward.z);
            float lap = 220 * Mathf.Deg2Rad;
            float a1 = behind + Mathf.PI / 2, a0 = a1 + lap;
            Vector3 P(float a, float rr, float h) => center + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * rr + Vector3.up * h;
            var pts = new List<Vector3>();
            int n = 48;
            // 入口直线：从圈外 6 米直飞到圈上（穿门）
            var tangent0 = new Vector3(Mathf.Cos(a0), 0, -Mathf.Sin(a0));   // 逆时针（角度减小）方向的切线
            var p0 = P(a0, r, height);
            flyDir = tangent0;
            standPos = p0 - tangent0 * 6;
            pts.Add(standPos);
            pts.Add(p0 - tangent0 * 3);
            for (int i = 0; i <= n; i++)
            {
                float a = a0 - lap * i / n;
                pts.Add(P(a, r, height));
            }
            // 往里收：再转 90° 到大象正后方，半径同时缩到 0（起点上空），高度降到座位上方 3 米
            for (int i = 1; i <= 24; i++)
            {
                float k = i / 24f, s = DivaUi.Smooth(k);
                float a = a1 - Mathf.PI / 2 * s;
                pts.Add(P(a, r * (1 - s * .96f), Mathf.Lerp(height, StandHeightAbove(seatWorld) + 3f, s)));
            }
            // 降到座位：座位正上方，高度按站着的眼睛算（落下去的时候眼睛正好在坐着的高度）
            var landing = new Vector3(seatWorld.x, StandHeightAbove(seatWorld), seatWorld.z);
            pts.Add(landing + Vector3.up * 1.5f);
            pts.Add(landing);
            // Catmull-Rom 细分成折线
            path.Clear(); pathLength.Clear();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var pa = pts[Mathf.Max(0, i - 1)]; var pb = pts[i]; var pc = pts[i + 1]; var pd = pts[Mathf.Min(pts.Count - 1, i + 2)];
                for (int k = 0; k < 6; k++)
                {
                    float t = k / 6f, t2 = t * t, t3 = t2 * t;
                    var p = .5f * (2 * pb + (-pa + pc) * t + (2 * pa - 5 * pb + 4 * pc - pd) * t2 + (-pa + 3 * pb - 3 * pc + pd) * t3);
                    path.Add(p);
                }
            }
            path.Add(pts[pts.Count - 1]);
            float len = 0;
            for (int i = 0; i < path.Count; i++) { if (i > 0) len += Vector3.Distance(path[i], path[i - 1]); pathLength.Add(len); }
        }

        // 站在泡泡里时驾驶舱（脚下）该在的高度：让眼睛最后落在坐着的高度（seat + eyeHeight）
        float StandHeightAbove(Vector3 seat) => seat.y + rig.eyeHeight - (cam ? rig.StandingEyeHeight : 1.6f);

        Vector3 PathPoint(float dist, out Vector3 tangent)
        {
            dist = Mathf.Clamp(dist, 0, pathLength[pathLength.Count - 1]);
            int i = pathLength.FindIndex(l => l >= dist);
            if (i <= 0) { tangent = (path[1] - path[0]).normalized; return path[0]; }
            float seg = pathLength[i] - pathLength[i - 1];
            float k = seg > 1e-5f ? (dist - pathLength[i - 1]) / seg : 0;
            tangent = (path[i] - path[i - 1]).normalized;
            return Vector3.Lerp(path[i - 1], path[i], k);
        }

        // ------------------------------------------------------------------ build
        static Texture2D softDot, bubbleTex;
        void MakeTextures()
        {
            if (softDot) return;
            // 肥皂泡：边上一圈彩虹，左上一个高光点，中间透明
            bubbleTex = DivaUi.MakeTexture("Diva Tour Bubble", 128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                if (r > 1) return Color.clear;
                var c = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(y, x) / (2 * Mathf.PI) + r * .3f, 1), .5f, 1);
                float rim = Mathf.Pow(Mathf.Clamp01((r - .6f) / .4f), 2.2f) * (1 - Mathf.Clamp01((r - .96f) / .04f));
                float spot = Mathf.Exp(-((x + .4f) * (x + .4f) + (y - .4f) * (y - .4f)) / .015f);
                float a = Mathf.Clamp01(.04f + rim * .9f + spot);
                return new Color(Mathf.Lerp(c.r, 1, spot), Mathf.Lerp(c.g, 1, spot), Mathf.Lerp(c.b, 1, spot), a);
            });
            softDot = DivaUi.MakeTexture("Diva Tour Dot", 64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Exp(-r * r * 9) + .35f * Mathf.Exp(-r * r * 2.2f);
                return new Color(1, 1, 1, Mathf.Clamp01(a * (1 - Mathf.Clamp01((r - .9f) * 10))));
            });
        }

        Material Glow(Texture texture, Color color)
        {
            Material m;
            if (show.glowMaterial) m = new Material(show.glowMaterial);
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!shader) return null;
                m = new Material(shader);
            }
            m.name = "Diva Tour Glow";
            m.mainTexture = texture;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0);
            Tint(m, color);
            return m;
        }

        static void Tint(Material m, Color c)
        {
            if (!m) return;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        void BuildDream()
        {
            var shared = Resources.Load<Material>("DivaVR Dream Sphere");
            if (!shared) { Debug.LogWarning("DIVA_VR tour: Resources/DivaVR Dream Sphere missing"); return; }
            dreamMat = new Material(shared);
            dreamMat.SetFloat(PortalRadius, 0);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Dream";
            dream = go.transform;
            dream.SetParent(world, false);
            dream.localScale = Vector3.one * 12;   // 半径 6 米：离镇子的东西都够远
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = dreamMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            // 梦里漂的泡泡和星光
            dreamBubbles = Particles("Dream Bubbles", dream, 300, Glow(bubbleTex, new Color(1, 1, 1, .8f)));
            var main = dreamBubbles.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(6, 10);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .2f);
            main.startSize = new ParticleSystem.MinMaxCurve(.12f, .55f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1, 1, 1, .8f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = -.01f;
            var shape = dreamBubbles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .45f;
            var em = dreamBubbles.emission; em.rateOverTime = 20;
            var noise = dreamBubbles.noise; noise.enabled = true; noise.strength = .15f; noise.frequency = .3f;
            dreamBubbles.Emit(120);
            sparkles = Particles("Dream Sparkles", dream, 400, null);
            main = sparkles.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3);
            main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(.02f, .07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .8f, .95f), new Color(.8f, 1f, 1f));
            shape = sparkles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .48f;
            em = sparkles.emission; em.rateOverTime = 40;
            Twinkle(sparkles);
            sparkles.Emit(120);
            if (sound != null && sound.Clip("portal"))
            {
                hum = new GameObject("Dream Hum").AddComponent<AudioSource>();
                hum.transform.SetParent(world, false);
                hum.clip = sound.Clip("portal"); hum.loop = true; hum.playOnAwake = false; hum.pitch = .75f;
                hum.spatialBlend = 0; hum.volume = .4f * sound.volume;
                hum.Play();
            }
        }

        void BuildBubble()
        {
            var shared = Resources.Load<Material>("DivaVR Bubble");
            bubbleMat = shared ? new Material(shared) : null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Ride Bubble";
            bubble = go.transform;
            bubble.SetParent(world, false);
            bubble.localScale = Vector3.one * .001f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = bubbleMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            if (!bubbleMat) go.SetActive(false);
            // 脚下一片淡淡的光（站在泡泡里有个"地"）
            var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(floor.GetComponent<Collider>());
            floor.name = "Bubble Floor";
            bubbleFloor = floor.transform;
            bubbleFloor.SetParent(world, false);
            bubbleFloor.localRotation = Quaternion.Euler(90, 0, 0);
            bubbleFloor.localScale = Vector3.one * 2.2f;
            var fr = floor.GetComponent<MeshRenderer>();
            fr.sharedMaterial = floorMat = Glow(softDot, new Color(1f, .7f, .9f, 0));
            fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 飞的时候泡泡后面拖的星光
            trail = Particles("Bubble Trail", world, 600, null);
            var main = trail.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0, .3f); main.startSize = new ParticleSystem.MinMaxCurve(.04f, .12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .6f, .9f), new Color(.6f, 1f, .95f));
            main.gravityModifier = .02f;
            var shape = trail.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = bubbleRadius * .9f;
            var em = trail.emission; em.rateOverTime = 0;
            Twinkle(trail);
            trail.Emit(1);   // 先画一帧，着色器现在就编译
            if (sound != null && sound.Clip("wind"))
            {
                wind = new GameObject("Wind").AddComponent<AudioSource>();
                wind.transform.SetParent(world, false);
                wind.clip = sound.Clip("wind"); wind.loop = true; wind.playOnAwake = false;
                wind.spatialBlend = 0; wind.volume = 0;
                wind.Play();
            }
        }

        void BuildPortal()
        {
            portal = new GameObject("Portal").transform;
            portal.SetParent(world, false);
            ring = MeshObject("Ring", portal, Torus(1, .034f, 160, 12, u => Color.HSVToRGB(Mathf.Repeat(u * 2, 1), .85f, 1)), ringMat = Glow(Texture2D.whiteTexture, Color.white * 1.3f));
            ringInner = MeshObject("Ring Inner", portal, Torus(1.03f, .009f, 160, 6, u => new Color(1, 1, 1, .4f + .6f * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * Mathf.PI * 12)), 3))), innerMat = Glow(Texture2D.whiteTexture, new Color(1, .95f, 1) * 2.2f));
            // 先画一帧（小到看不见）：着色器现在就编译，出发时不卡
            portal.localScale = Vector3.one * .001f;
            portal.position = Vector3.down * 50;
        }

        void BuildSpark()
        {
            spark = new GameObject("Spark").transform;
            spark.SetParent(world, false);
            sparkHalo = Quad("Halo", spark, haloMat = Glow(softDot, new Color(1f, .5f, .85f, .8f) * 1.6f));
            sparkHalo.localScale = Vector3.one * .3f;
            var core = Quad("Core", spark, Glow(softDot, Color.white * 3));
            core.localScale = Vector3.one * .09f;
            sparkTrail = spark.gameObject.AddComponent<TrailRenderer>();
            sparkTrail.sharedMaterial = Glow(softDot, new Color(1f, .6f, .9f, 1) * 2);
            sparkTrail.time = .35f; sparkTrail.minVertexDistance = .02f;
            sparkTrail.widthCurve = new AnimationCurve(new Keyframe(0, .06f), new Keyframe(1, 0));
            sparkTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sparkTrail.emitting = false;
            spark.gameObject.SetActive(false);
        }

        void BuildPrompt()
        {
            var go = new GameObject("Tour Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            prompt = go.transform;
            prompt.SetParent(world, false);
            var rt = (RectTransform)prompt;
            rt.sizeDelta = new Vector2(1100, 260);
            rt.localScale = Vector3.one * (.9f / 1100f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam; canvas.sortingOrder = 60;
            promptGroup = go.GetComponent<CanvasGroup>();
            promptGroup.alpha = 0; promptGroup.blocksRaycasts = false; promptGroup.interactable = false;
            var ink = new Color(.06f, .05f, .12f);
            DivaUi.Panel(rt, "Plate", new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(1020, 200), DivaUi.WithAlpha(ink, .82f), DivaUi.WithAlpha(ink, .55f), 22).stripeWidth = 8;
            promptTitle = DivaUi.Text(rt, "Title", new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(1000, 90), 66, Color.white);
            DivaUi.Glow(promptTitle, new Color(1, .3f, .7f, .9f));
            promptSub = DivaUi.Text(rt, "Sub", new Vector2(.5f, .5f), new Vector2(0, -36), new Vector2(1000, 60), 36, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);

            // 天上的大标题（穿过门的时候出现）
            var tg = new GameObject("Tour Title", typeof(RectTransform), typeof(Canvas)) { layer = 5 };
            title = tg.transform;
            title.SetParent(world, false);
            var trt = (RectTransform)title;
            trt.sizeDelta = new Vector2(2000, 400);
            trt.localScale = Vector3.one * (14f / 2000f);
            var tc = tg.GetComponent<Canvas>();
            tc.renderMode = RenderMode.WorldSpace; tc.worldCamera = cam; tc.sortingOrder = 55;
            var tt = DivaUi.Text(trt, "Text", new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(2000, 220), 180, Color.white);
            DivaUi.Localize(tt, () => DivaText.T("vr.tour.title"));
            DivaUi.Glow(tt, new Color(1, .4f, .8f, 1), .7f, .4f, .15f);
            var ts = DivaUi.Text(trt, "Sub", new Vector2(.5f, .5f), new Vector2(0, -120), new Vector2(2000, 100), 64, new Color(1f, .9f, 1f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(ts, () => DivaText.T("vr.tour.course"));
            title.position = center + Vector3.up * (height + 9) - flyDir * 2;
            foreach (var x in new[] { tt, ts, promptTitle, promptSub }) x.ForceMeshUpdate();   // 字形现在就生成，飞的时候不卡
            title.gameObject.SetActive(false);
        }

        void BuildBeacons()
        {
            if (game == null) return;
            foreach (var t in game.tasks)
            {
                if (t == null || !t.landmark) continue;
                var b = new Beacon { at = t.landmark.position, text = DivaText.Task(t.label) };
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(pillar.GetComponent<Collider>());
                pillar.name = "Beacon " + t.label;
                pillar.transform.SetParent(world, false);
                pillar.transform.position = b.at + Vector3.up * 6;
                pillar.transform.localScale = new Vector3(1.2f, 6, 1.2f);
                var mr = pillar.GetComponent<MeshRenderer>();
                b.mat = Glow(softDot, new Color(1f, .6f, .9f, 0));
                mr.sharedMaterial = b.mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.pillar = pillar.transform;
                var lg = new GameObject("Label", typeof(RectTransform), typeof(Canvas)) { layer = 5 };
                b.label = lg.transform;
                b.label.SetParent(world, false);
                var lrt = (RectTransform)b.label;
                lrt.sizeDelta = new Vector2(1200, 200);
                lrt.localScale = Vector3.one * (4.5f / 1200f);
                var lc = lg.GetComponent<Canvas>();
                lc.renderMode = RenderMode.WorldSpace; lc.worldCamera = cam; lc.sortingOrder = 56;
                DivaUi.Panel(lrt, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 150), DivaUi.WithAlpha(new Color(.06f, .05f, .12f), .8f), DivaUi.WithAlpha(new Color(.06f, .05f, .12f), .5f), 24).stripeWidth = 8;
                var text = DivaUi.Text(lrt, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 140), 96, Color.white);
                text.text = b.text;
                DivaUi.Glow(text, new Color(1, .35f, .75f, .9f));
                text.ForceMeshUpdate();
                b.label.position = b.at + Vector3.up * 12.5f;
                b.label.localScale = Vector3.zero;
                beacons.Add(b);
            }
        }

        void FindElephantParts()
        {
            var animator = loco.elephantAnimator;
            var skin = animator ? animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).FirstOrDefault() : null;
            if (!skin) return;
            var bones = skin.bones.Where(b => b).Distinct().ToArray();
            head = bones.FirstOrDefault(b => b.name.ToLower().Contains("head"));
            eyes = bones.Where(b => b.name.ToLower().EndsWith("_eye_bone")).ToArray();
            trunk = demo.trunkBones != null ? demo.trunkBones.Where(b => b).ToArray() : new Transform[0];
            if (head) headRest = Quaternion.Inverse(head.rotation) * root.forward;
            eyeRest = eyes.Select(e => Quaternion.Inverse(e.rotation) * root.forward).ToArray();
            eyeTurn = eyes.Select(_ => Quaternion.identity).ToArray();
        }

        // ------------------------------------------------------------------ flow
        void Go(Stage next)
        {
            stage = next;
            stageTime = 0;
            Debug.Log("DIVA_VR tour " + next);
            switch (next)
            {
                case Stage.Burst:
                    // 光点飞到门的位置炸开：门开，泡泡成形
                    sparkTrail.Clear(); sparkTrail.emitting = true;
                    sound?.PlayAt("whoosh", PortalPos(), .9f);
                    break;
                case Stage.Fly:
                    sound?.PlayAt("pop", PortalPos(), .7f);
                    sound?.Play("ready", .5f);
                    fx.Flash(PortalPos(), new Color(1f, .7f, .95f), 1.8f, .45f);
                    Burst(PortalPos(), 140, 2.4f, .05f);
                    flash = 1;
                    traveled = 0; speed = 0;
                    var em = trail.emission; em.rateOverTime = 120;
                    Capture("tour_portal", .6f);
                    Capture("tour_fly", 9f);
                    break;
                case Stage.Land:
                    Capture("tour_land", 2.5f);
                    break;
                case Stage.Pop:
                    PopBubble();
                    break;
            }
        }

        void Update()
        {
            if (stage == Stage.Done) return;
            float dt = Time.unscaledDeltaTime;
            stageTime += dt;
            var raw = input ? input.Current : default;
            bool pinch = raw.shoot && !lastPinch;
            lastPinch = raw.shoot;
            if (!aligned && rig.PoseReady) Align();
            switch (stage)
            {
                case Stage.Dream:
                    // 捏一下出发（没有手部追踪时 20 秒后自己出发）
                    if (aligned && stageTime > 1f && (pinch || autopilot && stageTime > 2.5f || stageTime > 20)) Go(Stage.Burst);
                    break;
                case Stage.Burst:
                    if (stageTime >= .7f) Go(Stage.Fly);
                    break;
                case Stage.Fly:
                    FlyStep(dt);
                    break;
                case Stage.Land:
                    LandStep(dt);
                    break;
                case Stage.Pop:
                    if (stageTime >= 1.1f) Finish();
                    break;
            }
            if (stage != Stage.Done) Animate(dt);
        }

        /// <summary>开场时游戏拿不到人的操作（大象在起点等着）。</summary>
        public DivaVRFrame Drive(DivaVRFrame raw) => default;

        Vector3 PortalPos() => standPos + flyDir * 2.4f + Vector3.up * (rig.StandingEyeHeight - .05f);

        void Align()
        {
            rig.Stand(standPos, Quaternion.LookRotation(flyDir));
            aligned = true;
            if (stage == Stage.Dream && !spark.gameObject.activeSelf)
            {
                spark.gameObject.SetActive(true);
                var h = cam.transform;
                var flat = Flat(h.forward, flyDir);
                spark.position = h.position + flat * .75f - Vector3.up * .08f;
                prompt.position = h.position + flat * 1.3f - Vector3.up * .3f;
                sound?.PlayAt("skill", spark.position, .5f);
            }
        }

        // 沿路线飞：速度按走过 / 剩下的距离平滑加减（起步 1 米/秒²以内，不猛），最快 cruise；到起点上空就开始降落
        void FlyStep(float dt)
        {
            float total = pathLength[pathLength.Count - 1];
            float remaining = total - traveled;
            float want = Mathf.Min(cruise, .6f + traveled * .5f, .5f + remaining * .35f);
            speed = Mathf.MoveTowards(speed, want, dt * 1.6f);
            traveled += speed * dt;
            var p = PathPoint(traveled, out var tangent);
            // 朝向跟着路线的切线；最后 35 米提前慢慢转到大象的正前方（落下去时朝向已经对好，不用再转）
            float targetYaw = remaining < 35 ? root.eulerAngles.y : Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg;
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, .9f, remaining < 35 ? 22 : 16, dt);
            rig.Place(p, Quaternion.Euler(0, yaw, 0));
            if (!courseShown && traveled > 4) { courseShown = true; title.gameObject.SetActive(true); sound?.Play("confirm", .5f); }
            // 快到座位上方了：进入降落（最后 1.6 米竖直）
            if (remaining < 1.7f) { landT = 0; Go(Stage.Land); }
        }

        // 最后一段竖直落到座位（高度按此刻站着的眼睛算：落定时眼睛正好在坐着的高度，破泡时不跳），大象抬头，短短扬一下鼻子
        float landFromY;
        void LandStep(float dt)
        {
            float total = pathLength[pathLength.Count - 1];
            if (landT == 0) landFromY = transform.position.y;
            landT += dt;
            float k = DivaUi.Smooth(landT / 3.2f);
            float dist = Mathf.Lerp(total - 1.7f, total, k);
            var p = PathPoint(dist, out _);
            p.y = Mathf.Lerp(landFromY, StandHeightAbove(seatWorld), k);
            p.x = Mathf.Lerp(p.x, seatWorld.x, k); p.z = Mathf.Lerp(p.z, seatWorld.z, k);
            float targetYaw = root.eulerAngles.y;
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, .6f, 20, dt);
            rig.Place(p, Quaternion.Euler(0, yaw, 0));
            speed = Mathf.MoveTowards(speed, 0, dt * 2);
            if (!greeted && landT > .3f)
            {
                greeted = true; greetTime = 0;
                sound?.PlayAt("toot", head ? head.position + forward * .8f : start, 1.1f);
            }
            if (landT >= 3.4f) Go(Stage.Pop);
        }

        void PopBubble()
        {
            var at = bubble.position;
            fx.Flash(at, new Color(1f, .8f, .95f), 2.2f, .35f);
            Burst(at, 220, 3.2f, .06f);
            fx.Confetti(at + Vector3.up * .5f, Candy, 160);
            sound?.PlayAt("pop", at, 1f);
            sound?.Play("combo", .5f);
            bubble.gameObject.SetActive(false);
            bubbleFloor.gameObject.SetActive(false);
            var em = trail.emission; em.rateOverTime = 0;
            // 坐到座位上：保持现在的朝向（不转），驾驶舱出来
            rig.Board(true);
            ShowCockpit(true);
            flash = 1.2f;
            Capture("tour_cockpit", .8f);
            Debug.Log("DIVA_VR tour boarded");
        }

        void Finish()
        {
            if (stage == Stage.Done) return;
            stage = Stage.Done;
            releaseAt = Time.unscaledTime + .5f;
            Restore();
            DivaVR.ArrivalDone = true;
            Debug.Log("DIVA_VR tour done");
        }

        void Restore()
        {
            ShowCockpit(true);
            if (rig && rig.Detached) rig.Board(true);
            if (world) Destroy(world.gameObject);
            if (fxRoot) Destroy(fxRoot.gameObject, 6);
            CancelInvoke();
        }

        void ShowCockpit(bool on)
        {
            if (!rig) return;
            if (!on)
            {
                foreach (Transform child in rig.transform)
                    if (child.gameObject.activeSelf && (!cam || !cam.transform.IsChildOf(child))) { cockpitParts.Add(child.gameObject); child.gameObject.SetActive(false); }
                foreach (var b in new Behaviour[] { GetComponent<DivaVRCockpit>(), GetComponent<DivaVRAmbience>() })
                    if (b && b.enabled) { paused.Add(b); b.enabled = false; }
                return;
            }
            foreach (var g in cockpitParts) if (g) g.SetActive(true);
            cockpitParts.Clear();
            foreach (var b in paused) if (b) b.enabled = true;
            paused.Clear();
        }

        // ------------------------------------------------------------------ per-frame look
        void Animate(float dt)
        {
            float t = Time.unscaledTime;
            flash = Mathf.MoveTowards(flash, 0, dt * 2.2f);
            var h = cam.transform;

            // 梦境跟着人；星光偶尔"叮"一下
            if (dream) dream.position = transform.position + Vector3.up * 1.2f;
            if (stage == Stage.Dream && t > sparkleNext)
            {
                sparkleNext = t + Random.Range(1.2f, 2.6f);
                sound?.PlayAt("tick", h.position + Random.onUnitSphere * 2.5f, .25f);
            }

            // 光点：在眼前轻轻浮动；出发时飞向门的位置
            if (spark.gameObject.activeSelf)
            {
                if (stage == Stage.Dream)
                {
                    var flat = Flat(h.forward, flyDir);
                    var want = h.position + flat * .75f - Vector3.up * .08f;
                    if (Vector3.Angle(flat, Flat(spark.position - h.position, flat)) > 30 || Vector3.Distance(spark.position, want) > .6f)
                        spark.position = Vector3.Lerp(spark.position, want, 1 - Mathf.Exp(-dt * 2.5f));
                    sparkHalo.localScale = Vector3.one * .3f * (1 + .18f * Mathf.Sin(t * 4.2f));
                }
                else if (stage == Stage.Burst)
                {
                    float k = DivaUi.Smooth(stageTime / .7f);
                    spark.position = Vector3.Lerp(spark.position, PortalPos(), k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .3f;
                    sparkHalo.localScale = Vector3.one * Mathf.Lerp(.3f, .8f, k);
                }
                else spark.gameObject.SetActive(false);
                Billboard(spark);
            }

            // 门：出发时弹开；穿过门平面的一瞬间梦境碎成一片片散开，门在身后收起
            if (stage == Stage.Fly && !portalDone)
            {
                if (!portalPlaced)
                {
                    portalPlaced = true;
                    portal.SetPositionAndRotation(PortalPos(), Quaternion.LookRotation(-flyDir));
                    if (dreamMat) { dreamMat.SetVector(PortalCenter, PortalPos()); dreamMat.SetVector(PortalNormal, -flyDir); }
                }
                float through = Vector3.Dot(transform.position - PortalPos(), flyDir);   // 驾驶舱过门平面多远
                bool passed = through > -.3f;
                openness = passed ? Mathf.Max(0, openness - dt * 2.5f) : Mathf.Max(.02f, DivaUi.EaseOutBack(stageTime / .9f));
                float r = 1.35f * openness * (1 + .012f * Mathf.Sin(t * 1.6f));
                portal.localScale = Vector3.one * Mathf.Max(.001f, r);
                ring.localRotation = Quaternion.Euler(0, 0, t * 14);
                ringInner.localRotation = Quaternion.Euler(0, 0, -t * 22);
                Tint(ringMat, Color.white * 1.3f * (1 + flash * 1.5f));
                Tint(innerMat, new Color(1, .95f, 1) * 2.2f * (1 + flash));
                if (passed)
                {
                    if (dissolve == 0) { sound?.PlayAt("whoosh", transform.position, .6f); Burst(transform.position + flyDir * 2 + Vector3.up * 1.2f, 160, 3f, .06f); }
                    dissolve = Mathf.Min(1.2f, dissolve + dt * 1.1f);
                    if (dreamMat) { dreamMat.SetFloat(PortalRadius, 0); dreamMat.SetFloat(Dissolve, dissolve); }
                    if (dreamBubbles) { var em = dreamBubbles.emission; em.rateOverTime = 0; }
                    if (sparkles) { var em = sparkles.emission; em.rateOverTime = 0; }
                }
                else if (dreamMat) dreamMat.SetFloat(PortalRadius, r * .985f);
                if (dissolve >= 1.2f) { portalDone = true; portal.gameObject.SetActive(false); if (dream) dream.gameObject.SetActive(false); }
            }

            // 泡泡：出发时从光点处长出来包住人；飞的时候轻轻呼吸
            if (bubble.gameObject.activeSelf)
            {
                float size = stage == Stage.Dream ? 0 : stage == Stage.Burst ? DivaUi.EaseOutBack(stageTime / .7f) : 1;
                float breathe = 1 + .012f * Mathf.Sin(t * 1.1f);
                bubble.position = transform.position + Vector3.up * (rig.StandingEyeHeight * .55f);
                bubble.localScale = Vector3.one * Mathf.Max(.001f, bubbleRadius * 2 * size * breathe);
                if (bubbleMat) { bubbleMat.SetFloat(BubbleAlpha, Mathf.Lerp(.75f, .45f, Mathf.Clamp01(stageTime / 3)) + flash * .3f); bubbleMat.SetFloat(BubbleShift, t * .02f); }
                bubbleFloor.position = transform.position + Vector3.up * .01f;
                Tint(floorMat, new Color(1f, .7f, .9f, .22f * size));
                trail.transform.position = bubble.position;
            }
            if (wind) wind.volume = Mathf.Clamp01(speed / cruise) * .35f * (sound != null ? sound.volume : 1);
            if (hum) hum.volume = Mathf.MoveTowards(hum.volume, (stage == Stage.Dream || stage == Stage.Burst ? .4f : .12f) * (sound != null ? sound.volume : 1), dt * .3f);

            // 任务点：泡泡飞近时光柱亮起来、名字弹出来；降落时都收掉
            foreach (var b in beacons)
            {
                float d = Vector3.ProjectOnPlane(b.at - transform.position, Vector3.up).magnitude;
                bool show = stage == Stage.Fly && d < 26;
                if (show && b.shown == 0) { b.shown = t; sound?.PlayAt("confirm", b.at + Vector3.up * 8, .4f); }
                float k = b.shown > 0 ? DivaUi.EaseOutBack((t - b.shown) / .6f) : 0;
                if (stage == Stage.Land || stage == Stage.Pop) k = Mathf.Max(0, 1 - stageTime * 1.5f);
                Tint(b.mat, new Color(1f, .6f, .9f, .35f * k * (.8f + .2f * Mathf.Sin(t * 3 + b.at.x))));
                b.label.localScale = Vector3.one * (4.5f / 1200f) * Mathf.Max(0, k);
                var to = b.label.position - h.position; to.y = 0;
                if (to.sqrMagnitude > 1e-4f) b.label.rotation = Quaternion.LookRotation(to);
            }
            if (title.gameObject.activeSelf)
            {
                var to = title.position - h.position; to.y = 0;
                if (to.sqrMagnitude > 1e-4f) title.rotation = Quaternion.LookRotation(to);
                float age = stageTime;
                title.localScale = Vector3.one * (14f / 2000f) * DivaUi.EaseOutBack(Mathf.Clamp01((traveled - 4) / 2.5f));
                if (stage == Stage.Land) title.gameObject.SetActive(false);
            }
            UpdatePrompt(dt);
        }

        void UpdatePrompt(float dt)
        {
            string key = stage switch
            {
                Stage.Dream => aligned && stageTime > 1f ? "vr.tour.begin" : null,
                Stage.Land => "vr.tour.land",
                _ => null
            };
            if (key != null && key != promptKey)
            {
                promptKey = key; promptShownAt = Time.unscaledTime;
                promptTitle.text = DivaText.T(key);
                promptSub.text = DivaText.T(key + ".b");
            }
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, key != null ? 1 : 0, dt * (key != null ? 3 : 4));
            // 弹出来的时候蹦一下
            float pop = key != null ? DivaUi.EaseOutBack((Time.unscaledTime - promptShownAt) / .5f) : 1;
            prompt.localScale = Vector3.one * (.9f / 1100f) * Mathf.Max(.01f, pop);
            if (promptGroup.alpha <= 0 && key == null) return;
            // 在视线正前方 1.3 米、略低一点；偏出 22° 才挪
            var h = cam.transform;
            var flat = Flat(h.forward, flyDir);
            var want = h.position + flat * 1.3f - Vector3.up * .3f;
            var to = Flat(prompt.position - h.position, flat);
            if (Vector3.Angle(flat, to) > 22 || Mathf.Abs((prompt.position - h.position).magnitude - 1.33f) > .5f || promptGroup.alpha < .05f)
                prompt.position = Vector3.Lerp(prompt.position, want, promptGroup.alpha < .05f ? 1 : 1 - Mathf.Exp(-dt * 3));
            prompt.rotation = Quaternion.LookRotation(prompt.position - h.position);
        }

        void LateUpdate()
        {
            if (stage == Stage.Done || !root || !head) return;
            float dt = Time.unscaledDeltaTime;
            // 降落时大象抬头看人（泡泡在 30 米内就开始找）；坐上去以后不看了
            float d = Vector3.Distance(transform.position, head.position);
            float lookTarget = stage == Stage.Fly && d < 30 ? Mathf.InverseLerp(30, 12, d) : stage == Stage.Land ? 1 : 0;
            lookWeight = Mathf.MoveTowards(lookWeight, lookTarget, dt * .8f);
            PoseGreet(dt);
            PoseLook(dt);
        }

        // 扬一下鼻子打招呼：头抬 18°、鼻子后四节往上卷，1.6 秒内做完
        void PoseGreet(float dt)
        {
            if (greetTime < 0 || trunk.Length == 0) return;
            greetTime += dt;
            float t = greetTime;
            float up = DivaUi.Smooth(t / .45f) * (1 - DivaUi.Smooth((t - 1.0f) / .6f));
            float bounce = t > .45f ? Mathf.Sin((t - .45f) * 12) * Mathf.Exp(-(t - .45f) * 3.5f) : 0;
            var axis = root.right;
            for (int i = 0; i < trunk.Length; i++)
            {
                float angle = (i < 3 ? 0 : 26 * up) + bounce * 4 * (i / (float)trunk.Length);
                trunk[i].rotation = Quaternion.AngleAxis(angle, axis) * trunk[i].rotation;
            }
            head.rotation = Quaternion.AngleAxis(-(18 * up + bounce * 2), axis) * head.rotation;
            if (t > 1.7f) greetTime = -1;
        }

        void PoseLook(float dt)
        {
            if (lookWeight <= 0 && headTurn == Quaternion.identity) return;
            var target = cam.transform.position;
            float k = 1 - Mathf.Exp(-dt * 3);
            var now = head.rotation * headRest;
            var want = LimitedTurn(now, target - head.position, 30, 40);
            headTurn = Quaternion.Slerp(headTurn, Quaternion.Slerp(Quaternion.identity, want, lookWeight), k);
            head.rotation = headTurn * head.rotation;
            for (int i = 0; i < eyes.Length; i++)
            {
                if (!eyes[i]) continue;
                var eyeNow = eyes[i].rotation * eyeRest[i];
                var eyeWant = LimitedTurn(eyeNow, target - eyes[i].position, 22, 20);
                eyeTurn[i] = Quaternion.Slerp(eyeTurn[i], Quaternion.Slerp(Quaternion.identity, eyeWant, lookWeight), 1 - Mathf.Exp(-dt * 8));
                eyes[i].rotation = eyeTurn[i] * eyes[i].rotation;
            }
        }

        static Quaternion LimitedTurn(Vector3 now, Vector3 want, float yawMax, float pitchMax)
        {
            if (now.sqrMagnitude < 1e-6f || want.sqrMagnitude < 1e-6f) return Quaternion.identity;
            float yawNow = Mathf.Atan2(now.x, now.z) * Mathf.Rad2Deg, yawWant = Mathf.Atan2(want.x, want.z) * Mathf.Rad2Deg;
            float pitchNow = Mathf.Asin(Mathf.Clamp(now.normalized.y, -1, 1)) * Mathf.Rad2Deg, pitchWant = Mathf.Asin(Mathf.Clamp(want.normalized.y, -1, 1)) * Mathf.Rad2Deg;
            float yaw = Mathf.Clamp(Mathf.DeltaAngle(yawNow, yawWant), -yawMax, yawMax);
            float pitch = Mathf.Clamp(pitchWant - pitchNow, -pitchMax, pitchMax);
            var flatNow = new Vector3(now.x, 0, now.z);
            var axis = flatNow.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, flatNow).normalized : Vector3.right;
            return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(-pitch, axis);
        }

        // ------------------------------------------------------------------ helpers
        static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0;
            if (v.sqrMagnitude < 1e-4f) { v = fallback; v.y = 0; }
            return v.normalized;
        }

        void Billboard(Transform t)
        {
            var to = t.position - cam.transform.position;
            if (to.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(to);
        }

        Transform MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        Transform Quad(string name, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        ParticleSystem Particles(string name, Transform parent, int max, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material ? material : Glow(softDot, Color.white * 2);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        void Burst(Vector3 at, int count, float speedMax, float size)
        {
            if (!fxRoot) return;
            var ps = Particles("Burst", fxRoot, count, null);
            ps.transform.position = at;
            var main = ps.main;
            main.loop = false; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMax * .3f, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(size * .5f, size);
            main.gravityModifier = .15f;
            var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .05f;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 2.5f; drag.multiplyDragByParticleSize = false; drag.multiplyDragByParticleVelocity = true;
            Twinkle(ps);
            var em = ps.emission; em.rateOverTime = 0;
            var colors = new Gradient();
            colors.SetKeys(Candy.Select((c, i) => new GradientColorKey(c, i / (float)(Candy.Length - 1))).ToArray(), new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
            ps.Emit(count);
            Destroy(ps.gameObject, 2);
        }

        static void Twinkle(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .15f), new GradientAlphaKey(.8f, .7f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var size = ps.sizeOverLifetime; size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .4f), new Keyframe(.2f, 1), new Keyframe(1, .2f)));
        }

        static Mesh Torus(float major, float minor, int segments, int sides, System.Func<float, Color> color)
        {
            var verts = new List<Vector3>(); var colors = new List<Color>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float u = i / (float)segments, a = u * Mathf.PI * 2;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                var c = color(u);
                for (int j = 0; j <= sides; j++)
                {
                    float b = j / (float)sides * Mathf.PI * 2;
                    verts.Add(dir * (major + Mathf.Cos(b) * minor) + Vector3.forward * Mathf.Sin(b) * minor);
                    colors.Add(c);
                    uvs.Add(new Vector2(u, j / (float)sides));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int a = i * row + j, b = a + row;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var mesh = new Mesh { name = "Diva Tour Torus" };
            mesh.SetVertices(verts); mesh.SetColors(colors); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        void Capture(string name, float delay)
        {
            var capture = GetComponent<DivaVRCapture>();
            if (capture) capture.Queue(name, delay);
        }
    }
}
