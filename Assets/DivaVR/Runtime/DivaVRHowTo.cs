using DigiPhant;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 起跑时"驾驶方法"卡片旁边的小动画：卡片上的五行字一行一行亮，左边的 3D 小人同步做那个动作（光看字新手看不懂）。
    /// 没推杆之前卡片多留一会儿（最多 12 秒），推了就按原来的节奏淡出。
    /// </summary>
    public class DivaVRHowTo : MonoBehaviour
    {
        public float clipSeconds = 2.6f;
        static readonly DivaVRGestureDemo.Move[] Clips = { DivaVRGestureDemo.Move.Push, DivaVRGestureDemo.Move.Twist, DivaVRGestureDemo.Move.Pinch, DivaVRGestureDemo.Move.Mouth, DivaVRGestureDemo.Move.HandsUp };

        DivaShowDirector show; DigiPhantLocomotion loco; DivaVRGestureDemo demo;
        int clip = -1; float clipAt; bool moved; ShowPhase lastPhase = (ShowPhase)(-1);

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            if (!show) { enabled = false; return; }
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow"); var body = Resources.Load<Material>("DivaVR Cockpit Body");
            if (glow && body) demo = DivaVRGestureDemo.Create(transform, Vector3.zero, glow, body);
        }

        void LateUpdate()
        {
            var phase = show.Phase;
            if (phase != lastPhase) { lastPhase = phase; if (phase == ShowPhase.Countdown) moved = false; }
            if (loco && loco.CurrentSpeed > .3f) moved = true;
            show.VrCardHold = phase == ShowPhase.Play && !moved && show.PhaseTime < 12f;
            var rect = show.VrCardRect;
            if (!demo || !rect || !show.VrCardVisible)
            {
                if (clip >= 0) { demo?.Hide(); show.VrCardHighlight(-1); }
                clip = -1; return;
            }
            float now = Time.unscaledTime;
            if (clip < 0 || now - clipAt > clipSeconds)
            {
                clip = (clip + 1) % Clips.Length; clipAt = now;
                demo.Show(Clips[clip]); show.VrCardHighlight(clip);
                if (show.Sound != null) show.Sound.Play("tick", .25f);
            }
            // 小人站在卡片左边那块空地上，往眼睛这边提 30 厘米：演示里的靶子 / 按钮在小人前方 20 多厘米，卡片底板不透明，不提就埋进去
            var head = DivaVR.Head;
            var pos = rect.TransformPoint(new Vector3(-420, -70, 0));
            var toEye = head ? (head.position - pos).normalized : -rect.forward;
            demo.transform.position = pos + toEye * .30f;
            demo.transform.rotation = rect.rotation;
            demo.transform.localScale = Vector3.one * (rect.lossyScale.x * 1300f);
        }
    }
}
