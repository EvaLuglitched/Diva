using System.Collections.Generic;
using System.Linq;
using DigiPhant;
using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 泡泡开场（每次打开 app 演一次，完全沉浸），镜头顺序照 Eva 的开场（先全镇，再一步步推近），做成动画片的分镜：
    /// 1. 梦境：粉彩、肥皂泡、星光，眼前一颗光点 "捏一下 出发"。
    /// 2. 捏 → 光点飞出去开出一道门，门里是高空俯瞰的糖果镇；梦境像泡泡一样碎开，人已经悬在小镇上空（全景）。
    ///    "糖果镇"大标题，任务点一个个亮起来（赛道预告），镜头慢慢推近。
    /// 3. 泡泡转场 → 中景：大象身后上方，看它站在起跑线。
    /// 4. 泡泡转场 → 特写：大象正前方偏侧的低角度，它看着你，扬一下鼻子 "嘟" 一声（很短）。
    /// 5. 泡泡 "啵" 地破掉 → 驾驶舱，进入选涂装。
    /// 舒适：每个镜头只有很慢的直线推近（不超过 0.6 米/秒，不转），镜头之间用泡泡转场切换（不连续飞，不晕）；
    /// 人身边一直有一只透明泡泡当参照物。只有开头捏一下，没捏 20 秒后自己出发。
    /// </summary>
    [DefaultExecutionOrder(1600)]   // 在 Diva Show（1500）之后、驾驶舱（2500）之前：这里每帧摆驾驶舱（人）的位置
    public class DivaVRBubbleTour : MonoBehaviour
    {
        [Tooltip("泡泡半径（米）。")]
        public float bubbleRadius = 1.7f;

        enum Stage { Dream, Burst, Open, Wide, Mid, Hero, Pop, Done }
        Stage stage = Stage.Dream;
        float stageTime;

        /// <summary>开场还在接管操作吗（DivaVRInput 问）：开场里人的手势不交给游戏。</summary>
        public bool Driving => stage != Stage.Done || Time.unscaledTime < releaseAt;
        float releaseAt;

        static readonly int PortalCenter = Shader.PropertyToID("_PortalCenter"), PortalNormal = Shader.PropertyToID("_PortalNormal"), PortalRadius = Shader.PropertyToID("_PortalRadius");
        static readonly int BubbleAlpha = Shader.PropertyToID("_Alpha"), BubbleShift = Shader.PropertyToID("_Shift"), Dissolve = Shader.PropertyToID("_Dissolve");
        static readonly Color[] Candy = { new Color(1f, .45f, .8f), new Color(.45f, .95f, .9f), new Color(1f, .85f, .4f), new Color(.7f, .55f, 1f), new Color(.55f, 1f, .6f) };
        // 切镜头时眼前淡到的颜色：和夜晚的小镇差不多暗的薰衣草紫。不用亮粉色：头显里整个视野一下变亮就是"闪屏"
        static readonly Color Dusk = new Color(.2f, .15f, .33f);
        const float WipeIn = .4f, WipeOut = .55f;

        DivaVRRig rig;
        DivaVRInput input;
        DivaShowDirector show;
        DivaShowSound sound;
        DivaShowFx fx;
        DigiPhantLocomotion loco;
        DivaDemo demo;
        DivaGameManager game;
        Camera cam;
        bool autopilot, aligned, lastPinch;

        Transform world, fxRoot, dream, bubble, bubbleFloor, portal, ring, ringInner, spark, sparkHalo, title, wipe;
        Material dreamMat, bubbleMat, ringMat, innerMat, floorMat, wipeMat;
        ParticleSystem dreamBubbles, sparkles;
        TrailRenderer sparkTrail;
        AudioSource hum;
        float openness, flash, sparkleNext, dissolve;

        // 镜头：眼睛从 from 慢慢推到 to，朝向整段不变（看 look）
        struct Shot { public Vector3 from, to, look; public float seconds; }
        Shot wide, mid, hero;
        Vector3 start, forward, centre, portalPos;
        Quaternion shotRot;
        float wipeAt = -1;          // 泡泡转场：这个时间点切镜头，前后各 0.3 秒盖住
        Stage wipeTo;

        // 任务点的光柱和名字（全景时一个个亮起来）
        class Beacon { public Transform pillar, label; public Material mat; public float shown; public Vector3 at; }
        readonly List<Beacon> beacons = new List<Beacon>();

        // 大象：看人、扬一下鼻子
        Transform root, head;
        Transform[] eyes = new Transform[0], trunk = new Transform[0];
        Vector3 headRest;
        Vector3[] eyeRest = new Vector3[0];
        Quaternion headTurn = Quaternion.identity;
        Quaternion[] eyeTurn = new Quaternion[0];
        float lookWeight, greetTime = -1;
        bool greeted, popBurst, menuPlaced; float menuShownAt;
        readonly List<GameObject> hiddenClouds = new List<GameObject>();
        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }
        int fireworkNext;

        readonly List<GameObject> cockpitParts = new List<GameObject>();
        readonly List<Behaviour> paused = new List<Behaviour>();

        // 提示面板
        Transform prompt;
        CanvasGroup promptGroup;
        TextMeshProUGUI promptTitle, promptSub, menuHint, menuStart, menuLang;
        // 糖果招牌的 3D 部件：吊着牌子的气球和绳子（跟着 prompt 走），头正对着的气球可以捏爆
        DivaSlant signFace, signBand; Transform signRoot; readonly List<Transform> balloons = new List<Transform>(), strings = new List<Transform>(); int balloonHover = -1;
        RectTransform btnStart, btnLang, menuHovered;
        DivaSlant plateStart, plateLang;
        string promptKey, menuLangText;
        float promptShownAt;

        void Start()
        {
            rig = GetComponent<DivaVRRig>();
            input = GetComponent<DivaVRInput>();
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
            forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            centre = TownCentre();

            world = new GameObject("Diva VR Tour").transform;
            fxRoot = new GameObject("Diva VR Tour FX").transform;
            fx = new DivaShowFx(fxRoot, show.glowMaterial, show.bubbleMaterial);
            MakeTextures();
            PlanShots();
            FindElephantParts();
            BuildDream();
            BuildBubble();
            BuildPortal();
            BuildSpark();
            BuildPrompt();
            BuildBeacons();
            BuildWipe();
            Prewarm();
            // 全景镜头的路上有 Eva 的云（漂在 30 多米高）会糊在脸上：开场期间把离镜头路径 20 米内的云藏起来
            foreach (var f in FindObjectsByType<DivaFloat>(FindObjectsSortMode.None))
            {
                bool cloud = f.name.Contains("Cloud") || (f.transform.parent && f.transform.parent.name.Contains("Cloud"));
                if (!cloud) continue;
                float d = Mathf.Min(DistanceToSegment(f.transform.position, wide.from, wide.to), DistanceToSegment(f.transform.position, mid.from, mid.to));
                if (d < 20) { hiddenClouds.Add(f.gameObject); f.gameObject.SetActive(false); }
            }
            if (hiddenClouds.Count > 0) Debug.Log($"DIVA_VR tour: {hiddenClouds.Count} clouds hidden near the shots");
            // 开头直接是小镇全景 + 菜单：梦境球、门、光点都不要（Carl：像一团云挡着）
            if (dream) dream.gameObject.SetActive(false);
            if (portal) portal.gameObject.SetActive(false);
            if (spark) spark.gameObject.SetActive(false);
            dissolve = 1.2f;

            // 开场时驾驶舱、仪表、全息模型、指引都收起来：人在泡泡里
            ShowCockpit(false);
            shotRot = Quaternion.LookRotation(Flat(wide.look - wide.from, forward));
            PlaceEye(wide.from, shotRot);
            if (rig.PoseReady) Align();
            Debug.Log($"DIVA_VR tour start: centre {centre} wide {wide.from} mid {mid.from} hero {hero.from}");
            Capture("tour_dream", 2.5f);
        }

        void OnDestroy() { if (stage != Stage.Done) Restore(); }

        /// <summary>小镇中心：Eva 开场的中心点（她的全景就是绕它转），没有就用任务点和起点的中心。</summary>
        Vector3 TownCentre()
        {
            var intro = FindAnyObjectByType<DivaIntro>(FindObjectsInactive.Include);
            if (intro) return new Vector3(intro.transform.position.x, start.y, intro.transform.position.z);
            var pts = new List<Vector3> { start };
            if (game != null) pts.AddRange(game.tasks.Where(t => t != null && t.landmark).Select(t => t.landmark.position));
            var c = pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
            return new Vector3(c.x, start.y, c.z);
        }

        /// <summary>三个镜头（眼睛的位置）。全景在大象身后高处、越过它看向全镇；中景在它身后上方；特写在它正前方偏 35° 的低处。</summary>
        void PlanShots()
        {
            var away = Vector3.ProjectOnPlane(start - centre, Vector3.up);
            var back = away.sqrMagnitude > 4 ? away.normalized : -forward;   // 从镇中心往大象那边再往外
            var side = Vector3.Cross(Vector3.up, back);
            var wideFrom = centre + back * 60 + side * 12 + Vector3.up * 31;
            var wideLook = centre + Vector3.up * 2;
            // 无人机从高空往镇子俯冲 14 米（缓入缓出，直线、不转），比之前的 4.5 米有冲劲得多
            wide = new Shot { from = wideFrom, to = wideFrom + (wideLook - wideFrom).normalized * 14f, look = wideLook, seconds = 7.5f };
            // 中景：大象身后上方，从正后方往两边试，挑第一个看大象不被广告牌、树挡住的角度
            var blockers = FindObjectsByType<Renderer>().Where(r => r.enabled && !r.transform.IsChildOf(root) && !r.transform.IsChildOf(rig.transform) && !(r is ParticleSystemRenderer)
                                                                    && r.bounds.size.magnitude < 60 && r.bounds.max.y > start.y + .5f).ToArray();
            var midLook = start + Vector3.up * 2.5f;
            var midFrom = PickClear(new[] { 18f, -18, 38, -38, 60, -60, 85, -85 }, a => start + Quaternion.AngleAxis(a, Vector3.up) * -forward * 15 + Vector3.up * 7, midLook, blockers);
            // 低空掠过：从后上方朝大象俯冲 11 米
            mid = new Shot { from = midFrom, to = midFrom + (midLook - midFrom).normalized * 11, look = midLook, seconds = 4.6f };
            // 特写：正前方偏一侧的低处
            var heroLook = start + forward * 1.4f + Vector3.up * 2.6f;
            var heroFrom = PickClear(new[] { 35f, -35, 55, -55, 20, -20 }, a => start + Quaternion.AngleAxis(a, Vector3.up) * forward * 7.2f + Vector3.up * 2.1f, heroLook, blockers);
            hero = new Shot { from = heroFrom, to = heroFrom + Vector3.ProjectOnPlane(heroLook - heroFrom, Vector3.up).normalized * 2.6f, look = heroLook, seconds = 4.2f };
            portalPos = wide.from + Flat(wide.look - wide.from, forward) * 2.6f - Vector3.up * .05f;
        }

        /// <summary>按顺序试几个角度，返回第一个从眼睛到 look 没被挡住的位置（都挡住就用第一个）。</summary>
        static Vector3 PickClear(float[] angles, System.Func<float, Vector3> at, Vector3 look, Renderer[] blockers)
        {
            foreach (float a in angles)
            {
                var eye = at(a);
                var side = Vector3.Cross(Vector3.up, look - eye).normalized;
                bool blocked = false;
                // 看大象的身子、脚和两边都不被挡；镜头旁边 3.5 米内也不能有东西（画面角上一大块黑影）
                foreach (var p in new[] { look, look - Vector3.up * 2.2f, look + side * 2.5f, look - side * 2.5f })
                {
                    var ray = new Ray(eye, (p - eye).normalized);
                    float len = Vector3.Distance(eye, p) - 2.5f;   // 大象自己附近的不算
                    foreach (var r in blockers)
                        if (r && r.bounds.IntersectRay(ray, out float d) && d < len) { blocked = true; break; }
                    if (blocked) break;
                }
                if (!blocked)
                    foreach (var r in blockers)
                        if (r && r.bounds.SqrDistance(eye) < 3.5f * 3.5f) { blocked = true; break; }
                if (!blocked) return eye;
            }
            return at(angles[0]);
        }

        /// <summary>
        /// 开场前先把三个镜头各画一次（很小的图、看不见）：镇子里所有着色器现在就准备好，门打开、镜头切换时不卡
        /// （编辑器里第一次看到全镇卡了 5 秒；头显上着色器第一次用到时也要现编）。
        /// </summary>
        void Prewarm()
        {
            var go = new GameObject("Diva Tour Prewarm");
            var c = go.AddComponent<Camera>();
            c.enabled = false; c.stereoTargetEye = StereoTargetEyeMask.None;
            c.cullingMask = cam.cullingMask; c.clearFlags = cam.clearFlags; c.backgroundColor = cam.backgroundColor;
            c.fieldOfView = 100; c.nearClipPlane = .05f; c.farClipPlane = cam.farClipPlane;
            var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(c);
            var mainData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
            data.renderPostProcessing = mainData.renderPostProcessing;
            var rt = new RenderTexture(256, 144, 24);
            c.targetTexture = rt;
            // 第一张带着梦境球（它的着色器也要准备），后面把球藏起来才看得到镇子
            for (int i = -1; i < 3; i++)
            {
                var s = i < 0 ? wide : i == 0 ? wide : i == 1 ? mid : hero;
                if (dream) dream.gameObject.SetActive(i < 0);
                c.transform.SetPositionAndRotation(s.from, Quaternion.LookRotation(s.look - s.from));
                c.Render();
            }
            if (dream) dream.gameObject.SetActive(true);
            c.targetTexture = null;
            rt.Release();
            Destroy(rt); Destroy(go);
        }

        /// <summary>把人（眼睛）放到 eye、朝向 rot：驾驶舱（脚下）在眼睛下方站着的高度。</summary>
        void PlaceEye(Vector3 eye, Quaternion rot)
        {
            float h = rig.StandingEyeHeight;
            if (!rig.Detached) rig.Stand(eye - Vector3.up * h, rot);
            else rig.Place(eye - Vector3.up * h, rot);
        }

        // ------------------------------------------------------------------ build
        static Texture2D softDot, bubbleTex;
        void MakeTextures()
        {
            if (softDot) return;
            softDot = DivaUi.MakeTexture("Diva Tour Dot", 64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Exp(-r * r * 9) + .35f * Mathf.Exp(-r * r * 2.2f);
                return new Color(1, 1, 1, Mathf.Clamp01(a * (1 - Mathf.Clamp01((r - .9f) * 10))));
            });
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
            dreamMat.SetVector(PortalCenter, portalPos);
            dreamMat.SetVector(PortalNormal, -Flat(wide.look - wide.from, forward));
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Dream";
            dream = go.transform;
            dream.SetParent(world, false);
            dream.position = wide.from;
            dream.localScale = Vector3.one * 12;   // 半径 6 米
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = dreamMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            // 梦里漂的肥皂泡和星光
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
        }

        void BuildPortal()
        {
            portal = new GameObject("Portal").transform;
            portal.SetParent(world, false);
            ring = MeshObject("Ring", portal, Torus(1, .034f, 160, 12, u => Color.HSVToRGB(Mathf.Repeat(u * 2, 1), .85f, 1)), ringMat = Glow(Texture2D.whiteTexture, Color.white * 1.3f));
            ringInner = MeshObject("Ring Inner", portal, Torus(1.03f, .009f, 160, 6, u => new Color(1, 1, 1, .4f + .6f * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * Mathf.PI * 12)), 3))), innerMat = Glow(Texture2D.whiteTexture, new Color(1, .95f, 1) * 2.2f));
            // 先画一帧（小到看不见、放在脚下很远）：着色器现在就编译，出发时不卡
            portal.localScale = Vector3.one * .001f;
            portal.position = wide.from - Vector3.up * 50;
        }

        void BuildSpark()
        {
            spark = new GameObject("Spark").transform;
            spark.SetParent(world, false);
            sparkHalo = Quad("Halo", spark, Glow(softDot, new Color(1f, .5f, .85f, .8f) * 1.6f));
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
            // 进入菜单：标题、副标题、[开始] [EN / 中文]，看着按钮捏一下；不看按钮直接捏也是开始
            rt.sizeDelta = new Vector2(1100, 660);
            rt.localScale = Vector3.one * (1.1f / 1100f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam; canvas.sortingOrder = 60;
            promptGroup = go.GetComponent<CanvasGroup>();
            promptGroup.alpha = 0; promptGroup.blocksRaycasts = false; promptGroup.interactable = false;
            // 糖果招牌（参考 PvZ 的墓碑菜单：菜单是立在世界里的一块实物）：粉色厚边 + 奶油面板 + 顶上一条粉色标题带，
            // 上面吊着五个气球（捏一下会爆，好玩）；四个站不用文字列，直接在下面的镇子里一个个亮起信标
            DivaUi.Panel(rt, "Rim", new Vector2(.5f, .5f), new Vector2(0, -10), new Vector2(1080, 664), new Color(.95f, .35f, .68f), new Color(.72f, .18f, .48f), 30);
            signFace = DivaUi.Panel(rt, "Face", new Vector2(.5f, .5f), new Vector2(0, 4), new Vector2(1040, 630), new Color(1f, .97f, .99f), new Color(.99f, .9f, .95f), 30);
            signBand = DivaUi.Panel(rt, "Band", new Vector2(.5f, 1), new Vector2(0, -80), new Vector2(980, 132), new Color(1f, .45f, .78f), new Color(.9f, .3f, .66f), 24);
            promptTitle = DivaUi.Text(rt, "Title", new Vector2(.5f, 1), new Vector2(0, -80), new Vector2(960, 120), 92, Color.white);
            DivaUi.Glow(promptTitle, new Color(1, .3f, .7f, .9f));
            promptSub = DivaUi.Text(rt, "Sub", new Vector2(.5f, 1), new Vector2(0, -226), new Vector2(980, 50), 32, new Color(.1f, .5f, .55f), TextAlignmentOptions.Center, false);   // 锚点在顶：-226 是文字的上边，正好在标题带（到 -212）下面
            // START：一块大糖果板，整块牌子唯一的主按钮
            btnStart = DivaUi.Rect(rt, "Start", new Vector2(.5f, 0), new Vector2(0, 180), new Vector2(780, 190));
            plateStart = DivaUi.Panel(btnStart, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(780, 190), new Color(1f, .38f, .74f), new Color(.82f, .22f, .6f), 22);
            menuStart = DivaUi.Text(btnStart, "Label", new Vector2(.5f, .5f), new Vector2(0, 4), new Vector2(760, 150), 70, Color.white);
            DivaUi.Glow(menuStart, new Color(1, .2f, .6f, .8f));
            btnLang = DivaUi.Rect(rt, "Lang", new Vector2(.5f, 0), new Vector2(0, 86), new Vector2(300, 76));
            plateLang = DivaUi.Panel(btnLang, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 76), new Color(.26f, .22f, .4f), new Color(.17f, .14f, .28f), 16);
            menuLang = DivaUi.Text(btnLang, "Label", new Vector2(.5f, .5f), new Vector2(0, 1), new Vector2(280, 66), 34, Color.white);
            menuHint = DivaUi.Text(rt, "Hint", new Vector2(.5f, 0), new Vector2(0, 16), new Vector2(1000, 56), 24, new Color(.16f, .13f, .25f, .7f), TextAlignmentOptions.Center, false);
            BuildBalloons();

            // 全景里的大标题：浮在镇子上空，正对着人
            var tg = new GameObject("Tour Title", typeof(RectTransform), typeof(Canvas)) { layer = 5 };
            title = tg.transform;
            title.SetParent(world, false);
            var trt = (RectTransform)title;
            trt.sizeDelta = new Vector2(2000, 400);
            var tc = tg.GetComponent<Canvas>();
            tc.renderMode = RenderMode.WorldSpace; tc.worldCamera = cam; tc.sortingOrder = 55;
            var tt = DivaUi.Text(trt, "Text", new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(2000, 220), 180, Color.white);
            DivaUi.Localize(tt, () => DivaText.T("vr.tour.title"));
            DivaUi.Glow(tt, new Color(1, .4f, .8f, 1), .7f, .4f, .15f);
            var ts = DivaUi.Text(trt, "Sub", new Vector2(.5f, .5f), new Vector2(0, -120), new Vector2(2000, 100), 64, new Color(1f, .9f, 1f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(ts, () => DivaText.T("vr.tour.course"));
            // 浮在镇子中心上空，正对全景镜头（往下看镇子时正好在视线里）
            var dir = Flat(wide.look - wide.from, forward);
            title.position = centre + Vector3.up * 17 - dir * 6;
            title.rotation = Quaternion.LookRotation(title.position - wide.from);
            title.localScale = Vector3.zero;
            foreach (var x in new[] { tt, ts, promptTitle, promptSub }) x.ForceMeshUpdate();   // 字形现在就生成，演的时候不卡
        }

        void BuildBeacons()
        {
            if (game == null) return;
            foreach (var t in game.tasks)
            {
                if (t == null || !t.landmark) continue;
                var b = new Beacon { at = t.landmark.position };
                var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(pillar.GetComponent<Collider>());
                pillar.name = "Beacon " + t.label;
                pillar.transform.SetParent(world, false);
                pillar.transform.position = b.at + Vector3.up * 6;
                pillar.transform.localScale = new Vector3(1.4f, 6, 1.4f);
                var mr = pillar.GetComponent<MeshRenderer>();
                b.mat = Glow(softDot, new Color(1f, .6f, .9f, 0));
                mr.sharedMaterial = b.mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                b.pillar = pillar.transform;
                var lg = new GameObject("Label", typeof(RectTransform), typeof(Canvas)) { layer = 5 };
                b.label = lg.transform;
                b.label.SetParent(world, false);
                var lrt = (RectTransform)b.label;
                lrt.sizeDelta = new Vector2(1200, 200);
                var lc = lg.GetComponent<Canvas>();
                lc.renderMode = RenderMode.WorldSpace; lc.worldCamera = cam; lc.sortingOrder = 56;
                DivaUi.Panel(lrt, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 150), DivaUi.WithAlpha(new Color(.06f, .05f, .12f), .8f), DivaUi.WithAlpha(new Color(.06f, .05f, .12f), .5f), 24).stripeWidth = 8;
                var text = DivaUi.Text(lrt, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1100, 140), 96, Color.white);
                text.text = (beacons.Count + 1) + ". " + DivaText.Task(t.label);
                DivaUi.Glow(text, new Color(1, .35f, .75f, .9f));
                text.ForceMeshUpdate();
                b.label.position = b.at + Vector3.up * 10f;
                b.label.localScale = Vector3.zero;
                beacons.Add(b);
            }
        }

        // 转场：包住头的一个小球（半径 0.2 米），切镜头的那一下全盖住。用球不用贴脸的方片：视野多大都盖得住，没有边
        void BuildWipe()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Shot Fade";
            wipe = go.transform;
            wipe.SetParent(cam.transform, false);
            wipe.localPosition = Vector3.zero;
            wipe.localScale = Vector3.one * .4f;
            var shared = Resources.Load<Material>("DivaVR Vignette");
            wipeMat = shared ? new Material(shared) : null;
            if (wipeMat)
            {
                wipeMat.mainTexture = Texture2D.whiteTexture;
                if (wipeMat.HasProperty("_BaseMap")) wipeMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (wipeMat.HasProperty("_Cull")) wipeMat.SetFloat("_Cull", 0);
                Tint(wipeMat, new Color(Dusk.r, Dusk.g, Dusk.b, 0));
            }
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = wipeMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!wipeMat) go.SetActive(false);
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
                    sparkTrail.Clear(); sparkTrail.emitting = true;
                    sound?.PlayAt("whoosh", portalPos, .9f);
                    break;
                case Stage.Open:
                    // 门开：门里是高空俯瞰的糖果镇
                    spark.gameObject.SetActive(false);
                    portal.SetPositionAndRotation(portalPos, Quaternion.LookRotation(-Flat(wide.look - wide.from, forward)));
                    sound?.PlayAt("pop", portalPos, .7f);
                    sound?.Play("ready", .5f);
                    // 门开：只放一些泡泡，不要闪光（头显里一闪很刺眼）
                    Burst(portalPos, 90, 2.0f, .05f);
                    flash = .35f;
                    sound?.Play("boom", .6f, .8f);
                    break;
                case Stage.Wide:
                    sound?.Play("boom", .9f);
                    fireworkNext = 0;
                    Capture("tour_wide", 4f);
                    break;
                case Stage.Mid:
                    shotRot = Quaternion.LookRotation(Flat(mid.look - mid.from, forward));
                    Capture("tour_mid", 2f);
                    break;
                case Stage.Hero:
                    shotRot = Quaternion.LookRotation(Flat(hero.look - hero.from, forward));
                    Capture("tour_hero", 1.6f);
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
            stageTime += Mathf.Min(dt, .1f);   // 加载卡顿的一帧不算进阶段时间（否则一帧就把菜单 / 转场跳过去）
            var raw = input ? input.Current : default;
            bool pinch = raw.shoot && !lastPinch;
            lastPinch = raw.shoot;
            if (!aligned && rig.PoseReady) Align();
            switch (stage)
            {
                case Stage.Dream:
                    // 捏一下出发（没有手部追踪时 20 秒后自己出发）
                    PlaceEye(wide.from, shotRot);
                    menuHovered = cam ? MenuHitDir(cam.transform.forward) : null;
                    // 菜单出来 1.5 秒内的捏合不算（刚进来手还在动）；只有按到按钮才有反应：语言键切语言，开始键才开始
                    bool armed = aligned && menuPlaced && Time.unscaledTime - menuShownAt > 1.5f;
                    var hit = pinch && armed ? MenuHit(raw) : null;
                    if (hit == btnLang) { DivaText.Toggle(); sound?.Play("tick", .5f); }
                    else if (pinch && armed && hit == null && balloonHover >= 0) PopBalloon(balloonHover);
                    if (hit == btnStart || autopilot && stageTime > 2.5f || stageTime > 150) { sound?.Play("confirm", .6f); Go(Stage.Wide); }   // 150 秒兜底只救卡住的开场（手没追踪到），不能在玩家还在选语言时替他开始
                    break;
                case Stage.Burst:
                    PlaceEye(wide.from, shotRot);
                    if (stageTime >= .7f) Go(Stage.Open);
                    break;
                case Stage.Open:
                    PlaceEye(wide.from, shotRot);
                    if (stageTime >= 1.4f) Go(Stage.Wide);
                    break;
                case Stage.Wide:
                    PlaceShot(wide);
                    if (stageTime >= wide.seconds - WipeIn) StartWipe(Stage.Mid);
                    break;
                case Stage.Mid:
                    PlaceShot(mid);
                    if (stageTime >= mid.seconds - WipeIn) StartWipe(Stage.Hero);
                    break;
                case Stage.Hero:
                    PlaceShot(hero);
                    if (!greeted && stageTime > .6f)
                    {
                        greeted = true; greetTime = 0;
                        sound?.PlayAt("toot", head ? head.position + forward * .8f : start, 1.1f);
                    }
                    if (stageTime >= hero.seconds - WipeIn) StartWipe(Stage.Pop);
                    break;
                case Stage.Pop:
                    // 泡泡碎片等人坐到座位上（下一帧）再放：在正前方散开，不往脸上飞
                    if (!popBurst && stageTime > .02f)
                    {
                        popBurst = true;
                        var at = cam.transform.position + Flat(cam.transform.forward, forward) * 1.6f;
                        Burst(at, 160, 2.6f, .06f);
                        sound?.PlayAt("pop", at, 1f);
                    }
                    if (stageTime >= 1.1f) Finish();
                    break;
            }
            // 转场到点：切镜头（这时转场正好全盖住）
            if (wipeAt > 0 && Time.unscaledTime >= wipeAt && stage != wipeTo) Go(wipeTo);
            if (stage != Stage.Done) Animate(dt);
        }

        // 镜头里慢慢直线推近（缓入缓出），朝向不变
        void PlaceShot(Shot s)
        {
            float k = DivaUi.Smooth(Mathf.Clamp01(stageTime / s.seconds));
            PlaceEye(Vector3.Lerp(s.from, s.to, k), shotRot);
        }

        void StartWipe(Stage next)
        {
            if (wipeAt > 0) return;
            wipeTo = next;
            wipeAt = Time.unscaledTime + WipeIn;
            sound?.Play("whoosh", .35f);
        }

        /// <summary>开场时游戏拿不到人的操作（大象在起点等着）。</summary>
        public DivaVRFrame Drive(DivaVRFrame raw) => default;

        void Align()
        {
            PlaceEye(wide.from, shotRot);
            aligned = true;
            if (stage == Stage.Dream && !menuPlaced)
            {
                menuPlaced = true; menuShownAt = Time.unscaledTime;
                var h = cam.transform;
                var flat = Flat(h.forward, shotRot * Vector3.forward);
                prompt.position = h.position + flat * 1.4f - Vector3.up * .12f;
                sound?.Play("ready", .5f);
            }
        }

        void PopBubble()
        {
            sound?.Play("combo", .5f);
            bubble.gameObject.SetActive(false);
            bubbleFloor.gameObject.SetActive(false);
            // 粉色盖住的这一下换位置：坐到座位上，朝向直接对准大象的正前方（盖着看不见，不用慢慢转），驾驶舱出来
            wipeAt = -1;
            rig.Board(snap: true);
            ShowCockpit(true);
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
            foreach (var c in hiddenClouds) if (c) c.SetActive(true);
            hiddenClouds.Clear();
            if (rig && rig.Detached) rig.Board();
            if (wipe) Destroy(wipe.gameObject);
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
                foreach (var b in new Behaviour[] { GetComponent<DivaVRCockpit>(), GetComponent<DivaVRAmbience>(), GetComponent<DivaVRGuide>(), GetComponent<DivaVRCoach>() })
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

            // 梦里星光偶尔"叮"一下
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
                    var flat = Flat(h.forward, shotRot * Vector3.forward);
                    var want = h.position + flat * .75f - Vector3.up * .08f;
                    if (Vector3.Angle(flat, Flat(spark.position - h.position, flat)) > 30 || Vector3.Distance(spark.position, want) > .6f)
                        spark.position = Vector3.Lerp(spark.position, want, 1 - Mathf.Exp(-dt * 2.5f));
                    sparkHalo.localScale = Vector3.one * .3f * (1 + .18f * Mathf.Sin(t * 4.2f));
                }
                else if (stage == Stage.Burst)
                {
                    float k = DivaUi.Smooth(stageTime / .7f);
                    spark.position = Vector3.Lerp(spark.position, portalPos, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .3f;
                    sparkHalo.localScale = Vector3.one * Mathf.Lerp(.3f, .8f, k);
                }
                Billboard(spark);
            }

            // 门：弹开，门里是全景；然后梦境碎成一片片散开，门收起
            if (stage == Stage.Open || stage == Stage.Wide && dissolve < 1.2f)
            {
                openness = stage == Stage.Open ? Mathf.Max(.02f, DivaUi.EaseOutBack(stageTime / .9f)) : Mathf.Max(0, openness - dt * 3);
                float r = 1.35f * openness * (1 + .012f * Mathf.Sin(t * 1.6f));
                portal.localScale = Vector3.one * Mathf.Max(.001f, r);
                ring.localRotation = Quaternion.Euler(0, 0, t * 14);
                ringInner.localRotation = Quaternion.Euler(0, 0, -t * 22);
                Tint(ringMat, Color.white * 1.3f * (1 + flash * 1.5f));
                Tint(innerMat, new Color(1, .95f, 1) * 2.2f * (1 + flash));
                if (stage == Stage.Wide)
                {
                    if (dissolve == 0)
                    {
                        sound?.PlayAt("whoosh", h.position + Vector3.up, .6f);
                        Burst(portalPos, 80, 2.4f, .06f);
                        // 化开时梦境球改成半透明、画在镇子后面（透明队列），镇子才能从下面透出来
                        if (dreamMat)
                        {
                            dreamMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                            dreamMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                            dreamMat.SetFloat("_ZWrite", 0);
                            dreamMat.renderQueue = 3000;
                        }
                    }
                    dissolve = Mathf.Min(1.2f, dissolve + dt * .75f);
                    if (dreamMat) { dreamMat.SetFloat(PortalRadius, 0); dreamMat.SetFloat(Dissolve, dissolve); }
                    if (dreamBubbles) { var em = dreamBubbles.emission; em.rateOverTime = 0; }
                    if (sparkles) { var em = sparkles.emission; em.rateOverTime = 0; }
                    if (dissolve >= 1.2f) { portal.gameObject.SetActive(false); if (dream) dream.gameObject.SetActive(false); }
                }
                else if (dreamMat) dreamMat.SetFloat(PortalRadius, r * .985f);
            }

            // 泡泡：门开时长出来包住人，之后一直在身边（参照物），破的时候没了
            if (bubble.gameObject.activeSelf)
            {
                float size = stage == Stage.Dream || stage == Stage.Burst ? 0 : stage == Stage.Open ? DivaUi.EaseOutBack(stageTime / .8f) : stage == Stage.Wide ? DivaUi.EaseOutBack(stageTime / .8f) : 1;
                float breathe = 1 + .012f * Mathf.Sin(t * 1.1f);
                bubble.position = transform.position + Vector3.up * (rig.StandingEyeHeight * .55f);
                bubble.localScale = Vector3.one * Mathf.Max(.001f, bubbleRadius * 2 * size * breathe);
                if (bubbleMat) { bubbleMat.SetFloat(BubbleAlpha, .45f + flash * .15f); bubbleMat.SetFloat(BubbleShift, t * .02f); }
                bubbleFloor.position = transform.position + Vector3.up * .01f;
                Tint(floorMat, new Color(1f, .7f, .9f, .22f * size));
            }
            if (hum) hum.volume = Mathf.MoveTowards(hum.volume, (stage <= Stage.Open ? .4f : .1f) * (sound != null ? sound.volume : 1), dt * .3f);

            // 全景：镇子上空三发烟花
            if (stage == Stage.Wide && fireworkNext < 3 && stageTime > 1.6f + fireworkNext * 1.3f)
            {
                var c = new[] { new Color(1f, .45f, .8f), new Color(1f, .85f, .3f), new Color(.5f, .95f, .85f) }[fireworkNext];
                var at = centre + Vector3.up * (22 + fireworkNext * 3) + Quaternion.AngleAxis(fireworkNext * 110 - 110, Vector3.up) * forward * 18;
                fx.Firework(at, c * 1.6f);
                sound?.PlayAt("pop", at, .6f, .7f);
                fireworkNext++;
            }
            // 全景：标题弹出来，任务点一个个亮起来（赛道预告）；之后收掉
            if (stage == Stage.Wide)
                title.localScale = Vector3.one * (44f / 2000f) * Mathf.Max(0, DivaUi.EaseOutBack((stageTime - 1.2f) / .7f)) * Mathf.Clamp01((wide.seconds - stageTime) / .4f);
            else title.localScale = Vector3.zero;
            for (int i = 0; i < beacons.Count; i++)
            {
                var b = beacons[i];
                bool reveal = stage == Stage.Wide ? stageTime > 2.2f + i * .9f : stage == Stage.Dream && menuPlaced && Time.unscaledTime - menuShownAt > 1.2f + i * .9f;
                if ((stage == Stage.Wide || stage == Stage.Dream) && b.shown == 0 && reveal) { b.shown = t; sound?.Play("confirm", .35f); }
                float k = b.shown > 0 ? DivaUi.EaseOutBack((t - b.shown) / .6f) : 0;
                if (stage != Stage.Wide && stage != Stage.Dream) k = 0;
                Tint(b.mat, new Color(1f, .6f, .9f, .6f * k * (.8f + .2f * Mathf.Sin(t * 3 + i))));
                b.label.localScale = Vector3.one * (12f / 1200f) * Mathf.Max(0, k);
                var to = b.label.position - h.position; to.y = 0;
                if (to.sqrMagnitude > 1e-4f) b.label.rotation = Quaternion.LookRotation(to);
            }

            // 转场：0.4 秒淡到暗紫，切镜头，0.55 秒淡回来（电影里的"暗场"，比一下变亮的粉色舒服得多）
            float cover = 0;
            if (wipeAt > 0)
            {
                float d = Time.unscaledTime - wipeAt;
                cover = d < 0 ? DivaUi.Smooth(1 + d / WipeIn) : 1 - DivaUi.Smooth(d / WipeOut);
                if (d > WipeOut) wipeAt = -1;
            }
            if (stage == Stage.Pop) cover = Mathf.Max(cover, 1 - DivaUi.Smooth((stageTime - .1f) / .7f));
            Tint(wipeMat, new Color(Dusk.r, Dusk.g, Dusk.b, cover));
            if (wipe) wipe.gameObject.SetActive(cover > .001f);
            UpdatePrompt(dt);
        }

        /// <summary>捏的时候眼睛看的方向打到菜单的哪个按钮（打不到就 null）。</summary>
        RectTransform MenuHit(DivaVRFrame raw)
        {
            // 眼睛看的方向有就先用它，打不到再用头的朝向
            var byLook = raw.look.sqrMagnitude > .01f ? MenuHitDir(raw.look) : null;
            return byLook ?? (cam ? MenuHitDir(cam.transform.forward) : null);
        }

        RectTransform MenuHitDir(Vector3 dir)
        {
            if (!prompt || promptGroup.alpha < .5f || !cam) return null;
            var origin = cam.transform.position;
            var plane = new Plane(prompt.forward, prompt.position);
            if (!plane.Raycast(new Ray(origin, dir.normalized), out float d)) return null;
            var p = new Ray(origin, dir.normalized).GetPoint(d);
            foreach (var b in new[] { btnStart, btnLang })
            {
                var local = (Vector2)b.InverseTransformPoint(p); var r = b.rect;
                if (Mathf.Abs(local.x - r.center.x) < r.width * .5f + 70 && Mathf.Abs(local.y - r.center.y) < r.height * .5f + 70) return b;
            }
            return null;
        }

        void UpdatePrompt(float dt)
        {
            string key = stage == Stage.Dream && aligned && stageTime > 1f ? "vr.tour.begin" : null;
            if (key != null && (key != promptKey || menuLangText != DivaText.T("lang")))
            {
                promptKey = key; if (menuLangText == null) promptShownAt = Time.unscaledTime;
                promptTitle.text = DivaText.T("game");
                promptSub.text = DivaText.T("vr.menu.sub");
                menuStart.text = DivaText.T("vr.menu.start");
                menuLangText = DivaText.T("lang"); menuLang.text = menuLangText;
                menuHint.text = DivaText.T("vr.menu.hint") + "\n<size=70%>" + DivaText.T("vr.menu.exit") + "</size>";
            }
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, key != null ? 1 : 0, dt * (key != null ? 3 : 4));
            // 头朝向的按钮高亮
            if (plateStart) { bool hs = menuHovered == btnStart; plateStart.Set(Color.Lerp(new Color(1f, .38f, .74f), Color.white, hs ? .3f : 0), Color.Lerp(new Color(.82f, .22f, .6f), Color.white, hs ? .2f : 0)); btnStart.localScale = Vector3.one * (hs ? 1.08f : 1); }
            if (plateLang) { bool hl = menuHovered == btnLang; plateLang.Set(Color.Lerp(new Color(.26f, .22f, .4f), Color.white, hl ? .3f : 0), Color.Lerp(new Color(.17f, .14f, .28f), Color.white, hl ? .2f : 0)); btnLang.localScale = Vector3.one * (hl ? 1.08f : 1); }
            float pop = key != null ? DivaUi.EaseOutBack((Time.unscaledTime - promptShownAt) / .5f) : 1;
            prompt.localScale = Vector3.one * (.9f / 1100f) * Mathf.Max(.01f, pop);
            if (promptGroup.alpha <= 0 && key == null) return;
            var h = cam.transform;
            var flat = Flat(h.forward, shotRot * Vector3.forward);
            var want = h.position + flat * 1.3f - Vector3.up * .3f;
            var to = Flat(prompt.position - h.position, flat);
            if (Vector3.Angle(flat, to) > 22 || Mathf.Abs((prompt.position - h.position).magnitude - 1.33f) > .5f || promptGroup.alpha < .05f)
                prompt.position = Vector3.Lerp(prompt.position, want, promptGroup.alpha < .05f ? 1 : 1 - Mathf.Exp(-dt * 3));
            prompt.rotation = Quaternion.LookRotation(prompt.position - h.position);
            if (btnStart) btnStart.localScale = Vector3.one * (menuHovered == btnStart ? 1.06f : 1f);
            // 气球：吊在牌子顶边上，各自慢慢摇；头正对着哪个哪个鼓起来
            if (signRoot)
            {
                signRoot.gameObject.SetActive(promptGroup.alpha > .05f);
                signRoot.position = prompt.position; signRoot.rotation = prompt.rotation; signRoot.localScale = Vector3.one * Mathf.Max(.01f, pop);
                float tt = Time.unscaledTime; balloonHover = -1;
                for (int i = 0; i < balloons.Count; i++)
                {
                    var b = balloons[i]; var st = strings[i];
                    if (!b.gameObject.activeSelf) { st.gameObject.SetActive(false); continue; }
                    var lp = new Vector3((i - 2) * .17f + Mathf.Sin(tt * .9f + i) * .025f, .66f + Mathf.Sin(tt * 1.3f + i * 1.7f) * .03f, 0);
                    b.localPosition = lp;
                    var top = new Vector3((i - 2) * .1f, .33f, 0);
                    st.localPosition = (lp + top) * .5f; st.localRotation = Quaternion.FromToRotation(Vector3.up, lp - top); st.localScale = new Vector3(.004f, Vector3.Distance(lp, top) * .5f, .004f);
                    bool hov = Vector3.Angle(h.forward, b.position - h.position) < 3.5f;
                    if (hov) balloonHover = i;
                    b.localScale = Vector3.one * (hov ? .19f : .16f);
                }
            }
        }

        void BuildBalloons()
        {
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow");
            if (!glow || !prompt) return;
            signRoot = new GameObject("Sign Balloons").transform; signRoot.SetParent(prompt.parent, false);
            Color[] cs = { new Color(1f, .55f, .8f), new Color(.6f, 1f, .85f), new Color(1f, .9f, .45f), new Color(.75f, .6f, 1f), new Color(.55f, .85f, 1f) };
            var stringMat = new Material(glow); stringMat.color = Color.white * 1.2f; stringMat.SetColor("_BaseColor", Color.white * 1.2f);
            for (int i = 0; i < 5; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere); Destroy(b.GetComponent<Collider>());
                b.name = "Balloon " + i; b.transform.SetParent(signRoot, false); b.transform.localScale = Vector3.one * .16f;
                var m = new Material(glow); m.color = cs[i] * 1.3f; m.SetColor("_BaseColor", cs[i] * 1.3f);
                var mr = b.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                balloons.Add(b.transform);
                var st = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(st.GetComponent<Collider>());
                st.name = "String " + i; st.transform.SetParent(signRoot, false);
                var sr = st.GetComponent<MeshRenderer>(); sr.sharedMaterial = stringMat; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                strings.Add(st.transform);
            }
            signRoot.gameObject.SetActive(false);
        }

        void PopBalloon(int i)
        {
            if (i < 0 || i >= balloons.Count || !balloons[i].gameObject.activeSelf) return;
            var b = balloons[i];
            fx?.Pop(b.position, new Color(1f, .6f, .9f), .25f);
            sound?.PlayAt("pop", b.position, .7f, 1.25f);
            b.gameObject.SetActive(false); strings[i].gameObject.SetActive(false);
            Debug.Log("DIVA_VR menu balloon popped " + i);
        }

        void LateUpdate()
        {
            if (stage == Stage.Done || !root || !head) return;
            float dt = Time.unscaledDeltaTime;
            // 中景、特写时大象抬头看人；坐上去以后不看了
            float lookTarget = stage == Stage.Mid ? .6f : stage == Stage.Hero ? 1 : 0;
            lookWeight = Mathf.MoveTowards(lookWeight, lookTarget, dt * 1.2f);
            PoseGreet(dt);
            PoseLook(dt);
        }

        // 扬一下鼻子打招呼：头抬 18°、鼻子后四节往上卷，1.7 秒内做完
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
            var want = LimitedTurn(now, target - head.position, 35, 30);
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
