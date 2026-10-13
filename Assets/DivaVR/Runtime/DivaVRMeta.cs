using Diva;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 比赛节奏在驾驶舱里的呈现。规则本身（糖果、燃烧、黄金靶子、低水量、冲刺、辅助瞄准、圈速奖牌）在 PC / VR 共用的
    /// DivaShowMeta 里，这里只接它的事件：
    /// · 燃烧：驾驶舱灯条橙红 + 震动；火焰条进度给仪表。
    /// · 黄金靶子：靶子那里的空间音效（金色底盘由共用层画，浮字走 WorldText）。
    /// · 世界里的浮字（糖果 +15、火箭、黄金靶子）：DivaVRFloatText。
    /// · 大招就绪：上行就绪音 + 横幅 + 震动（VR 独有：PC 版用底部的大招提示条）；放大招那一刻象鸣。
    /// </summary>
    public class DivaVRMeta : MonoBehaviour
    {
        DivaShowDirector show; DivaVRCockpit cockpit;
        bool wasReady;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            cockpit = GetComponent<DivaVRCockpit>();
            if (!show) { enabled = false; return; }
            show.FireChanged += OnFire;
            show.GoldenChanged += OnGolden;
            show.WorldText += OnWorldText;
            show.WallBumped += OnWallBump;
            show.WallChanged += OnWall;
            DivaShowDirector.PhaseChanged += OnPhase;
        }

        // 终点前的冰墙：顶到了座舱震一下；炸开了大震 + 空间音效
        void OnWallBump() { if (cockpit) cockpit.Rumble(.45f); }
        void OnWall(bool up) { if (!up && cockpit) cockpit.Rumble(1f); }

        void OnDestroy()
        {
            if (show) { show.FireChanged -= OnFire; show.GoldenChanged -= OnGolden; show.WorldText -= OnWorldText; show.WallBumped -= OnWallBump; show.WallChanged -= OnWall; }
            DivaShowDirector.PhaseChanged -= OnPhase;
        }

        void OnFire(bool on)
        {
            if (cockpit) { cockpit.SetFire(on); if (on) cockpit.Rumble(.5f); }
            Debug.Log("DIVA_VR fire " + (on ? "on" : "off"));
        }

        void OnGolden(DivaTarget t)
        {
            if (t != null && show.Sound != null) show.Sound.PlayAt("skill", t.AimPoint, .9f, 1.3f);
        }

        // "+15" 这种小字小一点，"ROCKET!" / "GOLDEN TARGET!" 大一点
        void OnWorldText(string text, Vector3 at, Color colour) => DivaVRFloatText.Show(text, at, colour, text.StartsWith("+") ? .9f : 1.3f);

        void OnPhase(ShowPhase p)
        {
            if (p == ShowPhase.Ult && show.Sound != null) show.Sound.Play("trumpet", .7f, 1.1f);
            if (p == ShowPhase.Finish && show.Medal != "") Debug.Log("DIVA_VR medal " + show.Medal);
        }

        void Update()
        {
            if (!show) return;
            bool play = show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Ult;
            if (cockpit) cockpit.FireLevel = play && !show.OnFire ? show.FireLevel : 0;
            bool ready = play && show.Charge >= 100;
            if (ready && !wasReady)
            {
                show.VrBanner(DivaText.T("vr.ult.ready"), DivaText.T("vr.ult.ready.b"), new Color(.85f, .6f, 1f), 2.2f, "ready");
                if (show.Sound != null) show.Sound.Play("rise", .6f, 1.2f);
                if (cockpit) cockpit.Rumble(.3f);
            }
            wasReady = ready;
        }
    }
}
