using System.Linq;
using DigiPhant;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 驾驶舱：玩家坐在机甲大象背上。场景不用改，VR 模式打开时运行时自动装上。
    /// 舒适规则：驾驶舱只跟大象的位置和朝向（只转水平方向，有最大转速），地平线永远水平；
    /// 大象走路的颠簸在骨骼上，不在 travel root 上，所以不会传到头上。
    /// 层级：Diva VR Rig（跟着座位走）/ Camera Offset（把头对到眼睛的位置）/ 主摄像机（头显或模拟器驱动）。
    /// </summary>
    [DefaultExecutionOrder(2500)]   // 在 Diva Show（1500）和 Eva 的开场（2000）之后：头的位置最后定
    public class DivaVRRig : MonoBehaviour
    {
        [Tooltip("座位相对大象 travel root 的位置（米）。0 = 开始时按大象身体自动量：背上靠前一点。")]
        public Vector3 seat;
        [Tooltip("坐着时眼睛比座位高多少（米）。")]
        public float eyeHeight = .78f;
        [Tooltip("驾驶舱最快转多快（度/秒）。大象转得更快时视角会慢一点跟上，避免晕。")]
        public float maxYawSpeed = 36;
        [Tooltip("驾驶舱跟上大象朝向的平滑时间（秒）。")]
        public float yawSmoothTime = .45f;
        [Tooltip("仪表盘面板：在眼睛前方多远、低多少、多宽（米）。")]
        public float hudDistance = 1.8f, hudDrop = .24f, hudWidth = 2.0f;
        [Tooltip("VR 里大象最快跑多快（米/秒）、最快转多快（度/秒）、加减速多柔和（越小越柔和）。电脑版是 4.5 / 60 / 6。" +
                 "按舒适来定（参考 Meta：跑步约 2.8 米/秒），所有人都用这一套。")]
        public float runSpeed = 2.8f, turnSpeed = 30, inputSmoothing = 2.5f;

        DigiPhantLocomotion loco;
        Transform root, offset;
        Camera cam;
        float yaw, yawVelocity;
        bool placed, recentered;
        DivaVRHands hands;
        TextMesh stats;
        float statsTime; int statsFrames;
        float nextPoseLog, startTime;
        DivaVRHeadPose headPose;

        /// <summary>传送门开场时人站在自己的房间里，驾驶舱不跟着座位走（由 DivaVRPortal 摆放）。</summary>
        public bool Detached { get; private set; }
        public Camera Cam => cam;
        public DivaVRHands Hands => hands;
        /// <summary>头显已经给出有效的头部位置了吗（模拟器里一直是）。</summary>
        public bool PoseReady => DivaVR.Simulated || !headPose || headPose.Valid || Time.unscaledTime - startTime > 2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!DivaVR.Active || FindAnyObjectByType<DivaVRRig>()) return;
            new GameObject("Diva VR Rig").AddComponent<DivaVRRig>();
        }

        void Awake()
        {
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            var demo = FindAnyObjectByType<DivaDemo>();
            var controller = demo ? demo.GetComponent<DigiPhantController>() : FindAnyObjectByType<DigiPhantController>();
            // 手势不再来自电脑摄像头（MediaPipe），而是 Vision Pro 的手部追踪 / 模拟器：不开摄像头、不启动识别程序
            if (controller) controller.SetInputMode(InputMode.TestSliders);
            foreach (var preview in FindObjectsByType<DigiPhantCameraPreview>(FindObjectsInactive.Include))
            { preview.StopPreview(); preview.enabled = false; }
            if (demo) { demo.externalInput = true; demo.cameraHeld = true; demo.State.IndependentHands = true; }
            // 舒适：跑慢一点、转慢一点、加减速更柔和
            if (loco)
            {
                loco.runSpeed = runSpeed;
                loco.turnDegreesPerSecond = turnSpeed;
                loco.inputSmoothing = inputSmoothing;
            }
            // Eva 的天空镜头每帧把镜头往上抬、改视角：VR 里镜头只能跟着头
            foreach (var sky in FindObjectsByType<DivaSkyCamera>(FindObjectsInactive.Include)) sky.enabled = false;

            cam = demo && demo.thirdPersonCamera ? demo.thirdPersonCamera : Camera.main;
            offset = new GameObject("Camera Offset").transform;
            offset.SetParent(transform, false);
            if (cam)
            {
                cam.transform.SetParent(offset, false);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
                cam.nearClipPlane = .05f;
            }
            var hud = new GameObject("Dashboard").transform;
            hud.SetParent(transform, false);
            hud.localPosition = new Vector3(0, eyeHeight - hudDrop, hudDistance);
            // 面板微微后仰，正对眼睛
            hud.localRotation = Quaternion.Euler(Mathf.Atan2(hudDrop, hudDistance) * Mathf.Rad2Deg, 0, 0);

            DivaVR.Rig = transform;
            DivaVR.Head = cam ? cam.transform : null;
            DivaVR.HudAnchor = hud;
            DivaVR.HudWidth = hudWidth;
            if (DivaVR.Simulated) gameObject.AddComponent<DivaVRSimulator>();
            else
            {
                // Apple visionOS 插件要求场景里有一个 ARSession（它的检查规则里是错误级别）：ARKit 的世界追踪由它启动，
                // 合成器每一帧都要拿到"设备锚点"才会把画面显示出来；手部追踪也靠这个会话。场景里没有就在这里建一个
                if (!FindAnyObjectByType<UnityEngine.XR.ARFoundation.ARSession>(FindObjectsInactive.Include))
                {
                    var ar = new GameObject("AR Session").AddComponent<UnityEngine.XR.ARFoundation.ARSession>();
                    ar.attemptUpdate = false;
                    Debug.Log("DIVA_VR AR session created");
                }
                if (cam) { headPose = cam.gameObject.AddComponent<DivaVRHeadPose>(); headPose.head = cam.transform; }
                hands = gameObject.AddComponent<DivaVRHands>();
            }
            BuildStats(hud);
            gameObject.AddComponent<DivaVRCockpit>();
            gameObject.AddComponent<DivaVRHologram>();
            var show = FindAnyObjectByType<Diva.Show.DivaShowDirector>();
            if (show && show.glowMaterial) gameObject.AddComponent<DivaVRAmbience>().glow = show.glowMaterial;
            // 大时刻（倒数、技能横幅）的空中面板：前方 3.2 米、比眼睛高一点
            var moments = new GameObject("Moments").transform;
            moments.SetParent(transform, false);
            moments.localPosition = new Vector3(0, eyeHeight + .25f, 3.2f);
            DivaVR.MomentsAnchor = moments;
            // 仪表台小屏：眼睛前下方 0.6 米，斜着正对眼睛
            var dash = new GameObject("Dash Display").transform;
            dash.SetParent(transform, false);
            dash.localPosition = new Vector3(.05f, eyeHeight - .3f, .6f);
            dash.localRotation = Quaternion.LookRotation(new Vector3(0, -.3f, .6f), Vector3.up);
            DivaVR.DashAnchor = dash;
            if (!DivaVR.Simulated) HeadsetGraphics();
            DivaVRDebug.Apply(cam, transform, eyeHeight);
            if (DivaVRAutopilot.On) gameObject.AddComponent<DivaVRAutopilot>();
            if (DivaVRCapture.Requested) gameObject.AddComponent<DivaVRCapture>();
            // 每次打开 app 先演一次泡泡开场：在梦境里捏一下出发，泡泡载着人绕镇子飞一圈，落到大象背上
            if (!DivaVR.ArrivalDone && !DivaVRDebug.On("DIVA_NOPORTAL")) gameObject.AddComponent<DivaVRBubbleTour>();
            gameObject.AddComponent<DivaVRInput>();
            gameObject.AddComponent<DivaVRCoach>();
        }

        /// <summary>
        /// 头显上的画面设置：双眼高分辨率要稳 90 fps。MSAA 代替后期抗锯齿，关掉屏幕空间环境光遮蔽（双眼渲染很贵），
        /// 阴影只画近处。只在头显上改（编辑器里改的是工程里的资源文件，会被保存下来）。
        /// </summary>
        static void HeadsetGraphics()
        {
            if (Application.isEditor) return;
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                urp.msaaSampleCount = 4;
                // 渲染缩放用 1：Vision Pro 的画面由系统给出，URP 先画到缩小的图再放大拷过去时头显上可能没有画面
                // （visionOS 用的是 Mobile 那套设置，缩放 0.8）。调试：环境变量 DIVA_RS=0.8 试别的值
                urp.renderScale = DivaVRDebug.Float("DIVA_RS", 1);
                urp.shadowDistance = Mathf.Min(urp.shadowDistance, 35);
                // 阴影：2 级级联够用（双眼要画两遍），点光 / 聚光的阴影关掉（主光的保留）
                urp.shadowCascadeCount = Mathf.Min(urp.shadowCascadeCount, 2);
                // 传送门开场要露出真实房间（Mixed 沉浸）：后期处理要保留透明通道，否则整屏都是不透明的
                typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_AllowPostProcessAlphaOutput",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(urp, true);
                typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetProperty("supportsAdditionalLightShadows")?
                    .SetValue(urp, false);
                var renderer = urp.GetRenderer(0);
                var prop = typeof(UnityEngine.Rendering.Universal.ScriptableRenderer).GetProperty("rendererFeatures",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (renderer != null && prop?.GetValue(renderer) is System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> features)
                    foreach (var f in features)
                        if (f && f.GetType().Name.Contains("AmbientOcclusion")) f.SetActive(false);
                Debug.Log($"DIVA_VR graphics: rp {urp.name} scale {urp.renderScale} msaa {urp.msaaSampleCount} hdr {urp.supportsHDR} alphaOut {urp.allowPostProcessAlphaOutput} quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
            }
        }

        void Start()
        {
            startTime = Time.unscaledTime;
            if (!loco || !loco.travelRoot) { Debug.LogWarning("DIVA_VR: no elephant (DigiPhantLocomotion.travelRoot); the rig stays put."); return; }
            root = loco.travelRoot;
            if (seat == Vector3.zero) seat = MeasureSeat();
            yaw = root.eulerAngles.y;
            Debug.Log($"DIVA_VR rig on: seat {seat} eye {eyeHeight} simulated {DivaVR.Simulated}");
            // 机甲点亮时（人已经坐好准备开跑）再校正一次：前倾 / 侧倾按开跑时的坐姿算
            Diva.Show.DivaShowDirector.PhaseChanged += OnPhase;
        }

        void OnDestroy() => Diva.Show.DivaShowDirector.PhaseChanged -= OnPhase;

        void OnPhase(Diva.Show.ShowPhase phase)
        {
            if (phase == Diva.Show.ShowPhase.PowerUp && !DivaVR.Simulated) Recenter(true);
        }

        /// <summary>
        /// 按大象和机甲外壳的形状量座位：沿背的中线每 25 厘米取最高点，在背的中段找外壳最低的地方坐下
        /// （前面隆起的外壳和头戴就是"引擎盖"，身后是推进器）。没有机甲时就坐在身体上。
        /// </summary>
        Vector3 MeasureSeat()
        {
            var animator = loco.elephantAnimator;
            var skin = animator ? animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).FirstOrDefault() : null;
            if (!skin) return new Vector3(0, 3, 0);
            const float step = .25f, halfWidth = .35f;
            var top = new System.Collections.Generic.Dictionary<int, float>();
            void Add(Vector3 world)
            {
                var p = root.InverseTransformPoint(world);
                if (Mathf.Abs(p.x) > halfWidth) return;
                int k = Mathf.FloorToInt(p.z / step);
                top[k] = top.TryGetValue(k, out var y) ? Mathf.Max(y, p.y) : p.y;
            }
            var baked = new Mesh();
            skin.BakeMesh(baked, true);
            float minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (var v in baked.vertices)
            {
                var w = skin.transform.TransformPoint(v);
                Add(w);
                float z = root.InverseTransformPoint(w).z;
                minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
            }
            Destroy(baked);
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh && mf.sharedMesh.isReadable && mf.GetComponent<Renderer>() && mf.GetComponent<Renderer>().enabled)
                    foreach (var v in mf.sharedMesh.vertices) Add(mf.transform.TransformPoint(v));
            // 背的中段：身体长度（含鼻子）的 20%–45% 之间，也就是尾巴和肩膀之间
            float from = Mathf.Lerp(minZ, maxZ, .2f), to = Mathf.Lerp(minZ, maxZ, .45f);
            float bestZ = (from + to) / 2, bestY = float.MaxValue;
            foreach (var pair in top)
            {
                float z = (pair.Key + .5f) * step;
                if (z < from || z > to) continue;
                if (pair.Value < bestY) { bestY = pair.Value; bestZ = z; }
            }
            if (bestY == float.MaxValue) bestY = skin.bounds.max.y;
            return new Vector3(0, bestY + .05f, bestZ);
        }

        void LateUpdate()
        {
            if (!root) return;
            if (Detached) { UpdateStats(); return; }
            // 水平朝向：慢慢跟上大象，最快 maxYawSpeed
            float target = root.eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            // 平滑地开始转、平滑地停（角速度不突变），最快 maxYawSpeed
            yaw = placed ? Mathf.SmoothDampAngle(yaw, target, ref yawVelocity, yawSmoothTime, maxYawSpeed, dt) : target;
            placed = true;
            var rot = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(root.TransformPoint(seat), rot);
            if (!recentered) Recenter(boardKeepFacing);
            UpdateStats();
            if (DivaVRCapture.Requested && Time.unscaledTime > nextPoseLog && cam)
            {
                nextPoseLog = Time.unscaledTime + 5;
                Debug.Log($"DIVA_VR pose head.local {cam.transform.localPosition:F3} rot {cam.transform.localEulerAngles:F0} offset {offset.localPosition:F3} eye-above-seat {transform.InverseTransformPoint(cam.transform.position):F3} recentered {recentered} pose-valid {(headPose ? headPose.Valid : false)}");
            }
        }

        // 仪表盘左下角的小字：帧率和手部追踪状态（调性能用，之后可以关）
        void BuildStats(Transform hud)
        {
            var go = new GameObject("VR Stats", typeof(TextMesh));
            go.transform.SetParent(hud, false);
            go.transform.localPosition = new Vector3(-hudWidth * .5f, -.36f, -.01f);
            stats = go.GetComponent<TextMesh>();
            stats.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = stats.font.material;
            stats.fontSize = 48; stats.characterSize = .006f; stats.anchor = TextAnchor.UpperLeft; stats.color = new Color(1, 1, 1, .7f);
        }

        void UpdateStats()
        {
            if (!stats) return;
            statsFrames++; statsTime += Time.unscaledDeltaTime;
            if (statsTime < .5f) return;
            stats.text = $"{statsFrames / statsTime:0} fps" + (DivaVR.Simulated ? "  simulator" : hands && hands.Tracking ? "  hands ok" : "  no hands");
            statsFrames = 0; statsTime = 0;
        }

        /// <summary>
        /// 站在房间里（传送门开场）：驾驶舱放在 floor（房间地面上人站的点），人现在面朝的方向转成 facing。
        /// 头显的追踪原点在地面时头的高度就是真实身高；原点在头上时（读数接近 0）按 1.6 米算。
        /// </summary>
        public void Stand(Vector3 floor, Quaternion facing)
        {
            Detached = true;
            transform.SetPositionAndRotation(floor, facing);
            if (!cam) return;
            var head = cam.transform.localPosition;
            var turn = Quaternion.Euler(0, DivaVR.Simulated ? 0 : -cam.transform.localEulerAngles.y, 0);
            var p = turn * head;
            offset.localRotation = turn;
            offset.localPosition = new Vector3(-p.x, head.y < .8f ? 1.6f - head.y : 0, -p.z);
        }

        /// <summary>人站着时眼睛离房间地面多高（米）。</summary>
        public float StandingEyeHeight => cam ? cam.transform.localPosition.y + offset.localPosition.y : 1.6f;

        /// <summary>站着时（开场）把驾驶舱放到 pos、朝向 rot（每帧调用）。</summary>
        public void Place(Vector3 pos, Quaternion rot) { if (Detached) transform.SetPositionAndRotation(pos, rot); }

        /// <summary>上机甲：回到大象背上的座位，按现在的头重新对准（这一帧的 LateUpdate 里就到位）。</summary>
        /// <param name="keepFacing">只对齐位置，不把人现在面朝的方向转成大象的正前方（泡泡落下来时朝向已经对好了，转了反而跳）。</param>
        public void Board(bool keepFacing = false)
        {
            Detached = false;
            // 朝向从现在的接着平滑转过去（不跳）；位置这一帧就到座位
            yaw = transform.eulerAngles.y; yawVelocity = 0; placed = true;
            recentered = false;
            boardKeepFacing = keepFacing;
        }
        bool boardKeepFacing;

        /// <summary>把头对到眼睛的位置：头显里人的头在哪儿都行（站着、坐着），都放到座位上方 eyeHeight 处。</summary>
        /// <param name="keepFacing">只重新对齐位置，不改"正前方"（开跑前人可能正转头看别处）。</param>
        public void Recenter(bool keepFacing = false)
        {
            if (!cam) return;
            var head = cam.transform.localPosition;
            // 等头显给出第一帧有效的位置（模拟器里头可能就在原点，所以不能用"不为 0"判断）；2 秒还没有就照现在的算
            if (!DivaVR.Simulated && headPose && !headPose.Valid && Time.unscaledTime - startTime < 2) return;
            // 人一开始面朝哪儿都行：把那个方向转成大象的正前方
            var turn = keepFacing ? offset.localRotation : Quaternion.Euler(0, DivaVR.Simulated ? 0 : -cam.transform.localEulerAngles.y, 0);
            var p = turn * head;
            offset.localRotation = turn;
            offset.localPosition = new Vector3(-p.x, eyeHeight - head.y, -p.z);
            if (hands) hands.SetNeutral(head);
            recentered = true;
        }
    }
}
