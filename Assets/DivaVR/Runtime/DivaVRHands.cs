using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// Vision Pro 上的手势：身体当摇杆，手负责精细操作（头和手都是头显给的 3D 位置，单位米）。
    /// 握杆推 = 前进 / 转向（DivaVRLever），歪头也能转（和电脑版的压肩 / 歪头一样）；拇指食指捏合 = 喷水（朝你看的方向）；
    /// 手放到嘴边或竖大拇指 0.4 秒 = 喝水 / 补水；双手举到眼前保持 = 大招 / 确认。
    /// </summary>
    public class DivaVRHands : MonoBehaviour
    {
        [Tooltip("头往前移多少米开始走、多少米跑满。")]
        public float leanStart = .05f, leanFull = .16f;
        [Tooltip("头往左右移多少米开始转、多少米转满。")]
        public float sideStart = .05f, sideFull = .16f;
        [Tooltip("拇指尖和食指尖小于这个距离（米）算捏合。")]
        public float pinchDistance = .02f;
        [Tooltip("手（掌心、指尖或手腕）离嘴小于这个距离（米）算喝水。")]
        public float mouthDistance = .19f;
        [Tooltip("大招：两只手腕都举到比眼睛低不超过这么多米（眼睛的高度附近）、在身前，保持多少秒。举过头顶手会出摄像头视野，追踪丢了就识别不到。")]
        public float ultBelowEye = .12f, ultHold = .4f;
        [Tooltip("一只手追踪丢了多久以内当作还在。")]
        public float blipSeconds = .3f;
        [Tooltip("歪头转弯（和电脑版的压肩 / 歪头一样）：头相对坐好时侧倾多少度开始转、多少度转满。杆推着的时候以杆为准。")]
        public float tiltStart = 8f, tiltFull = 22f;
        [Tooltip("竖大拇指补水（和电脑版一样）：拇指朝上、其他手指握起来，保持多少秒。")]
        public float thumbsUpHold = .4f;

        XRHandSubsystem hands;
        DivaVRHandlebars handlebars;
        DivaVRLever lever;
        Vector3 neutral;
        float neutralRoll;
        bool hasNeutral;
        float ultSince = -1;
        readonly float[] thumbsUpSince = { -1f, -1f };
        readonly float[] lastUpAt = { -10f, -10f };
        static readonly XRHandJointID[] MouthJoints = { XRHandJointID.Palm, XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.ThumbTip, XRHandJointID.Wrist };
        static readonly List<XRHandSubsystem> found = new List<XRHandSubsystem>();
        // Vision Pro 的"看 + 捏"：系统给出捏下去那一刻眼睛看的方向（按控件路径读，不直接依赖 visionOS 插件的类型）
        readonly InputAction[] gazeDirection = new InputAction[2], gazePhase = new InputAction[2];

        void OnEnable()
        {
            handlebars = GetComponent<DivaVRHandlebars>();
            lever = GetComponent<DivaVRLever>();
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

        /// <summary>现在举过头顶的手有几只（0–2），给"双手举高"的提示用。</summary>
        public int HandsUp { get; private set; }
        /// <summary>双手举高保持了多久（0–1，1 = 够了）。</summary>
        public float UltProgress => ultSince < 0 ? 0 : Mathf.Clamp01((Time.unscaledTime - ultSince) / Mathf.Max(.01f, ultHold));

        /// <summary>追踪到的食指尖（世界空间）。space：追踪空间（摄像机的父节点）。</summary>
        public void Fingertips(Transform space, List<Vector3> tips)
        {
            tips.Clear();
            if (hands == null || !hands.running || !space) return;
            foreach (var hand in new[] { hands.leftHand, hands.rightHand })
                if (hand.isTracked && hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) tips.Add(space.TransformPoint(pose.position));
        }

        /// <summary>记下坐好时头的位置（追踪空间），之后的前倾和左右倾都相对它算；歪头转弯也相对此时的头部侧倾算。</summary>
        public void SetNeutral(Vector3 headLocal) { neutral = headLocal; hasNeutral = true; neutralRoll = DivaVR.Head ? HeadRoll(DivaVR.Head) : 0; }

        /// <summary>头的侧倾角度（度）：左耳往下（往自己左边歪）为负，右耳往下为正。</summary>
        static float HeadRoll(Transform head) => -Mathf.Asin(Mathf.Clamp(head.right.y, -1, 1)) * Mathf.Rad2Deg;

        /// <summary>当前歪头转向量 -1..1（相对坐好时的侧倾；没歪到 tiltStart 就是 0）。</summary>
        public float HeadTilt(Transform head)
        {
            float roll = Mathf.DeltaAngle(neutralRoll, HeadRoll(head));
            return Mathf.Sign(roll) * Mathf.InverseLerp(tiltStart, tiltFull, Mathf.Abs(roll));
        }

        /// <summary>竖大拇指：拇指尖在手腕上方、指向上，食指和中指尖收在掌心附近。</summary>
        static bool ThumbsUp(XRHand hand, Transform space)
        {
            if (!hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) || !hand.GetJoint(XRHandJointID.ThumbMetacarpal).TryGetPose(out var thumbBase) ||
                !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) ||
                !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index) || !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var middle)) return false;
            Vector3 tip = space.TransformPoint(thumb.position), root = space.TransformPoint(thumbBase.position), w = space.TransformPoint(wrist.position), p = space.TransformPoint(palm.position);
            var thumbDir = tip - root;
            if (thumbDir.magnitude < .03f || Vector3.Dot(thumbDir.normalized, Vector3.up) < .6f || tip.y < w.y + .04f) return false;
            // 其他手指握起来：指尖离掌心很近（伸直的食指离掌心 8 厘米以上）
            return Vector3.Distance(space.TransformPoint(index.position), p) < .055f && Vector3.Distance(space.TransformPoint(middle.position), p) < .055f;
        }

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

            if (GazePinch(space, out var gaze)) { f.shoot = true; f.look = gaze; }
            if (hands == null || !hands.running) return f;
            // 走和转弯：握拳拉杆（DivaVRLever）。前倾 / 侧倾把头当油门，太晕，不用了
            if (lever) { lever.Read(hands, space, head); f.go = lever.Go; f.steer = lever.Steer; }
            else if (handlebars) { handlebars.Read(hands, space); f.go = handlebars.Go; f.steer = handlebars.Steer; }
            // 歪头转弯（和电脑版"压肩或歪头"对齐）：杆没推左右时，头往哪边歪就往哪边转
            if (Mathf.Abs(f.steer) < .05f) f.steer = HeadTilt(head);
            var mouth = head.position - head.up * .09f + head.forward * .04f;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            int up = 0, i = 0;
            float now = Time.unscaledTime;
            foreach (var hand in new[] { hands.leftHand, hands.rightHand })
            {
                bool onGrip = handlebars && (i == 0 ? handlebars.HeldLeft : handlebars.HeldRight) || lever && lever.Hand == i;   // 握着杆的手指尖是弯的，不算捏
                if (!hand.isTracked)
                {
                    // 追踪丢了一下：这只手刚才举着就当还举着
                    if (now - lastUpAt[i] < blipSeconds) up++;
                    i++; continue;
                }
                bool thumb = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose);
                bool index = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose);
                // 捏合 = 喷水；握着把手时手指本来就是弯的，不算（系统的"看 + 捏"另外算）
                if (!onGrip && thumb && index && Vector3.Distance(thumbPose.position, indexPose.position) < pinchDistance) f.shoot = true;
                // 竖大拇指保持 0.4 秒 = 补水（和电脑版一样；手放嘴边照旧也可以）。握杆的那只手不算
                if (!onGrip && ThumbsUp(hand, space)) { if (thumbsUpSince[i] < 0) thumbsUpSince[i] = now; if (now - thumbsUpSince[i] >= thumbsUpHold) f.drink = true; }
                else thumbsUpSince[i] = -1;
                // 手到嘴边：手掌 / 任一指尖 / 手腕哪个离嘴最近算哪个，19 厘米内、不高过眼睛、在脸前面就算（以前只看食指尖，手一歪就不算）
                foreach (var jid in MouthJoints)
                    if (hand.GetJoint(jid).TryGetPose(out var jp))
                    {
                        var pt = space.TransformPoint(jp.position);
                        if (Vector3.Distance(pt, mouth) < mouthDistance && pt.y < head.position.y + .03f && Vector3.Dot(pt - head.position, fwd) > -.04f) { f.drink = true; break; }
                    }
                if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist))
                {
                    var w = space.TransformPoint(wrist.position);
                    // 举手 = 手腕到眼睛高度附近、在身前（不用举过头顶：那里摄像头看不到手）
                    if (w.y > head.position.y - ultBelowEye && Vector3.Dot(w - head.position, fwd) > .08f) { up++; lastUpAt[i] = now; }
                }
                i++;
            }
            if (f.drink) { f.shoot = false; up = 0; }   // 手在嘴边：不算喷水，也不算举手
            HandsUp = up;
            if (up == 2) { if (ultSince < 0) ultSince = now; }
            else ultSince = -1;
            f.ult = ultSince >= 0 && Time.unscaledTime - ultSince >= ultHold;
            if (f.drink) f.go = f.steer = 0;   // 大招不清油门：举手那一刻 DivaVRDash 会点火全速冲
            return f;
        }
    }
}
