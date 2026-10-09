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
    /// 打开：启动参数 -divaAutoplay（模拟器：xcrun simctl launch … -divaAutoplay），或者 DivaVRAutopilot.On = true。
    /// </summary>
    public class DivaVRAutopilot : MonoBehaviour
    {
        public static bool On { get; set; } = Environment.GetCommandLineArgs().Contains("-divaAutoplay");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => On = Environment.GetCommandLineArgs().Contains("-divaAutoplay");

        DivaShowDirector show;
        DigiPhantLocomotion loco;
        DivaGameManager game;
        float shootUntil, nextShot, ultUntil, phaseSeen;
        ShowPhase lastPhase;
        Vector3 shootAt;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            game = FindAnyObjectByType<DivaGameManager>();
        }

        public DivaVRFrame Read()
        {
            var f = new DivaVRFrame();
            if (!show || !loco || !loco.travelRoot) return f;
            float now = Time.unscaledTime;
            if (show.Phase != lastPhase) { lastPhase = show.Phase; phaseSeen = now; }
            float inPhase = now - phaseSeen;
            switch (show.Phase)
            {
                case ShowPhase.Select:
                case ShowPhase.Results:
                    // 看一会儿再举手确认（结算页多停一会儿给人看分数）
                    f.ult = inPhase > (show.Phase == ShowPhase.Select ? 4 : 8) && inPhase % 3 < 1;
                    return f;
                case ShowPhase.Play:
                    Drive(ref f, now);
                    return f;
                default:
                    return f;
            }
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
                var to = Vector3.ProjectOnPlane(task.landmark.position - root.position, Vector3.up);
                float angle = Vector3.SignedAngle(fwd, to, Vector3.up);
                float dist = to.magnitude;
                f.steer = Mathf.Clamp(angle / 30f, -1, 1);
                f.go = Mathf.Abs(angle) > 70 ? .25f : dist < task.radius * .5f ? 0 : 1;
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
