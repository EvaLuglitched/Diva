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
    /// 传送门开场（每次打开 app 演一次，Vision Pro 和模拟器都有）：
    /// 1. 人站在自己的房间里（Vision Pro 透视），面前飘着一颗光点："捏一下，打开传送门"（门开在你面对的方向）。
    /// 2. 捏 → 光点飞出去，在面前 2.4 米处炸开一道彩虹传送门，门里是糖果镇。
    /// 3. 机甲大象从镇子那头走过来，走到门口停下，象牙和鼻子伸进你的房间；它的头和眼睛一直看着你。
    /// 4. "伸手摸摸它的鼻子"：鼻子朝你卷过来，摸到它（或者等一会儿）它就扬起鼻子长鸣，彩带撒进房间。
    /// 5. "双手举高 登上机甲"（和比赛里放大招、确认是同一个手势）：光柱把你接上去（白光过渡，视角不移动，不晕），
    ///    白光散开时你已经坐在驾驶舱里。门那边还是你的房间，门收拢关上，进入选涂装。
    /// 做法：房间是一个包住人的"挡板盒子"（Diva/VR Portal Occluder），画成透明但写深度，只有从门洞里能看到镇子。
    /// 房间地面比镇子地面高 roomLift 米：大象的脸正好在门里，眼睛比人高一点（仰头看它，显得大）。
    /// 大象最后停在它原来的起点，进入选涂装时不会跳。门、房间都放在大象起点前方，开场时人站的地方附近的镇子物件先藏起来。
    /// </summary>
    [DefaultExecutionOrder(1600)]   // 在 Diva Show（1500）之后：走路时这里把大象的输入放开；在驾驶舱（2500）之前
    public class DivaVRPortal : MonoBehaviour
    {
        [Tooltip("门洞半径（米）。")]
        public float radius = 1.35f;
        [Tooltip("门的中心离房间地面多高（米）。")]
        public float centerHeight = 1.45f;
        [Tooltip("人站的地方离门多远（米）。")]
        public float distance = 2.4f;
        [Tooltip("房间地面比糖果镇地面高多少（米）。")]
        public float roomLift = .7f;
        [Tooltip("门所在的平面在大象起点前方多远（米）：象牙和鼻子会从门里伸进房间。")]
        public float planeAhead = 2.35f;
        [Tooltip("大象从门后多远开始走过来（米）。")]
        public float walkFrom = 9.5f;

        enum Stage { Waiting, Flying, Opening, Arriving, Offer, Greet, Board, Boarding, Inside, Closing, Done }
        Stage stage = Stage.Waiting;
        float stageTime;

        /// <summary>开场还在接管操作吗（DivaVRInput 问）。演完后还要等人把举着的手放下，免得一进选涂装就被当成"确认"。</summary>
        public bool Driving => stage != Stage.Done || holdUlt;
        bool holdUlt;
        float walkGo;

        static readonly int RoomPreview = Shader.PropertyToID("_DivaRoomPreview");
        static readonly int PortalCenter = Shader.PropertyToID("_PortalCenter"), PortalNormal = Shader.PropertyToID("_PortalNormal"), PortalRadius = Shader.PropertyToID("_PortalRadius");
        static readonly Color[] Candy = { new Color(1f, .45f, .8f), new Color(.45f, .95f, .9f), new Color(1f, .85f, .4f), new Color(.7f, .55f, 1f), new Color(.55f, 1f, .6f) };

        DivaVRRig rig;
        DivaVRInput input;
        DivaVRSimulator simulator;
        DivaShowDirector show;
        DivaShowSound sound;
        DivaShowFx fx;
        DigiPhantLocomotion loco;
        DivaDemo demo;
        Camera cam;
        bool autopilot, aligned, lastPinch, switched, hdrWas, hdrChanged, handTouch;

        Transform fxRoot, world, room, portal, ring, ringInner, membrane, pool, spark, sparkHalo, column, overlay;
        Material occluder, ringMat, innerMat, membraneMat, poolMat, sparkMat, haloMat, columnMat, overlayMat;
        ParticleSystem rimSparks, rising;
        TrailRenderer sparkTrail;
        AudioSource hum;
        Vector3 start, forward, center, sparkFrom, touchPoint, liftFrom;
        Light portalLight;
        Quaternion startRotation;
        float openness, glowBoost, flash;

        // 大象：头和眼睛看人、鼻子伸过来、扬鼻长鸣
        Transform root, head;
        Transform[] eyes = new Transform[0], trunk = new Transform[0], bones = new Transform[0];
        Vector3 headRest;
        Vector3[] eyeRest = new Vector3[0];
        Quaternion headTurn = Quaternion.identity;
        Quaternion[] eyeTurn = new Quaternion[0];
        Vector3[] savedPos = new Vector3[0];
        Quaternion[] savedRot = new Quaternion[0];
        float lookWeight, reachWeight, trumpetTime = -1;
        AnimationClip trumpetClip;
        Animator animator;

        readonly List<Renderer> hidden = new List<Renderer>();
        readonly List<GameObject> cockpitParts = new List<GameObject>();
        readonly List<Behaviour> paused = new List<Behaviour>();
        readonly List<Vector3> tips = new List<Vector3>();

        // 提示面板
        Transform prompt;
        CanvasGroup promptGroup;
        TextMeshProUGUI promptTitle, promptSub;
        Image[] handDots = new Image[0];
        RectTransform holdFill;
        GameObject handRow;
        string promptKey;

        void Start()
        {
            rig = GetComponent<DivaVRRig>();
            input = GetComponent<DivaVRInput>();
            simulator = GetComponent<DivaVRSimulator>();
            show = FindAnyObjectByType<DivaShowDirector>();
            demo = FindAnyObjectByType<DivaDemo>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            cam = rig ? rig.Cam : null;
            autopilot = DivaVRAutopilot.On;
            if (!rig || !cam || !show || !show.enabled || !demo || !loco || !loco.travelRoot)
            {
                Debug.LogWarning("DIVA_VR arrival: missing rig / show / elephant; skipping the portal opening.");
                Finish();
                return;
            }
            sound = show.Sound;
            trumpetClip = show.trumpetClip;
            root = loco.travelRoot;
            animator = loco.elephantAnimator;
            start = root.position;
            startRotation = root.rotation;
            forward = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;

            world = new GameObject("Diva VR Arrival").transform;
            // 特效单独放：门关上时最后那一下烟花要放完，不跟着开场的东西一起删掉
            fxRoot = new GameObject("Diva VR Arrival FX").transform;
            fx = new DivaShowFx(fxRoot, show.glowMaterial, show.bubbleMaterial);
            room = new GameObject("Room").transform;
            room.SetParent(world, false);
            room.SetPositionAndRotation(start + forward * (planeAhead + distance) + Vector3.up * roomLift, Quaternion.LookRotation(-forward));
            center = room.TransformPoint(new Vector3(0, centerHeight, distance));

            BuildRoom();
            BuildPortal();
            BuildSpark();
            BuildPrompt();
            FindElephantParts();

            // 门后大象要走的路上、人站的房间里的镇子物件先藏起来（演完放回去）
            HideInside(root, new Bounds(new Vector3(0, 3, (3.6f - walkFrom - 2) / 2), new Vector3(3.6f, 5.2f, walkFrom + 5.6f)));
            HideInside(room, new Bounds(new Vector3(0, 3.5f, (distance - 6) / 2), new Vector3(12.4f, 7.2f, distance + 6.4f)));
            // 走动的范围（stageRadius）按起点算：先让它记下起点，再把大象挪到门后
            loco.Initialise();
            root.SetPositionAndRotation(start - forward * walkFrom, startRotation);

            // 开场时驾驶舱、仪表、全息模型都收起来：人站在自己的房间里
            ShowCockpit(false);
            Shader.SetGlobalVector(RoomPreview, DivaVR.Simulated ? new Vector4(.15f, .16f, .2f, 0) : Vector4.zero);
            // 透视要透明通道：头显上开场时这台摄像机先不用 HDR（Unity 文档：Metal 透视要关 HDR），上了大象再打开
            if (!DivaVR.Simulated && !Application.isEditor) { hdrWas = cam.allowHDR; cam.allowHDR = false; hdrChanged = true; }
            // 第一帧就站进房间（头显还没给位置时先按 1.6 米高摆，给了以后再对准一次）
            rig.Stand(room.position, room.rotation);
            if (rig.PoseReady) Align();
            Debug.Log($"DIVA_VR arrival start: room {room.position} portal {center} elephant from {root.position} hidden {hidden.Count}");
            Capture("portal_waiting", 3);
        }

        void OnDestroy()
        {
            if (stage != Stage.Done) Restore();
        }

        // ------------------------------------------------------------------ build
        void BuildRoom()
        {
            var shared = Resources.Load<Material>("DivaVR Portal Occluder");
            if (!shared) { Debug.LogWarning("DIVA_VR arrival: Resources/DivaVR Portal Occluder missing; no passthrough room."); return; }
            occluder = new Material(shared);
            occluder.SetVector(PortalCenter, center);
            occluder.SetVector(PortalNormal, forward);
            occluder.SetFloat(PortalRadius, 0);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(box.GetComponent<Collider>());
            box.name = "Room Occluder";
            box.transform.SetParent(room, false);
            // 正面就是门所在的平面；地面抬高 5 毫米，盖住下面的镇子地面
            const float halfWidth = 6, back = 6, height = 7, floorGap = .005f;
            box.transform.localPosition = new Vector3(0, floorGap + height / 2, (distance - back) / 2);
            box.transform.localScale = new Vector3(halfWidth * 2, height, distance + back);
            var mr = box.GetComponent<MeshRenderer>();
            mr.sharedMaterial = occluder;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
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
            m.name = "Diva Portal Glow";
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

        static Texture2D softDot, ringBand, membraneTex, poolTex, columnTex;

        static void MakeTextures()
        {
            if (softDot) return;
            softDot = DivaUi.MakeTexture("Diva Portal Dot", 64, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                float a = Mathf.Exp(-r * r * 9) + .35f * Mathf.Exp(-r * r * 2.2f);
                return new Color(1, 1, 1, Mathf.Clamp01(a * (1 - Mathf.Clamp01((r - .9f) * 10))));
            });
            ringBand = Texture2D.whiteTexture;
            // 门里那层"能量膜"：边上一圈亮，往里淡，加五条慢慢转的旋臂
            membraneTex = DivaUi.MakeTexture("Diva Portal Membrane", 256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                if (r > 1) return Color.clear;
                float rim = Mathf.Exp(-(1 - r) / .045f) * .8f + Mathf.Exp(-(1 - r) / .14f) * .18f;
                float arms = .5f + .5f * Mathf.Sin(Mathf.Atan2(y, x) * 5 + r * 13);
                float swirl = arms * arms * Mathf.SmoothStep(.6f, .97f, r) * .12f;
                var c = Color.HSVToRGB(Mathf.Repeat(Mathf.Atan2(y, x) / (2 * Mathf.PI) + .1f, 1), .55f, 1);
                return new Color(c.r, c.g, c.b, Mathf.Clamp01(rim + swirl));
            });
            poolTex = DivaUi.MakeTexture("Diva Portal Pool", 128, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y);
                return new Color(1, 1, 1, Mathf.Clamp01(Mathf.Exp(-r * r * 3.2f) - .04f));
            });
            // 光柱：上下淡出，竖着几条亮线
            columnTex = DivaUi.MakeTexture("Diva Board Column", 128, (x, y) =>
            {
                float v = (y + 1) / 2, u = (x + 1) / 2;
                float fade = Mathf.SmoothStep(0, .15f, v) * (1 - Mathf.SmoothStep(.55f, 1, v));
                float lines = .55f + .45f * Mathf.Pow(.5f + .5f * Mathf.Sin(u * Mathf.PI * 2 * 9), 6);
                return new Color(1, 1, 1, fade * lines);
            });
        }

        void BuildPortal()
        {
            MakeTextures();
            portal = new GameObject("Portal").transform;
            portal.SetParent(world, false);
            portal.SetPositionAndRotation(center, Quaternion.LookRotation(forward));   // 门面朝房间，圆环在本地 XY 平面上

            // 彩虹圆环（粗管）+ 里面一圈白亮细环，反向转
            ring = MeshObject("Ring", portal, Torus(1, .034f, 160, 12, u => Color.HSVToRGB(Mathf.Repeat(u * 2, 1), .85f, 1)), ringMat = Glow(ringBand, new Color(1, 1, 1, 1) * 1.3f));
            ringInner = MeshObject("Ring Inner", portal, Torus(1.03f, .009f, 160, 6, u => new Color(1, 1, 1, .4f + .6f * Mathf.Pow(Mathf.Abs(Mathf.Sin(u * Mathf.PI * 12)), 3))), innerMat = Glow(ringBand, new Color(1, .95f, 1, 1) * 3f));
            membrane = Quad("Membrane", portal, membraneMat = Glow(membraneTex, new Color(1, 1, 1, .9f)));
            membrane.localPosition = new Vector3(0, 0, .01f);

            // 门前地板上的一摊光：那边的光洒进你的房间
            pool = Quad("Light Pool", room, poolMat = Glow(poolTex, new Color(1f, .55f, .85f, 0)));
            pool.localPosition = new Vector3(0, .012f, distance - .95f);
            pool.localRotation = Quaternion.Euler(90, 0, 0);
            pool.localScale = new Vector3(3.4f, 2.4f, 1);

            // 门里透出来的光：照亮走到门口的大象的脸和鼻子（镇子的太阳在它背后）
            portalLight = new GameObject("Portal Light").AddComponent<Light>();
            portalLight.transform.SetParent(world, false);
            portalLight.transform.position = center + forward * 1.5f + Vector3.up * .3f;
            portalLight.type = LightType.Point; portalLight.range = 8; portalLight.intensity = 0;
            portalLight.color = new Color(1f, .78f, .92f); portalLight.shadows = LightShadows.None;
            portalLight.enabled = false;

            // 圆环上一直往房间里飘的小光点
            rimSparks = Particles("Rim Sparks", portal, 900);
            var main = rimSparks.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.05f, .35f);
            main.startSize = new ParticleSystem.MinMaxCurve(.015f, .05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .5f, .85f), new Color(.5f, 1f, .95f));
            main.gravityModifier = -.02f;
            var shape = rimSparks.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = 1; shape.radiusThickness = 0;
            shape.rotation = Vector3.zero;
            var vel = rimSparks.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(.15f, .55f); vel.x = new ParticleSystem.MinMaxCurve(0, 0); vel.y = new ParticleSystem.MinMaxCurve(0, 0);
            Twinkle(rimSparks);
            var em = rimSparks.emission; em.rateOverTime = 0;

            portal.localScale = Vector3.one * .001f;
            portal.gameObject.SetActive(false);
            pool.gameObject.SetActive(false);

            if (sound != null && sound.Clip("portal"))
            {
                hum = new GameObject("Portal Hum").AddComponent<AudioSource>();
                hum.transform.SetParent(portal, false);
                hum.clip = sound.Clip("portal"); hum.loop = true; hum.playOnAwake = false;
                hum.spatialBlend = 1; hum.minDistance = 2.5f; hum.maxDistance = 30; hum.dopplerLevel = 0; hum.volume = 0;
            }
        }

        void BuildSpark()
        {
            spark = new GameObject("Summon Spark").transform;
            spark.SetParent(world, false);
            sparkHalo = Quad("Halo", spark, haloMat = Glow(softDot, new Color(1f, .5f, .85f, .8f) * 1.6f));
            sparkHalo.localScale = Vector3.one * .3f;
            var core = Quad("Core", spark, sparkMat = Glow(softDot, new Color(1, 1, 1, 1) * 3));
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
            var go = new GameObject("Arrival Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            prompt = go.transform;
            prompt.SetParent(world, false);
            var rt = (RectTransform)prompt;
            rt.sizeDelta = new Vector2(1100, 320);
            rt.localScale = Vector3.one * (.78f / 1100f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = cam; canvas.sortingOrder = 60;
            promptGroup = go.GetComponent<CanvasGroup>();
            promptGroup.alpha = 0; promptGroup.blocksRaycasts = false; promptGroup.interactable = false;
            var ink = new Color(.06f, .05f, .12f);
            DivaUi.Panel(rt, "Plate", new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(1020, 200), DivaUi.WithAlpha(ink, .86f), DivaUi.WithAlpha(ink, .6f), 22).stripeWidth = 8;
            promptTitle = DivaUi.Text(rt, "Title", new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(1000, 90), 64, Color.white);
            DivaUi.Glow(promptTitle, new Color(1, .3f, .7f, .9f));
            promptSub = DivaUi.Text(rt, "Sub", new Vector2(.5f, .5f), new Vector2(0, -36), new Vector2(1000, 60), 36, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);

            // "双手举高"：两只手各一个点（举起来就亮），下面一条按住多久的进度条
            handRow = new GameObject("Hands", typeof(RectTransform));
            var row = (RectTransform)handRow.transform;
            row.SetParent(rt, false);
            row.anchoredPosition = new Vector2(0, -128); row.sizeDelta = new Vector2(400, 60);
            var dot = Sprite.Create(softDot, new Rect(0, 0, softDot.width, softDot.height), new Vector2(.5f, .5f));
            handDots = new[]
            {
                DivaUi.Image(row, "Left", new Vector2(.5f, .5f), new Vector2(-70, 8), new Vector2(54, 54), new Color(1, 1, 1, .3f), dot),
                DivaUi.Image(row, "Right", new Vector2(.5f, .5f), new Vector2(70, 8), new Vector2(54, 54), new Color(1, 1, 1, .3f), dot)
            };
            var bar = DivaUi.Image(row, "Hold", new Vector2(.5f, .5f), new Vector2(0, -30), new Vector2(320, 10), new Color(1, 1, 1, .18f));
            holdFill = DivaUi.Image(bar.transform, "Fill", new Vector2(0, .5f), Vector2.zero, new Vector2(320, 10), new Color(1f, .45f, .8f)).rectTransform;
            holdFill.pivot = new Vector2(0, .5f);
            holdFill.anchoredPosition = Vector2.zero;
            handRow.SetActive(false);
        }

        void FindElephantParts()
        {
            var skin = animator ? animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).FirstOrDefault() : null;
            if (!skin) return;
            bones = skin.bones.Where(b => b).Distinct().ToArray();
            savedPos = new Vector3[bones.Length]; savedRot = new Quaternion[bones.Length];
            head = bones.FirstOrDefault(b => b.name.ToLower().Contains("head"));
            eyes = bones.Where(b => b.name.ToLower().EndsWith("_eye_bone") || b.name.ToLower() == "eye_l" || b.name.ToLower() == "eye_r").ToArray();
            trunk = demo.trunkBones != null ? demo.trunkBones.Where(b => b).ToArray() : new Transform[0];
            // 静止时头和眼睛的"正前方"（骨骼自己的坐标里）：之后每帧算现在朝哪儿，再转向人
            if (head) headRest = Quaternion.Inverse(head.rotation) * root.forward;
            eyeRest = eyes.Select(e => Quaternion.Inverse(e.rotation) * root.forward).ToArray();
            eyeTurn = eyes.Select(_ => Quaternion.identity).ToArray();
        }

        // ------------------------------------------------------------------ flow
        void Go(Stage next)
        {
            stage = next;
            stageTime = 0;
            Debug.Log("DIVA_VR arrival " + next);
            switch (next)
            {
                case Stage.Flying:
                    sparkFrom = spark.position;
                    sparkTrail.Clear(); sparkTrail.emitting = true;
                    sound?.PlayAt("whoosh", center, .9f);
                    break;
                case Stage.Opening:
                    spark.gameObject.SetActive(false);
                    portal.gameObject.SetActive(true);
                    pool.gameObject.SetActive(true);
                    portalLight.enabled = true;
                    fx.Flash(center, new Color(1f, .7f, .95f), 1.6f, .45f);
                    Burst(center, 140, 2.2f, .05f);
                    rimSparks.Emit(160);
                    var em = rimSparks.emission; em.rateOverTime = 70;
                    sound?.PlayAt("pop", center, 1.2f);
                    sound?.Play("ready", .8f);
                    if (hum) hum.Play();
                    flash = 1;
                    Capture("portal_open", 1.6f);
                    break;
                case Stage.Arriving:
                    loco.inputSmoothing = 9;   // 停得准：起点就是选涂装时大象的位置
                    break;
                case Stage.Offer:
                    Capture("portal_arrived", 1.2f);
                    break;
                case Stage.Board:
                    sound?.Play("skill", .6f);
                    Capture("portal_board", 1);
                    break;
                case Stage.Boarding:
                    BeginBoarding();
                    break;
                case Stage.Inside:
                    // 门升到驾驶舱正前方：从镇子这边回头看，门里是你自己的房间
                    liftFrom = center;
                    Capture("portal_cockpit", 1.4f);
                    break;
                case Stage.Closing:
                    sound?.PlayAt("whoosh", center, .7f);
                    break;
            }
        }

        void Update()
        {
            if (stage == Stage.Done)
            {
                if (holdUlt && !(input && input.Current.ult)) holdUlt = false;
                return;
            }
            float dt = Time.unscaledDeltaTime;
            stageTime += dt;
            var raw = input ? input.Current : default;
            bool pinch = raw.shoot && !lastPinch;
            lastPinch = raw.shoot;
            if (!aligned && rig.PoseReady) Align();
            walkGo = 0;
            switch (stage)
            {
                case Stage.Waiting:
                    // 捏一下开门（门开在你面对的方向）。没有手部追踪时（演示保险）25 秒后自己开
                    if (aligned && stageTime > .8f && (pinch || autopilot && stageTime > 2.5f || stageTime > 25))
                    {
                        // 按人现在站的位置和面对的方向重新摆房间；光点和提示在人眼里不动
                        var sparkLocal = cam.transform.InverseTransformPoint(spark.position);
                        var promptLocal = cam.transform.InverseTransformPoint(prompt.position);
                        Align();
                        spark.position = cam.transform.TransformPoint(sparkLocal);
                        prompt.position = cam.transform.TransformPoint(promptLocal);
                        Go(Stage.Flying);
                    }
                    break;
                case Stage.Flying:
                    if (stageTime >= .6f) Go(Stage.Opening);
                    break;
                case Stage.Opening:
                    if (stageTime >= 1.1f) Go(Stage.Arriving);
                    break;
                case Stage.Arriving:
                    Walk();
                    break;
                case Stage.Offer:
                    if (stageTime > .8f && (handTouch || (simulator || !rig.Hands) && pinch || autopilot && stageTime > 2.2f || stageTime > 9)) Greet();
                    break;
                case Stage.Greet:
                    if (stageTime >= 1.9f) Go(Stage.Board);
                    break;
                case Stage.Board:
                    if (raw.ult || autopilot && stageTime > 2.5f || stageTime > 40) Go(Stage.Boarding);
                    break;
                case Stage.Boarding:
                    if (!switched && stageTime >= .65f) SwitchToCockpit();
                    if (stageTime >= 1.6f) Go(Stage.Inside);
                    break;
                case Stage.Inside:
                    LiftPortal();
                    if (stageTime >= 2.4f) Go(Stage.Closing);
                    break;
                case Stage.Closing:
                    if (stageTime >= .85f) { ClosingBurst(); Finish(); return; }
                    break;
            }
            Animate(dt);
        }

        /// <summary>开场时游戏拿到的操作：只有走路（大象自己走到门口），人的手势不让大象喷水、放大招。</summary>
        public DivaVRFrame Drive(DivaVRFrame raw)
        {
            if (stage == Stage.Done) { if (!raw.ult) holdUlt = false; raw.ult = false; return raw; }
            return new DivaVRFrame { go = walkGo };
        }

        void Align()
        {
            rig.Stand(room.position, room.rotation);
            aligned = true;
            if (stage == Stage.Waiting && !spark.gameObject.activeSelf)
            {
                spark.gameObject.SetActive(true);
                var h = cam.transform;
                var flat = Flat(h.forward, room.forward);
                spark.position = h.position + flat * .72f - Vector3.up * .1f;
                prompt.position = h.position + flat * 1.15f - Vector3.up * .42f;
                sound?.PlayAt("skill", spark.position, .5f);
            }
        }

        // 大象沿着原来的朝向走到起点：离得远时稳稳地走，最后两米慢下来，差几厘米时对齐
        void Walk()
        {
            if (stageTime < .9f) return;
            demo.inputLocked = false;
            float remaining = Vector3.Dot(start - root.position, forward);
            if (remaining <= .04f)
            {
                loco.StopMotion();
                root.SetPositionAndRotation(new Vector3(start.x, root.position.y, start.z), startRotation);
                demo.inputLocked = true;
                Go(Stage.Offer);
                return;
            }
            walkGo = Mathf.Clamp(remaining * .32f, .09f, .6f);
        }

        // 手指尖碰到鼻子了吗：在 LateUpdate 里、鼻子已经卷过来以后量（人看到的就是这个位置）
        void CheckTouch()
        {
            if (handTouch || stage != Stage.Offer || !rig.Hands || trunk.Length < 2) return;
            rig.Hands.Fingertips(cam.transform.parent, tips);
            foreach (var tip in tips)
                for (int i = 0; i + 1 < trunk.Length; i++)
                    if (DistanceToSegment(tip, trunk[i].position, trunk[i + 1].position) < .2f) { touchPoint = tip; handTouch = true; return; }
        }

        void Greet()
        {
            if (touchPoint == Vector3.zero && trunk.Length > 0) touchPoint = trunk[trunk.Length - 1].position;
            Go(Stage.Greet);
            trumpetTime = 0;
            Burst(touchPoint, 70, 1.3f, .035f);
            fx.Flash(touchPoint, new Color(1f, .85f, .95f), .5f, .3f);
            sound?.Play("confirm", .8f);
            var tip = trunk.Length > 0 ? trunk[trunk.Length - 1].position : center;
            Invoke(nameof(Trumpet), .2f);
            fx.Confetti(center + forward * .6f + Vector3.up * .3f, Candy, 180);
            glowBoost = 1;
            Capture("portal_greet", .9f);
            Debug.Log($"DIVA_VR arrival touch at {touchPoint} trunk tip {tip}");
        }

        void Trumpet()
        {
            if (sound == null) return;
            var at = head ? head.position + forward * 1.2f : center;
            sound.PlayAt("trumpet", at, 1.3f);
        }

        void BeginBoarding()
        {
            sound?.Play("rise", .9f);
            sound?.Play("confirm", .7f);
            // 光柱和往上飘的光点：在人站的地方（视角不动，只是"被接上去"的感觉）
            var feet = cam.transform.position; feet.y = room.position.y;
            column = new GameObject("Board Column").transform;
            column.SetParent(world, false);
            column.position = feet;
            var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(tube.GetComponent<Collider>());
            tube.transform.SetParent(column, false);
            tube.transform.localPosition = new Vector3(0, 2.2f, 0);
            tube.transform.localScale = new Vector3(1.3f, 2.2f, 1.3f);
            var tr = tube.GetComponent<MeshRenderer>();
            tr.sharedMaterial = columnMat = Glow(columnTex, new Color(1f, .75f, .95f, 0));
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rising = Particles("Rising", column, 1200);
            var main = rising.main;
            main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(.8f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0, 0);
            main.startSize = new ParticleSystem.MinMaxCurve(.02f, .06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .6f, .9f), new Color(.6f, 1f, .95f));
            var shape = rising.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .7f;
            shape.rotation = new Vector3(-90, 0, 0);
            var vel = rising.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(1.8f, 4.2f); vel.x = new ParticleSystem.MinMaxCurve(0, 0); vel.z = new ParticleSystem.MinMaxCurve(0, 0);
            Twinkle(rising);
            var em = rising.emission; em.rateOverTime = 260;
            rising.Play();
            // 贴在眼前的白光
            overlay = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(overlay.GetComponent<Collider>());
            overlay.name = "Board Fade";
            overlay.SetParent(cam.transform, false);
            overlay.localPosition = new Vector3(0, 0, .11f);
            overlay.localScale = Vector3.one * .7f;
            var shared = Resources.Load<Material>("DivaVR Vignette");
            overlayMat = shared ? new Material(shared) : null;
            if (overlayMat)
            {
                overlayMat.mainTexture = Texture2D.whiteTexture;
                if (overlayMat.HasProperty("_BaseMap")) overlayMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (overlayMat.HasProperty("_Cull")) overlayMat.SetFloat("_Cull", 0);
                Tint(overlayMat, new Color(1, .96f, 1, 0));
            }
            var or = overlay.GetComponent<MeshRenderer>();
            or.sharedMaterial = overlayMat; or.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!overlayMat) overlay.gameObject.SetActive(false);
        }

        // 白光最亮的那一刻：人回到大象背上的座位，驾驶舱和仪表出来
        // （HDR 等门关上再打开：回头看的那扇门里还要透出房间，要透明通道）
        void SwitchToCockpit()
        {
            switched = true;
            rig.Board();
            ShowCockpit(true);
            if (column) Destroy(column.gameObject, .05f);
            lookWeight = 0; headTurn = Quaternion.identity;
            for (int i = 0; i < eyeTurn.Length; i++) eyeTurn[i] = Quaternion.identity;
            Debug.Log("DIVA_VR arrival boarded");
        }

        void LiftPortal()
        {
            var eye = cam.transform.position;
            var target = new Vector3(eye.x, eye.y - .1f, eye.z) + forward * 4.6f;
            float k = DivaUi.Smooth(stageTime / 1.1f);
            center = Vector3.Lerp(liftFrom, target, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .4f;
            portal.position = center;
            portalLight.transform.position = center + forward * 1.5f + Vector3.up * .3f;
            if (occluder) occluder.SetVector(PortalCenter, center);
            if (hum) hum.transform.position = center;
        }

        void ClosingBurst()
        {
            fx.Flash(center, new Color(1f, .8f, .95f), 1.2f, .35f);
            fx.Firework(center + Vector3.up * .4f, new Color(1f, .5f, .85f));
            sound?.PlayAt("pop", center, 1f);
            sound?.Play("combo", .6f);
        }

        void Finish()
        {
            if (stage == Stage.Done) return;
            stage = Stage.Done;
            holdUlt = true;
            Restore();
            DivaVR.ArrivalDone = true;
            Debug.Log("DIVA_VR arrival done");
        }

        // 演完（或者中途出错）：镇子物件放回去，驾驶舱回来，开场的东西都拿掉
        void Restore()
        {
            foreach (var r in hidden) if (r) r.enabled = true;
            hidden.Clear();
            ShowCockpit(true);
            if (rig && rig.Detached) rig.Board();
            if (hdrChanged && cam) { cam.allowHDR = hdrWas; hdrChanged = false; }
            if (loco) loco.inputSmoothing = rig ? rig.inputSmoothing : loco.inputSmoothing;
            if (overlay) Destroy(overlay.gameObject);
            if (world) Destroy(world.gameObject);
            if (fxRoot) Destroy(fxRoot.gameObject, 6);
            Shader.SetGlobalVector(RoomPreview, Vector4.zero);
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
            glowBoost = Mathf.MoveTowards(glowBoost, 0, dt * .8f);

            // 光点：在眼前轻轻上下浮动；人转头太多就慢慢跟回眼前
            if (spark && spark.gameObject.activeSelf)
            {
                var h = cam.transform;
                if (stage == Stage.Waiting)
                {
                    var flat = Flat(h.forward, room.forward);
                    var want = h.position + flat * .72f - Vector3.up * .1f;
                    if (Vector3.Angle(flat, Flat(spark.position - h.position, flat)) > 30 || Vector3.Distance(spark.position, want) > .6f)
                        spark.position = Vector3.Lerp(spark.position, want, 1 - Mathf.Exp(-dt * 2.5f));
                    spark.position += Vector3.up * Mathf.Cos(t * 2.1f) * .012f * dt * 2.1f;
                    float pulse = 1 + .18f * Mathf.Sin(t * 4.2f);
                    sparkHalo.localScale = Vector3.one * .3f * pulse;
                }
                else if (stage == Stage.Flying)
                {
                    float k = DivaUi.Smooth(stageTime / .6f);
                    spark.position = Vector3.Lerp(sparkFrom, center, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * .35f;
                    sparkHalo.localScale = Vector3.one * Mathf.Lerp(.3f, .7f, k);
                }
                Billboard(spark);
            }

            // 门：张开（弹一下）、平时呼吸、关门时先亮一下再收拢
            if (portal && portal.gameObject.activeSelf)
            {
                switch (stage)
                {
                    case Stage.Opening: openness = Mathf.Max(.02f, DivaUi.EaseOutBack(stageTime / .9f)); break;
                    case Stage.Closing:
                        float c = stageTime;
                        openness = c < .25f ? 1 + .1f * DivaUi.Smooth(c / .25f) : 1.1f * (1 - Mathf.Pow(Mathf.Clamp01((c - .25f) / .55f), 2.2f));
                        break;
                    default: openness = Mathf.MoveTowards(openness, 1, dt * 2); break;
                }
                float breathe = 1 + .012f * Mathf.Sin(t * 1.6f);
                portal.localScale = Vector3.one * radius * Mathf.Max(.001f, openness) * breathe;
                ring.localRotation = Quaternion.Euler(0, 0, t * 14);
                ringInner.localRotation = Quaternion.Euler(0, 0, -t * 22);
                membrane.localRotation = Quaternion.Euler(0, 0, t * 9);
                float bright = 1 + flash * 1.5f + glowBoost * .8f + (stage == Stage.Closing ? 1.5f : 0);
                Tint(ringMat, Color.white * 1.3f * bright);
                Tint(innerMat, new Color(1, .95f, 1) * 2.2f * bright);
                Tint(membraneMat, new Color(1, 1, 1, .75f + .15f * Mathf.Sin(t * 2.3f)) * (1 + flash));
                float onFloor = switched ? 0 : 1;
                Tint(poolMat, new Color(1f, .55f, .85f, Mathf.Clamp01(openness) * onFloor * (.32f + .08f * Mathf.Sin(t * 1.6f) + flash * .4f)));
                portalLight.intensity = Mathf.Clamp01(openness) * (1.5f + flash * 2.5f + glowBoost * 1.5f) * (switched ? .4f : 1);
                if (occluder) occluder.SetFloat(PortalRadius, radius * Mathf.Clamp01(openness) * .985f * breathe);
                if (hum) hum.volume = Mathf.MoveTowards(hum.volume, stage == Stage.Closing ? 0 : .55f * (sound != null ? sound.volume : 1), dt * (stage == Stage.Closing ? 1.5f : .6f));
                if (stage == Stage.Closing) { var em = rimSparks.emission; em.rateOverTime = 0; }
            }

            // 上机甲：光柱亮起来，白光盖住眼睛，换到座位，再散开
            if (stage == Stage.Boarding)
            {
                float k = stageTime;
                Tint(columnMat, new Color(1f, .75f, .95f, Mathf.Clamp01(k / .4f) * .9f) * 1.6f);
                float fade = k < .65f ? Mathf.Pow(Mathf.Clamp01(k / .65f), 1.6f) : k < .8f ? 1 : 1 - DivaUi.Smooth((k - .8f) / .8f);
                Tint(overlayMat, new Color(1, .96f, 1, fade));
            }
            else if (overlay && stage != Stage.Done) { Destroy(overlay.gameObject); overlay = null; }

            UpdatePrompt(dt);
        }

        void UpdatePrompt(float dt)
        {
            string key = stage switch
            {
                Stage.Waiting => aligned && stageTime > .8f ? "vr.portal.open" : null,
                Stage.Offer => stageTime > .8f ? "vr.portal.touch" : null,
                Stage.Board => "vr.portal.board",
                _ => null
            };
            if (key != null && key != promptKey)
            {
                promptKey = key;
                promptTitle.text = DivaText.T(key);
                promptSub.text = DivaText.T(key + ".b");
                handRow.SetActive(key == "vr.portal.board");
            }
            promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, key != null ? 1 : 0, dt * (key != null ? 2.5f : 4));
            if (promptGroup.alpha <= 0 && key == null) return;

            // 跟着视线慢慢走（偏出 22° 才挪），在眼前 1.15 米、低一点的地方，正对着人
            var h = cam.transform;
            var flat = Flat(h.forward, room.forward);
            var want = h.position + flat * 1.15f - Vector3.up * .42f;
            var to = Flat(prompt.position - h.position, flat);
            if (Vector3.Angle(flat, to) > 22 || Mathf.Abs((prompt.position - h.position).magnitude - 1.2f) > .45f || promptGroup.alpha < .05f)
                prompt.position = Vector3.Lerp(prompt.position, want, promptGroup.alpha < .05f ? 1 : 1 - Mathf.Exp(-dt * 3));
            prompt.rotation = Quaternion.LookRotation(prompt.position - h.position);

            if (handRow.activeSelf)
            {
                int up = rig.Hands ? rig.Hands.HandsUp : simulator ? simulator.HandsUp : 0;
                float hold = rig.Hands ? rig.Hands.UltProgress : up == 2 ? 1 : 0;
                for (int i = 0; i < handDots.Length; i++)
                {
                    bool lit = up > i;
                    handDots[i].color = Color.Lerp(handDots[i].color, lit ? new Color(1f, .5f, .85f, 1) : new Color(1, 1, 1, .3f), 1 - Mathf.Exp(-dt * 12));
                    handDots[i].rectTransform.localScale = Vector3.one * (lit ? 1.15f + .08f * Mathf.Sin(Time.unscaledTime * 9) : 1);
                }
                holdFill.localScale = new Vector3(Mathf.Clamp01(hold), 1, 1);
            }
        }

        void LateUpdate()
        {
            if (stage == Stage.Done || !root) return;
            float dt = Time.unscaledDeltaTime;
            // 走过来的路上和到了以后，头和眼睛看着人；坐上去以后不看了
            float lookTarget = stage == Stage.Arriving ? Mathf.InverseLerp(9, 5, Vector3.Distance(root.position, start) + 2) :
                               stage == Stage.Offer ? 1 : stage == Stage.Greet || stage == Stage.Board ? (trumpetTime >= 0 ? .25f : 1) :
                               stage == Stage.Boarding && !switched ? 1 : 0;
            lookWeight = Mathf.MoveTowards(lookWeight, lookTarget, dt * .8f);
            reachWeight = Mathf.MoveTowards(reachWeight, stage == Stage.Offer && stageTime > .5f ? 1 : 0, dt * (stage == Stage.Offer ? .9f : 2.2f));
            PoseTrumpet(dt);
            PoseReach();
            PoseLook(dt);
            KeepClear(dt);
            CheckTouch();
        }

        // 扬鼻长鸣：在步态动画上叠 trumpet 片段，开头和结尾和原来的姿势混一下
        void PoseTrumpet(float dt)
        {
            if (trumpetTime < 0 || !trumpetClip || !animator || bones.Length == 0) return;
            trumpetTime += dt;
            float length = trumpetClip.length;
            if (trumpetTime >= length) { trumpetTime = -1; return; }
            float w = Mathf.Min(DivaUi.Smooth(trumpetTime / .3f), DivaUi.Smooth((length - trumpetTime) / .45f));
            for (int i = 0; i < bones.Length; i++) { savedPos[i] = bones[i].localPosition; savedRot[i] = bones[i].localRotation; }
            var a = animator.transform;
            Vector3 lp = a.localPosition; Quaternion lr = a.localRotation;
            Vector3 mp = Vector3.zero; Quaternion mr = Quaternion.identity;
            var motion = loco.animationMotionRoot;
            if (motion) { mp = motion.localPosition; mr = motion.localRotation; }
            trumpetClip.SampleAnimation(animator.gameObject, Mathf.Min(trumpetTime * .9f, length - .02f));
            a.localPosition = lp; a.localRotation = lr;
            if (motion) motion.SetLocalPositionAndRotation(mp, mr);
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i].localPosition = Vector3.Lerp(savedPos[i], bones[i].localPosition, w);
                bones[i].localRotation = Quaternion.Slerp(savedRot[i], bones[i].localRotation, w);
            }
        }

        // 鼻子朝人卷过来（每节往前上方弯一点，轻轻摆），像是伸过来让你摸
        void PoseReach()
        {
            if (reachWeight <= 0 || trunk.Length == 0) return;
            float t = Time.unscaledTime;
            var right = root.right;
            for (int i = 0; i < trunk.Length; i++)
            {
                float angle = -19f * reachWeight * (1 + .1f * Mathf.Sin(t * 1.7f + i * .6f));
                trunk[i].rotation = Quaternion.AngleAxis(angle, right) * trunk[i].rotation;
            }
        }

        void PoseLook(float dt)
        {
            if (!head || lookWeight <= 0 && headTurn == Quaternion.identity) return;
            var target = cam.transform.position;
            float k = 1 - Mathf.Exp(-dt * 3);
            var now = head.rotation * headRest;
            var wantDir = target - head.position;
            var want = LimitedTurn(now, wantDir, 30, 20);
            headTurn = Quaternion.Slerp(headTurn, Quaternion.Slerp(Quaternion.identity, want, lookWeight), k);
            head.rotation = headTurn * head.rotation;
            for (int i = 0; i < eyes.Length; i++)
            {
                if (!eyes[i]) continue;
                var eyeNow = eyes[i].rotation * eyeRest[i];
                var eyeWant = LimitedTurn(eyeNow, target - eyes[i].position, 22, 16);
                eyeTurn[i] = Quaternion.Slerp(eyeTurn[i], Quaternion.Slerp(Quaternion.identity, eyeWant, lookWeight), 1 - Mathf.Exp(-dt * 8));
                eyes[i].rotation = eyeTurn[i] * eyes[i].rotation;
            }
        }

        // 个人空间：鼻子（连同鼻尖上的水炮）离人的头不到 0.85 米时，大象把头抬起来让开（人走近时也一样）
        float clearLift;
        void KeepClear(float dt)
        {
            if (!head || trunk.Length < 2 || switched) { clearLift = Mathf.MoveTowards(clearLift, 0, dt * 30); return; }
            var eye = cam.transform.position;
            float Near()
            {
                var tip = trunk[trunk.Length - 1].position;
                var dir = (tip - trunk[trunk.Length - 2].position).normalized;
                float d = DistanceToSegment(eye, tip, tip + dir * .4f);
                for (int i = 0; i + 1 < trunk.Length; i++) d = Mathf.Min(d, DistanceToSegment(eye, trunk[i].position, trunk[i + 1].position));
                return d;
            }
            const float clear = .85f;
            float want = 0;
            var axis = -root.right;   // 绕这根轴转是抬头
            var rest = head.rotation;
            for (int i = 0; i < 6 && Near() < clear; i++)
            {
                want += 6;
                head.rotation = Quaternion.AngleAxis(6, axis) * head.rotation;
            }
            head.rotation = rest;
            clearLift = Mathf.MoveTowards(clearLift, Mathf.Max(want, clearLift * .98f), dt * (want > clearLift ? 90 : 25));
            head.rotation = Quaternion.AngleAxis(clearLift, axis) * head.rotation;
        }

        /// <summary>从 now 转到 want 的旋转，左右最多 yawMax 度、上下最多 pitchMax 度。</summary>
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
        /// <summary>
        /// 把和一个区域相交的镇子物件藏起来（frame 本地坐标里的盒子）。大象、驾驶舱、开场自己的东西不算；
        /// 很大的东西（地面）也不算——地面在房间地板下面，本来就被挡住。相交的物件连同它旁边的兄弟（同一棵树的树冠、树干）一起藏。
        /// </summary>
        void HideInside(Transform frame, Bounds local)
        {
            var corners = new Vector3[8];
            foreach (var r in FindObjectsByType<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || hidden.Contains(r)) continue;
                var t = r.transform;
                if (t.IsChildOf(root) || t.IsChildOf(rig.transform) || (world && t.IsChildOf(world)) || t.IsChildOf(show.transform)) continue;
                if (r is ParticleSystemRenderer || r.bounds.size.magnitude > 60) continue;
                var b = r.bounds;
                int n = 0;
                for (int x = 0; x < 2; x++) for (int y = 0; y < 2; y++) for (int z = 0; z < 2; z++)
                    corners[n++] = frame.InverseTransformPoint(new Vector3(x == 0 ? b.min.x : b.max.x, y == 0 ? b.min.y : b.max.y, z == 0 ? b.min.z : b.max.z));
                var lb = new Bounds(corners[0], Vector3.zero);
                for (int i = 1; i < 8; i++) lb.Encapsulate(corners[i]);
                if (!lb.Intersects(local)) continue;
                var group = t.parent && t.parent.parent ? t.parent : t;
                foreach (var g in group.GetComponentsInChildren<Renderer>())
                    if (g.enabled && !hidden.Contains(g) && !(g is ParticleSystemRenderer)) { g.enabled = false; hidden.Add(g); }
            }
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0;
            return Vector3.Distance(p, a + ab * t);
        }

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
            go.transform.localScale = Vector3.one * 2;   // 本地 1 = 门的半径
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        ParticleSystem Particles(string name, Transform parent, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Glow(softDot, Color.white * 2);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        /// <summary>一团往四面炸开的彩色光点（开门、摸到鼻子）。</summary>
        void Burst(Vector3 at, int count, float speed, float size)
        {
            if (!fxRoot) return;
            var ps = Particles("Burst", fxRoot, count);
            ps.transform.position = at;
            var main = ps.main;
            main.loop = false; main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .3f, speed);
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

        /// <summary>圆环网格（在 XY 平面上，半径 major，管粗 minor），顶点色按一圈的位置 u（0–1）给。</summary>
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
            var mesh = new Mesh { name = "Diva Portal Torus" };
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
