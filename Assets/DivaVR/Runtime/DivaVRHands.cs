using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// Vision Pro 上的手势，和电脑版的摄像头手势对齐（头和手都是头显给的 3D 位置，单位米）：
    /// · 前进：身体前倾（脖子根相对坐好时往前 7 厘米开始走、22 厘米跑满；坐回去就停，不倒车）。灵敏度故意低一点，
    ///   油门有平滑，头小幅晃动不会一顿一顿地改变速度——之前的版本就是因为太灵敏才晕。
    /// · 转弯：歪头（相对坐好时的侧倾 8° 开始、22° 转满，往自己左边歪 = 左转）。
    /// · 喷水：看着靶子捏一下 / 捏住（系统的"看 + 捏"，或拇指食指捏合）；双手前推那版 Carl 觉得不好用，改回来了。
    /// · 吃喝 / 补水：竖大拇指保持 0.4 秒；手放到嘴边也可以。
    /// · 大招 / 确认：双手举到眼前（眼睛高度附近、身前）保持 0.4 秒。举过头顶手会出摄像头视野，所以是眼前不是头顶。
    /// · 按钮：看着按钮捏一下（系统的"看 + 捏"），和喷水分开。
    /// 坐好时的头位置和侧倾在倒数时记一次（Recenter），暂停板上也有 RECENTER。
    /// </summary>
    public class DivaVRHands : MonoBehaviour
    {
        [Tooltip("前倾：脖子根往前移多少米开始走、多少米跑满。")]
        public float leanStart = .07f, leanFull = .22f;
        [Tooltip("歪头转弯：头相对坐好时侧倾多少度开始转、多少度转满（灵敏一点，小幅歪头就转满；转得快不快由 DivaVRInput 的转速上限管）。")]
        public float tiltStart = 5f, tiltFull = 15f;
        [Tooltip("拇指尖和食指尖小于这个距离（米）算捏合（按按钮）。")]
        public float pinchDistance = .02f;
        [Tooltip("手（掌心、指尖或手腕）离嘴小于这个距离（米）算喝水。")]
        public float mouthDistance = .19f;
        [Tooltip("竖大拇指保持多少秒算补水 / 吃喝。")]
        public float thumbsUpHold = .4f;
        [Tooltip("大招：两只手腕都举到比眼睛低不超过这么多米、在身前，保持多少秒。")]
        public float ultBelowEye = .12f, ultHold = .4f;
        [Tooltip("一只手追踪丢了多久以内当作还在。")]
        public float blipSeconds = .3f;

        XRHandSubsystem hands;
        Vector3 neutral;            // 坐好时脖子根在追踪空间里的位置
        float neutralRoll;
        bool hasNeutral;
        float ultSince = -1, go, steer, freezeGoUntil = -1;
        readonly float[] lastUpAt = { -10f, -10f }, thumbsUpSince = { -1f, -1f };
        static readonly XRHandJointID[] MouthJoints = { XRHandJointID.Palm, XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.ThumbTip, XRHandJointID.Wrist };
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
            Diva.Show.DivaShowDirector.PhaseChanged += OnPhase;
        }

        void OnDisable()
        {
            for (int i = 0; i < 2; i++) { gazeDirection[i]?.Dispose(); gazePhase[i]?.Dispose(); }
            Diva.Show.DivaShowDirector.PhaseChanged -= OnPhase;
        }

        // 倒数时人肯定坐好了：这时候的位置就是"坐好"
        void OnPhase(Diva.Show.ShowPhase p) { if (p == Diva.Show.ShowPhase.Countdown) Recenter(); }

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

        /// <summary>现在举到眼前的手有几只（0–2），给"双手举高"的提示用。</summary>
        public int HandsUp { get; private set; }
        /// <summary>双手举高保持了多久（0–1，1 = 够了）。</summary>
        public float UltProgress => ultSince < 0 ? 0 : Mathf.Clamp01((Time.unscaledTime - ultSince) / Mathf.Max(.01f, ultHold));
        /// <summary>现在捏着（喷水）。</summary>
        public bool Pushing { get; private set; }
        /// <summary>前倾了多少米（给教学的反馈条）。</summary>
        public float Lean { get; private set; }

        /// <summary>追踪到的食指尖（世界空间）。space：追踪空间（摄像机的父节点）。</summary>
        public void Fingertips(Transform space, List<Vector3> tips)
        {
            tips.Clear();
            if (hands == null || !hands.running || !space) return;
            foreach (var hand in new[] { hands.leftHand, hands.rightHand })
                if (hand.isTracked && hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var pose)) tips.Add(space.TransformPoint(pose.position));
        }

        /// <summary>头的侧倾角度（度）：往自己左边歪为负，右边为正。</summary>
        static float HeadRoll(Transform head) => -Mathf.Asin(Mathf.Clamp(head.right.y, -1, 1)) * Mathf.Rad2Deg;

        /// <summary>脖子根（追踪空间）：头转动时眼睛那个点会前后晃几厘米，脖子根稳得多。</summary>
        static Vector3 Neck(Transform head) => head.localPosition - head.localRotation * new Vector3(0, .06f, .09f);

        /// <summary>记下现在的坐姿（脖子根的位置、头的侧倾），之后前倾和歪头都相对它算。</summary>
        public void Recenter()
        {
            var head = DivaVR.Head; if (!head) return;
            neutral = Neck(head); neutralRoll = HeadRoll(head); hasNeutral = true;
            Debug.Log($"DIVA_VR recentered neck {neutral:F2} roll {neutralRoll:F1}");
        }

        /// <summary>当前歪头转向量 -1..1。</summary>
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
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (hands == null || !hands.running)
            {
                SubsystemManager.GetSubsystems(found);
                hands = found.Count > 0 ? found[0] : null;
            }
            if (!hasNeutral) Recenter();

            // ---- 身体：前倾 = 油门，歪头 = 转向（不需要手）
            Lean = Neck(head).z - neutral.z;
            float goT = Mathf.InverseLerp(leanStart, leanFull, Lean);
            if (now < freezeGoUntil) goT = go;   // 刚推出手去喷水的一瞬间身体会跟着往前一点，这 0.3 秒油门不动
            go = Mathf.MoveTowards(go, goT, dt * (goT > go ? 2.5f : 2f));   // 慢一点的油门：不会一顿一顿
            steer = Mathf.MoveTowards(steer, HeadTilt(head), dt * 6f);   // 转向跟手一点（Carl：转的时候可以再快一点）
            f.go = go; f.steer = steer;

            // ---- 看 + 捏：按按钮，比赛里就是开炮（看哪打哪）
            if (GazePinch(space, out var gaze)) { f.pinch = true; f.look = gaze; }
            if (hands == null || !hands.running) { f.shoot = f.pinch; Pushing = false; return f; }

            var mouth = head.position - head.up * .09f + head.forward * .04f;
            var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            int up = 0, i = 0;
            foreach (var hand in new[] { hands.leftHand, hands.rightHand })
            {
                if (!hand.isTracked)
                {
                    // 追踪丢了一下：这只手刚才举着就当还举着
                    if (now - lastUpAt[i] < blipSeconds) up++;
                    i++; continue;
                }
                bool thumb = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumbPose);
                bool index = hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var indexPose);
                // 捏合 = 按按钮 / 开炮
                if (thumb && index && Vector3.Distance(thumbPose.position, indexPose.position) < pinchDistance) f.pinch = true;
                // 竖大拇指保持 0.4 秒 = 补水 / 吃喝（和电脑版一样）
                if (ThumbsUp(hand, space)) { if (thumbsUpSince[i] < 0) thumbsUpSince[i] = now; if (now - thumbsUpSince[i] >= thumbsUpHold) f.drink = true; }
                else thumbsUpSince[i] = -1;
                // 手到嘴边也算吃喝：手掌 / 任一指尖 / 手腕哪个离嘴最近算哪个，19 厘米内、不高过眼睛、在脸前面
                foreach (var jid in MouthJoints)
                    if (hand.GetJoint(jid).TryGetPose(out var jp))
                    {
                        var pt = space.TransformPoint(jp.position);
                        if (Vector3.Distance(pt, mouth) < mouthDistance && pt.y < head.position.y + .03f && Vector3.Dot(pt - head.position, fwd) > -.04f) { f.drink = true; break; }
                    }
                if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist))
                {
                    var w = space.TransformPoint(wrist.position);
                    float forward = Vector3.Dot(w - head.position, fwd);
                    // 举手 = 手腕到眼睛高度附近、在身前（不用举过头顶：那里摄像头看不到手）
                    if (w.y > head.position.y - ultBelowEye && forward > .08f) { up++; lastUpAt[i] = now; }
                }
                i++;
            }
            if (f.drink) { f.pinch = false; up = 0; }   // 手在嘴边 / 竖拇指：不算捏、不算举手
            HandsUp = up;
            if (up == 2) { if (ultSince < 0) ultSince = now; }
            else ultSince = -1;
            f.ult = ultSince >= 0 && now - ultSince >= ultHold;
            // 捏 = 喷水（看哪打哪）；刚捏下去的 0.3 秒油门冻住（抬手时身体会跟着动一下）
            if (f.pinch && !Pushing) freezeGoUntil = now + .3f;
            Pushing = f.pinch;
            f.shoot = f.pinch;
            return f;
        }
    }
}
