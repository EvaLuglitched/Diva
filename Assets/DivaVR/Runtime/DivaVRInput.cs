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

        /// <summary>这一帧最终交给游戏的操作（给测试和调试看）。</summary>
        public DivaVRFrame Current { get; private set; }

        void Start()
        {
            demo = FindAnyObjectByType<DivaDemo>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            simulator = GetComponent<DivaVRSimulator>();
            hands = GetComponent<DivaVRHands>();
            autopilot = GetComponent<DivaVRAutopilot>();
        }

        void Update()
        {
            if (!demo) return;
            var head = DivaVR.Head;
            var frame = autopilot ? autopilot.Read() : simulator ? simulator.Read() : hands && head ? hands.Read(head, head.parent) : default;
            // 只有"捏"的时候才用看的方向瞄准：选涂装时随便转头看不会切换涂装，捏住再看左 / 右才切
            frame.aim = frame.shoot ? LookAim(frame.look) : 0;
            Current = frame;
            demo.State.ClearPeople();
            demo.State.Accept(1, frame.go, frame.steer, frame.aim, frame.shoot, frame.drink, 1, Time.realtimeSinceStartup, frame.ult);
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
