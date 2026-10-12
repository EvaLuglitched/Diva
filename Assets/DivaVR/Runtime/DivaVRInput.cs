using DigiPhant;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>VR 里一帧的操作：和电脑版摄像头手势一样的 6 个量，交给原来的 DivaControlState，玩法不用改。</summary>
    public struct DivaVRFrame
    {
        public float go, steer, aim;
        public bool shoot, drink, ult;
        /// <summary>捏的时候眼睛看的方向（世界空间，Vision Pro 的"看 + 捏"）；零向量 = 用头的朝向。</summary>
        public Vector3 look;
    }

    /// <summary>
    /// 把 VR 的操作交给游戏。来源：Vision Pro 的手部追踪和头的位置（DivaVRHands），电脑上是模拟器（键盘鼠标）。
    /// 瞄准用"看哪儿打哪儿"：头的水平朝向相对大象正前方的角度，换算成象鼻水炮的瞄准量。
    /// </summary>
    [DefaultExecutionOrder(-50)]   // 在 DigiPhantController（0）调用 DivaDemo.UpdateInputs 之前
    public class DivaVRInput : MonoBehaviour
    {
        DivaDemo demo;
        DigiPhantLocomotion loco;
        DivaVRSimulator simulator;
        DivaVRHands hands;
        DivaVRAutopilot autopilot;
        DivaVRBubbleTour tour;
        DivaVRGuide guide;
        DivaVRDash dash;
        Diva.Show.DivaShowDirector show;

        /// <summary>这一帧人的操作（开场时游戏拿到的是空的，见 DivaVRBubbleTour.Drive）。</summary>
        public DivaVRFrame Current { get; private set; }

        void Start()
        {
            demo = FindAnyObjectByType<DivaDemo>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            simulator = GetComponent<DivaVRSimulator>();
            hands = GetComponent<DivaVRHands>();
            autopilot = GetComponent<DivaVRAutopilot>();
            tour = GetComponent<DivaVRBubbleTour>();
            guide = GetComponent<DivaVRGuide>();
            dash = GetComponent<DivaVRDash>();
            show = FindAnyObjectByType<Diva.Show.DivaShowDirector>();
        }

        void Update()
        {
            if (!demo) return;
            var head = DivaVR.Head;
            var frame = autopilot ? autopilot.Read() : simulator ? simulator.Read() : hands && head ? hands.Read(head, head.parent) : default;
            // 冲刺中：自动全速往前，把手只管转弯
            if (dash && dash.Active && show && show.Phase == Diva.Show.ShowPhase.Play) frame.go = 1;
            // 辅助瞄准：捏的时候，看的方向 12° 以内、35 米以内最近的靶子就当作看准了它（眼睛的方向有一两度误差，远处的靶子很难正好看中）
            // 牌子在管手势的时候（选涂装、结算、暂停）不做辅助瞄准：那会把看向按钮的视线拐到旁边的靶子上，按错按钮
            bool guideOwns = guide && guide.isActiveAndEnabled && show && (guide.Handles(show.Phase) || guide.PauseHover);
            if (frame.shoot && head && !guideOwns) frame.look = AimAssist(frame.look.sqrMagnitude > .01f ? frame.look : head.forward, head.position);
            // 只有"捏"的时候才用看的方向瞄准：选涂装时随便转头看不会切换涂装，捏住再看左 / 右才切
            frame.aim = frame.shoot ? LookAim(frame.look) : 0;
            // 捏合的确认音（参考 Meta / Ultraleap 的手势设计：手势被识别要立刻有声音或画面反馈）
            if (frame.shoot && !Current.shoot && show && show.Sound != null) show.Sound.Play("tick", .5f);
            Current = frame;
            // 泡泡开场：人的手势只给开场用（捏一下出发），游戏拿不到
            if (tour && tour.Driving) frame = tour.Drive(frame);
            // 选涂装、结算：捏和身体动作归教学牌（DivaVRGuide 按按钮、教动作），游戏只拿到"双手举高"，而且只在可以出发的那一步
            else if (guide && guide.isActiveAndEnabled && show && guide.Handles(show.Phase))
                frame = new DivaVRFrame { ult = frame.ult && guide.AllowStart };
            // 比赛里看着仪表台的 PAUSE 捏一下 = 暂停，这一下不能同时开炮；杆照常
            else if (guide && guide.isActiveAndEnabled && guide.PauseHover) { frame.shoot = false; frame.look = default; frame.aim = 0; }
            demo.State.ClearPeople();
            demo.State.Accept(1, frame.go, frame.steer, frame.aim, frame.shoot, frame.drink, 1, Time.realtimeSinceStartup, frame.ult);
        }

        static Vector3 AimAssist(Vector3 look, Vector3 from)
        {
            Diva.DivaTarget best = null; float bestAngle = 12f;
            foreach (var t in Diva.DivaTarget.All)
            {
                if (!t || !t.IsUp) continue;
                var to = t.AimPoint - from;
                if (to.magnitude > 35) continue;
                float a = Vector3.Angle(look, to);
                if (a < bestAngle) { bestAngle = a; best = t; }
            }
            return best ? (best.AimPoint - from).normalized : look;
        }

        /// <summary>头相对大象正前方转了多少度，按象鼻能转的角度换算成 -1..1。</summary>
        float LookAim(Vector3 gaze)
        {
            var head = DivaVR.Head;
            if (!head || !loco || !loco.travelRoot) return 0;
            var look = Vector3.ProjectOnPlane(gaze.sqrMagnitude > .01f ? gaze : head.forward, Vector3.up);
            var forward = Vector3.ProjectOnPlane(loco.travelRoot.forward, Vector3.up);
            if (look.sqrMagnitude < 1e-4f || forward.sqrMagnitude < 1e-4f) return 0;
            float degrees = Vector3.SignedAngle(forward, look, Vector3.up);
            return Mathf.Clamp(degrees / Mathf.Max(1, demo.trunkAimDegrees), -1, 1);
        }
    }
}
