using System.Linq;
using Diva;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 参考守望先锋的反馈节奏：
    /// · 燃烧（ON FIRE）：打靶、吃糖、完成任务攒火焰条，满了进入 12 秒燃烧——驾驶舱灯条橙红、音乐加强、得分 ×1.5；不操作慢慢冷却。
    /// · 黄金靶子：每 24 秒左右随机一个靶子变金 10 秒，三倍分，出现时有提示音和飘字。
    /// · 大招就绪：能量满的一瞬间上行就绪音 + 横幅；放大招那一刻象鸣（慢动作 DivaShowPlay 的 Ult 阶段自己有，这里不碰 timeScale）。
    /// · 低水量：水量低于 25% 时提示音 + 教练提醒。
    /// · 圈速奖牌：冲线按用时给金 / 银 / 铜，英雄时刻的牌子上盖章。
    /// </summary>
    public class DivaVRMeta : MonoBehaviour
    {
        public float goldEvery = 24f, goldSeconds = 10f, fireSeconds = 12f;
        public float goldLap = 70f, silverLap = 100f;

        DivaShowDirector show; DivaGameManager game; DivaVRCockpit cockpit; DigiPhant.DivaDemo demo;
        float fire, fireUntil = -1, lastGold = -10, goldUntil = -1, lastWaterBeep = -10, ultReadyAt = -1;
        int hitsSeen, candiesSeen, tasksSeen; bool wasReady;
        DivaTarget golden; int goldenPoints; Transform goldRing; Material goldMat;
        public string Medal { get; private set; } = "";
        public float Fire => fire;
        public bool OnFire => Time.unscaledTime < fireUntil;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            game = FindAnyObjectByType<DivaGameManager>();
            demo = FindAnyObjectByType<DigiPhant.DivaDemo>();
            cockpit = GetComponent<DivaVRCockpit>();
            if (!show) { enabled = false; return; }
            DivaTarget.Hit += OnHit;
            DivaShowDirector.PhaseChanged += OnPhase;
            // 金环材质：跟驾驶舱灯条一样，有 Resources 资源就用，没有就退回 URP Unlit
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow");
            var unlit = glow ? null : Shader.Find("Universal Render Pipeline/Unlit");
            if (glow || unlit)
            {
                goldMat = glow ? new Material(glow) : new Material(unlit); var c = new Color(1f, .85f, .25f) * 2.2f; goldMat.color = c; goldMat.SetColor("_BaseColor", c);
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col = ring.GetComponent<Collider>(); if (col) Destroy(col);
                ring.name = "Golden Ring"; ring.transform.localScale = new Vector3(2.6f, .04f, 2.6f);
                var r = ring.GetComponent<MeshRenderer>(); r.sharedMaterial = goldMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                goldRing = ring.transform; goldRing.gameObject.SetActive(false);
            }
        }

        void OnDestroy() { DivaTarget.Hit -= OnHit; DivaShowDirector.PhaseChanged -= OnPhase; }

        void OnHit(DivaTarget t) { Heat(.22f); if (t == golden && show.Sound != null) show.Sound.PlayAt("combo", t.AimPoint, .8f, 1.3f); }

        void OnPhase(ShowPhase p)
        {
            if (p == ShowPhase.Countdown) { fire = 0; fireUntil = -1; Medal = ""; EndGold(); lastGold = Time.unscaledTime; show.ScoreMultiplier = 1; }
            if (p == ShowPhase.Ult && show.Sound != null) show.Sound.Play("trumpet", .7f, 1.1f);
            if (p == ShowPhase.Finish)
            {
                float lap = game ? game.RunSeconds : 999;
                Medal = lap <= goldLap ? "vr.medal.gold" : lap <= silverLap ? "vr.medal.silver" : "vr.medal.bronze";
                Debug.Log($"DIVA_VR medal {Medal} lap {lap:F1} s");
                EndGold(); fireUntil = -1; show.ScoreMultiplier = 1;
                if (show.Sound != null) show.Sound.MusicBoost(.45f, 1f);
            }
        }

        void Heat(float amount)
        {
            if (OnFire || show.Phase != ShowPhase.Play) return;
            fire = Mathf.Clamp01(fire + amount);
            if (fire >= 1)
            {
                fireUntil = Time.unscaledTime + fireSeconds; fire = 0;
                show.ScoreMultiplier = 1.5f;
                show.VrBanner(DivaText.T("vr.fire"), DivaText.T("vr.fire.b"), new Color(1f, .55f, .2f), 2.2f, "powerup");
                if (show.Sound != null) { show.Sound.Play("fanfare", .5f, 1.2f); show.Sound.MusicBoost(.6f, 1.06f); }
                if (cockpit) { cockpit.Rumble(.5f); cockpit.SetFire(true); }
                Debug.Log("DIVA_VR on fire");
            }
        }

        void Update()
        {
            if (!show) return;
            float now = Time.unscaledTime;
            bool play = show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Ult;
            // 火焰条：吃糖、完成任务也加热；不操作冷却
            if (play && game != null)
            {
                int candies = CountCandies(), tasks = game.tasks.Count(t => t != null && t.Done);
                if (candies > candiesSeen) Heat(.08f * (candies - candiesSeen));
                if (tasks > tasksSeen) Heat(.3f);
                candiesSeen = candies; tasksSeen = tasks;
                if (!OnFire) fire = Mathf.MoveTowards(fire, 0, Time.unscaledDeltaTime * .04f);
            }
            if (fireUntil > 0 && now >= fireUntil) { fireUntil = -1; show.ScoreMultiplier = 1; if (show.Sound != null) show.Sound.MusicBoost(.45f, 1f); if (cockpit) cockpit.SetFire(false); }
            // 黄金靶子
            if (play && golden == null && now - lastGold > goldEvery) StartGold();
            if (golden != null)
            {
                if (goldRing) { goldRing.position = golden.AimPoint; goldRing.rotation = Quaternion.Euler(0, now * 60, 0); goldRing.localScale = new Vector3(2.6f + .2f * Mathf.Sin(now * 6), .04f, 2.6f + .2f * Mathf.Sin(now * 6)); }
                if (now >= goldUntil || !golden.IsUp || !play) EndGold();
            }
            // 大招就绪
            bool ready = play && show.Charge >= 100;
            if (ready && !wasReady)
            {
                ultReadyAt = now;
                show.VrBanner(DivaText.T("vr.ult.ready"), DivaText.T("vr.ult.ready.b"), new Color(.85f, .6f, 1f), 2.2f, "ready");
                if (show.Sound != null) show.Sound.Play("rise", .6f, 1.2f);
                if (cockpit) cockpit.Rumble(.3f);
            }
            wasReady = ready;
            // 低水量
            if (play && demo != null && demo.State.Water < .25f && now - lastWaterBeep > 6f)
            {
                lastWaterBeep = now; Debug.Log($"DIVA_VR water low {demo.State.Water:F2}");
                if (show.Sound != null) show.Sound.Play("beep", .5f, .8f);
                var coach = GetComponent<DivaVRCoach>(); if (coach) coach.TipNow("vr.coach.water", 3.5f);
            }
        }

        int CountCandies()
        {
            var p = GetComponent<DivaVRPickups>();
            return p ? p.Taken : 0;
        }

        void StartGold()
        {
            var head = DivaVR.Head ? DivaVR.Head.position : transform.position;
            var fwd = DivaVR.Head ? Vector3.ProjectOnPlane(DivaVR.Head.forward, Vector3.up).normalized : Vector3.forward;
            var candidates = DivaTarget.All.Where(t => t && t.IsUp && Vector3.Distance(t.AimPoint, head) < 45 && Vector3.Dot(fwd, (t.AimPoint - head).normalized) > -.2f).ToList();
            if (candidates.Count == 0) { lastGold = Time.unscaledTime; return; }
            golden = candidates[Random.Range(0, candidates.Count)];
            goldenPoints = golden.points; golden.points = goldenPoints * 3;
            goldUntil = Time.unscaledTime + goldSeconds; lastGold = goldUntil;
            if (goldRing) goldRing.gameObject.SetActive(true);
            show.VrBanner(DivaText.T("vr.golden"), DivaText.T("vr.golden.b"), new Color(1f, .85f, .25f), 2f, "ready");
            if (show.Sound != null) show.Sound.PlayAt("skill", golden.AimPoint, .9f, 1.3f);
            DivaVRFloatText.Show(DivaText.T("vr.golden"), golden.AimPoint + Vector3.up * 1.2f, new Color(1f, .85f, .25f), 1.3f);
            Debug.Log($"DIVA_VR golden target {golden.name} for {goldSeconds} s");
        }

        void EndGold()
        {
            if (golden != null) golden.points = goldenPoints;
            golden = null;
            if (goldRing) goldRing.gameObject.SetActive(false);
        }
    }
}
