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
        public float maxYawSpeed = 50;
        [Tooltip("驾驶舱跟上大象朝向的平滑时间（秒）。")]
        public float yawSmoothTime = .35f;
        [Tooltip("仪表盘面板：在眼睛前方多远、低多少、多宽（米）。")]
        public float hudDistance = 1.8f, hudDrop = .24f, hudWidth = 2.0f;
        [Tooltip("VR 里大象最快跑多快（米/秒）、最快转多快（度/秒）、加减速多柔和（越小越柔和）。电脑版是 4.5 / 60 / 6。")]
        public float runSpeed = 3.6f, turnSpeed = 45, inputSmoothing = 3;

        DigiPhantLocomotion loco;
        Transform root, offset;
        Camera cam;
        float yaw, yawVelocity;
        bool placed, recentered;
        DivaVRHands hands;
        TextMesh stats;
        float statsTime; int statsFrames;
        float nextPoseLog;

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
                loco.runSpeed = Mathf.Min(loco.runSpeed, runSpeed);
                loco.turnDegreesPerSecond = Mathf.Min(loco.turnDegreesPerSecond, turnSpeed);
                loco.inputSmoothing = Mathf.Min(loco.inputSmoothing, inputSmoothing);
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
                if (cam) cam.gameObject.AddComponent<DivaVRHeadPose>().head = cam.transform;
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
            if (DivaVRAutopilot.On) gameObject.AddComponent<DivaVRAutopilot>();
            if (DivaVRCapture.Requested) gameObject.AddComponent<DivaVRCapture>();
            gameObject.AddComponent<DivaVRInput>();
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
                urp.shadowDistance = Mathf.Min(urp.shadowDistance, 35);
                var renderer = urp.GetRenderer(0);
                var prop = typeof(UnityEngine.Rendering.Universal.ScriptableRenderer).GetProperty("rendererFeatures",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                if (renderer != null && prop?.GetValue(renderer) is System.Collections.Generic.List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> features)
                    foreach (var f in features)
                        if (f && f.GetType().Name.Contains("AmbientOcclusion")) f.SetActive(false);
            }
        }

        void Start()
        {
            if (!loco || !loco.travelRoot) { Debug.LogWarning("DIVA_VR: no elephant (DigiPhantLocomotion.travelRoot); the rig stays put."); return; }
            root = loco.travelRoot;
            if (seat == Vector3.zero) seat = MeasureSeat();
            yaw = root.eulerAngles.y;
            Debug.Log($"DIVA_VR rig on: seat {seat} eye {eyeHeight} simulated {DivaVR.Simulated}");
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
            // 水平朝向：慢慢跟上大象，最快 maxYawSpeed
            float target = root.eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            // 平滑地开始转、平滑地停（角速度不突变），最快 maxYawSpeed
            yaw = placed ? Mathf.SmoothDampAngle(yaw, target, ref yawVelocity, yawSmoothTime, maxYawSpeed, dt) : target;
            placed = true;
            var rot = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(root.TransformPoint(seat), rot);
            if (!recentered) Recenter();
            UpdateStats();
            if (DivaVRCapture.Requested && Time.unscaledTime > nextPoseLog && cam)
            {
                nextPoseLog = Time.unscaledTime + 5;
                Debug.Log($"DIVA_VR pose head.local {cam.transform.localPosition:F3} rot {cam.transform.localEulerAngles:F0} offset {offset.localPosition:F3} eye-above-seat {transform.InverseTransformPoint(cam.transform.position):F3} recentered {recentered}");
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

        /// <summary>把头对到眼睛的位置：头显里人的头在哪儿都行（站着、坐着），都放到座位上方 eyeHeight 处。</summary>
        public void Recenter()
        {
            if (!cam) return;
            var head = cam.transform.localPosition;
            if (!DivaVR.Simulated && head == Vector3.zero) return;   // 头显还没给出第一帧位置
            // 人一开始面朝哪儿都行：把那个方向转成大象的正前方
            var turn = Quaternion.Euler(0, DivaVR.Simulated ? 0 : -cam.transform.localEulerAngles.y, 0);
            var p = turn * head;
            offset.localRotation = turn;
            offset.localPosition = new Vector3(-p.x, eyeHeight - head.y, -p.z);
            if (hands) hands.SetNeutral(head);
            recentered = true;
        }
    }
}
