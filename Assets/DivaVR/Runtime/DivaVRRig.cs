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
        [Tooltip("仪表盘面板：在眼睛前方多远、低多少、多宽（米）。")]
        public float hudDistance = 1.7f, hudDrop = .22f, hudWidth = 1.6f;
        [Tooltip("VR 里大象最快跑多快（米/秒）、最快转多快（度/秒）、加减速多柔和（越小越柔和）。电脑版是 4.5 / 60 / 6。")]
        public float runSpeed = 3.6f, turnSpeed = 45, inputSmoothing = 3;

        DigiPhantLocomotion loco;
        Transform root, offset;
        Camera cam;
        float yaw;
        bool placed, recentered;

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
            if (demo) { demo.externalInput = true; demo.cameraHeld = true; }
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
            else if (cam) AddTrackedPoseDriver(cam.gameObject);
            gameObject.AddComponent<DivaVRInput>();
        }

        static void AddTrackedPoseDriver(GameObject go)
        {
            if (go.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>()) return;
            var driver = go.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            driver.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationAndPosition;
            driver.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        }

        void Start()
        {
            if (!loco || !loco.travelRoot) { Debug.LogWarning("DIVA_VR: no elephant (DigiPhantLocomotion.travelRoot); the rig stays put."); return; }
            root = loco.travelRoot;
            if (seat == Vector3.zero) seat = MeasureSeat();
            yaw = root.eulerAngles.y;
            Debug.Log($"DIVA_VR rig on: seat {seat} eye {eyeHeight} simulated {DivaVR.Simulated}");
        }

        /// <summary>按大象身体量座位：背最高处再高一点，靠前（肩膀上方，前面是大象的头和耳朵）。</summary>
        Vector3 MeasureSeat()
        {
            var animator = loco.elephantAnimator;
            var skin = animator ? animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).FirstOrDefault() : null;
            if (!skin) return new Vector3(0, 3, 0);
            var b = skin.bounds;
            var center = root.InverseTransformPoint(b.center);
            // 包围盒在 travel root 坐标里的半长（大象可能不是轴对齐）
            var ext = root.InverseTransformVector(b.extents);
            ext = new Vector3(Mathf.Abs(ext.x), Mathf.Abs(ext.y), Mathf.Abs(ext.z));
            return new Vector3(center.x, center.y + ext.y * .92f, center.z + ext.z * .18f);
        }

        void LateUpdate()
        {
            if (!root) return;
            // 水平朝向：慢慢跟上大象，最快 maxYawSpeed
            float target = root.eulerAngles.y;
            float dt = Time.unscaledDeltaTime;
            yaw = placed ? Mathf.MoveTowardsAngle(yaw, target, maxYawSpeed * dt) : target;
            placed = true;
            var rot = Quaternion.Euler(0, yaw, 0);
            transform.SetPositionAndRotation(root.TransformPoint(seat), rot);
            if (!recentered) Recenter();
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
            recentered = true;
        }
    }
}
