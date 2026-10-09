using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// Vision Pro 上的手势：身体当摇杆，手负责精细操作（头和手都是头显给的 3D 位置，单位米）。
    /// 身体前倾 = 前进，左右倾 = 转向（相对坐好时头的位置）；拇指食指捏合 = 喷水（朝你看的方向）；
    /// 手放到嘴边 = 喝水；双手举过头顶 0.5 秒 = 大招 / 确认。
    /// </summary>
    public class DivaVRHands : MonoBehaviour
    {
        [Tooltip("头往前移多少米开始走、多少米跑满。")]
        public float leanStart = .05f, leanFull = .16f;
        [Tooltip("头往左右移多少米开始转、多少米转满。")]
        public float sideStart = .05f, sideFull = .16f;
        [Tooltip("拇指尖和食指尖小于这个距离（米）算捏合。")]
        public float pinchDistance = .02f;
        [Tooltip("手离嘴小于这个距离（米）算喝水。")]
        public float mouthDistance = .16f;
        [Tooltip("双手高过头顶多少米、保持多少秒算举手。")]
        public float ultAbove = .06f, ultHold = .5f;

        XRHandSubsystem hands;
        Vector3 neutral;
        bool hasNeutral;
        float ultSince = -1;
        static readonly List<XRHandSubsystem> found = new List<XRHandSubsystem>();
        // Vision Pro 的"看 + 捏"：系统给出捏下去那一刻眼睛看的方向（按控件路径读，不直接依赖 visionOS 插件的类型）
        readonly InputAction[] gazeDirection = new InputAction[2], gazePhase = new InputAction[2];

        void OnEnable()
        {
            for (int i = 0; i < 2; i++)
            {
                gazeDirection[i] = new InputAction($"Gaze {i} Direction", binding: $"<VisionOSSpatialPointerDevice>/spatialPointer{i}/startRayDirection");
                gazePhase[i] = new InputAction($"Gaze {i} Phase", binding: $"<VisionOSSpatialPointerDevice>/spatialPointer{i}/phase");
                gazeDirection[i].Enable(); gazePhase[i].Enable();
            }
        }

        void OnDisable()
        {
            for (int i = 0; i < 2; i++) { gazeDirection[i]?.Dispose(); gazePhase[i]?.Dispose(); }
        }

        /// <summary>系统的"看 + 捏"正在进行吗；是的话给出看的方向（世界空间）。</summary>
        bool GazePinch(Transform space, out Vector3 look)
        {
            look = Vector3.zero;
            for (int i = 0; i < 2; i++)
            {
                if (gazePhase[i] == null || gazePhase[i].activeControl == null) continue;
                var phase = gazePhase[i].ReadValue<UnityEngine.InputSystem.TouchPhase>();
                if (phase != UnityEngine.InputSystem.TouchPhase.Began && phase != UnityEngine.InputSystem.TouchPhase.Moved && phase != UnityEngine.InputSystem.TouchPhase.Stationary) continue;
                var dir = gazeDirection[i].ReadValue<Vector3>();
                if (dir.sqrMagnitude < .01f) continue;
                look = space.TransformDirection(dir.normalized);
                return true;
            }
            return false;
        }

        public bool Tracking => hands != null && hands.running && (hands.leftHand.isTracked || hands.rightHand.isTracked);

        /// <summary>记下坐好时头的位置（追踪空间），之后的前倾和左右倾都相对它算。</summary>
        public void SetNeutral(Vector3 headLocal) { neutral = headLocal; hasNeutral = true; }

        /// <summary>
        /// head：主摄像机；space：追踪空间（摄像机的父节点，手的关节位置也在这个空间里）。
        /// </summary>
        public DivaVRFrame Read(Transform head, Transform space)
        {
            var f = new DivaVRFrame();
            if (!head || !space) return f;
            if (hands == null || !hands.running)
            {
                SubsystemManager.GetSubsystems(found);
                hands = found.Count > 0 ? found[0] : null;
            }
            if (!hasNeutral) SetNeutral(head.localPosition);

            // 身体：头相对坐好时的位置，换到驾驶舱的前 / 右方向
            var lean = space.localRotation * (head.localPosition - neutral);
            f.go = Mathf.Clamp01((lean.z - leanStart) / Mathf.Max(.01f, leanFull - leanStart));
            float side = Mathf.Abs(lean.x);
            f.steer = Mathf.Sign(lean.x) * Mathf.Clamp01((side - sideStart) / Mathf.Max(.01f, sideFull - sideStart));

            if (GazePinch(space, out var gaze)) { f.shoot = true; f.look = gaze; }
            if (hands == null || !hands.running) return f;
            var mouth = head.position - head.up * .09f + head.forward * .04f;
            int up = 0;
            foreach (var hand in new[] { hands.leftHand, hands.rightHand })
            {
                if (!hand.isTracked) continue;
                bool thumb = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose);
                bool index = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose);
                if (thumb && index && Vector3.Distance(thumbPose.position, indexPose.position) < pinchDistance) f.shoot = true;
                if (index && Vector3.Distance(space.TransformPoint(indexPose.position), mouth) < mouthDistance) f.drink = true;
                if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) && space.TransformPoint(wrist.position).y > head.position.y + ultAbove) up++;
            }
            if (f.drink) f.shoot = false;   // 手在嘴边捏合不算喷水
            if (up == 2) { if (ultSince < 0) ultSince = Time.unscaledTime; }
            else ultSince = -1;
            f.ult = ultSince >= 0 && Time.unscaledTime - ultSince >= ultHold;
            if (f.ult || f.drink) f.go = f.steer = 0;
            return f;
        }
    }
}
