using System;
using System.Linq;
using DigiPhant;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 自动驾驶演示：自己选涂装开始，按顺序去完成任务（到了要卷鼻子的地方就做喝水动作），路上看到靶子就"看 + 捏"打，
    /// 大招充满就举手，冲线后结算，过一会儿再来一局。
    /// 用途：Vision Pro 模拟器里没有手部追踪时测试整局；展览时无人值守的演示。
    /// 打开：启动参数 -divaAutoplay，环境变量 DIVA_AUTOPLAY=1（模拟器：SIMCTL_CHILD_DIVA_AUTOPLAY=1 xcrun simctl launch …），或者 DivaVRAutopilot.On = true。
    /// </summary>
    public class DivaVRAutopilot : MonoBehaviour
    {
        public static bool On { get; set; } = Requested();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => On = Requested();

        // iOS / visionOS 的 app 拿不到启动参数，所以也认环境变量：SIMCTL_CHILD_DIVA_AUTOPLAY=1 xcrun simctl launch …
        static bool Requested() => Environment.GetCommandLineArgs().Contains("-divaAutoplay") || DivaVRDebug.On("DIVA_AUTOPLAY");

        DivaShowDirector show;
        DigiPhantLocomotion loco;
        DivaGameManager game;
        float shootUntil, nextShot, ultUntil, phaseSeen;
        ShowPhase lastPhase;
        Vector3 shootAt, center;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            game = FindAnyObjectByType<DivaGameManager>();
            if (loco && loco.travelRoot) center = loco.travelRoot.position;
        }

        public DivaVRFrame Read()
        {
            var f = new DivaVRFrame();
            if (!show || !loco || !loco.travelRoot) return f;
            float now = Time.unscaledTime;
            if (show.Phase != lastPhase) { lastPhase = show.Phase; phaseSeen = now; }
            float inPhase = now - phaseSeen;
            // 暂停板开着：不开车不打靶，交给教学牌（按"继续"）
            var guideAny = GetComponent<DivaVRGuide>();
            if (guideAny && guideAny.isActiveAndEnabled && guideAny.Paused) return guideAny.Demo();
            switch (show.Phase)
            {
                case ShowPhase.Select:
                case ShowPhase.Results:
                    // 有教学牌：像人一样按教学走（看着按钮捏、学动作、举手出发）
                    var guide = guideAny;
                    if (guide && guide.isActiveAndEnabled) return guide.Demo();
                    // 看一会儿再举手确认（结算页多停一会儿给人看分数）
                    f.ult = inPhase > (show.Phase == ShowPhase.Select ? 4 : 8) && inPhase % 3 < 1;
                    f.pinch = f.shoot;
                    return f;
                case ShowPhase.Play:
                    Drive(ref f, now);
                    return f;
                default:
                    return f;
            }
        }

        static readonly RaycastHit[] hits = new RaycastHit[16];

        /// <summary>从大象身体中间往某个方向扫一个 1.2 米的球，最近的障碍物在多远（大象自己、驾驶舱不算）。</summary>
        float Clearance(Transform root, Vector3 dir, float range = 9)
        {
            var from = root.position + Vector3.up * 1.6f;
            int n = Physics.SphereCastNonAlloc(from, 1.2f, dir, hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = range;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (!c || c.transform.IsChildOf(root) || (DivaVR.Rig && c.transform.IsChildOf(DivaVR.Rig)) || hits[i].distance <= 0) continue;
                best = Mathf.Min(best, hits[i].distance);
            }
            return best;
        }

        static readonly float[] Tries = { 0, -20, 20, -40, 40, -65, 65, -95, 95 };

        Vector3 PickHeading(Transform root, Vector3 desired, float dist, out float clear)
        {
            var best = desired; float bestScore = float.MinValue; clear = 0;
            float range = Mathf.Min(9, dist);
            foreach (float a in Tries)
            {
                var dir = Quaternion.Euler(0, a, 0) * desired;
                float c = Clearance(root, dir, range);
                // 场地边缘：前方 6 米出界就当堵住了
                var ahead = root.position + dir * 6;
                if (loco.stageRadius > 0 && Vector3.ProjectOnPlane(ahead - center, Vector3.up).magnitude > loco.stageRadius - 2) c = 0;
                float score = c - Mathf.Abs(a) * .05f;
                if (score > bestScore) { bestScore = score; best = dir; clear = c; }
            }
            return best;
        }

        void Drive(ref DivaVRFrame f, float now)
        {
            var root = loco.travelRoot;
            var fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up).normalized;
            // 大招
            if (show.Charge >= 100 && now > ultUntil + 4) ultUntil = now + .8f;
            if (now < ultUntil) { f.ult = true; return; }

            // 下一个没做完的任务
            var task = game ? game.tasks.FirstOrDefault(t => !t.Done && t.landmark) : null;
            if (task != null)
            {
                var goal = task.landmark.position;
                // Eva 的"绕过树干"（Pass）：要从任务点那条线走过去才算，所以目标放在任务点后面 5 米，别停在它跟前
                if (task.check == TaskCheck.Pass) goal += Vector3.ProjectOnPlane(task.passForward, Vector3.up).normalized * 5f;
                var to = Vector3.ProjectOnPlane(goal - root.position, Vector3.up);
                float dist = to.magnitude;
                // 朝目标的方向为中心，左右各试几个角度，挑最空、又最接近目标的方向（场地边缘也算障碍）
                var heading = PickHeading(root, to / Mathf.Max(.01f, dist), dist, out float clear);
                float angle = Vector3.SignedAngle(fwd, heading, Vector3.up);
                f.steer = Mathf.Clamp(angle / 30f, -1, 1);
                f.go = Mathf.Abs(angle) > 70 ? .25f : dist < task.radius * .5f && task.check != TaskCheck.Pass ? 0 : clear < 2.5f ? .35f : 1;
                if (dist < task.radius * .8f && task.check == TaskCheck.CurlTrunk) { f.go = 0; f.steer = 0; f.drink = true; }
                if (dist < task.radius * .8f && task.check == TaskCheck.LiftLegs) f.go = .5f;
            }

            // 打靶：前方 40° 以内、30 米以内最近的靶子，看着它捏住半秒多
            if (now > nextShot && !f.drink)
            {
                var head = DivaVR.Head ? DivaVR.Head.position : root.position;
                var target = FindObjectsByType<DivaTarget>(FindObjectsSortMode.None)
                    .Where(t => Vector3.Distance(t.transform.position, head) < 30 &&
                                Vector3.Angle(fwd, Vector3.ProjectOnPlane(t.transform.position - head, Vector3.up)) < 40)
                    .OrderBy(t => Vector3.Distance(t.transform.position, head)).FirstOrDefault();
                if (target) { shootAt = target.AimPoint; shootUntil = now + .7f; nextShot = now + 2.2f; }
            }
            if (now < shootUntil && DivaVR.Head)
            {
                f.shoot = true;
                f.look = (shootAt - DivaVR.Head.position).normalized;
            }
        }
    }
}
