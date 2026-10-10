using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 冲线后的"全场最佳"（参考守望先锋的 Play of the Game）：前方一块斜切的大牌子砸进来——PLAY OF THE RACE、涂装名、分数——
    /// 驾驶舱四周彩纸炸开，号角；慢动作由 DivaShow 的 Finish 阶段负责。
    /// </summary>
    public class DivaVRCelebration : MonoBehaviour
    {
        DivaShowDirector show;
        RectTransform card; CanvasGroup group;
        TextMeshProUGUI title, sub, score;
        DivaShowFx fx;
        float shownAt = -1;
        ShowPhase last;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            if (!show) { enabled = false; return; }
            var anchor = DivaVR.MomentsAnchor ? DivaVR.MomentsAnchor : transform;
            var go = new GameObject("Celebration Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            card = (RectTransform)go.transform;
            card.SetParent(anchor, false);
            card.sizeDelta = new Vector2(1400, 520);
            card.localScale = Vector3.one * (2.4f / 1400f);
            card.localPosition = new Vector3(0, .1f, 0);
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.worldCamera = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main; c.sortingOrder = 70;
            group = go.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false;
            var ink = new Color(.06f, .05f, .12f);
            var plate = DivaUi.Panel(card, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1360, 480), ink, new Color(.55f, .5f, .7f, 1), 60);
            plate.stripe = new Color(1f, .45f, .8f); plate.stripeWidth = 26;
            DivaUi.Panel(card, "Band", new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1200, 90), new Color(1f, .35f, .72f), new Color(.85f, .2f, .6f), 40);
            title = DivaUi.Text(card, "Title", new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1200, 90), 64, Color.white);
            sub = DivaUi.Text(card, "Sub", new Vector2(.5f, .5f), new Vector2(0, 20), new Vector2(1200, 80), 54, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            score = DivaUi.Text(card, "Score", new Vector2(.5f, 0), new Vector2(0, 90), new Vector2(1200, 150), 140, Color.white);
            DivaUi.Glow(score, new Color(1f, .85f, .3f, .95f));
            DivaUi.Glow(title, new Color(1, .3f, .7f, .8f));
            if (show.glowMaterial) fx = new DivaShowFx(new GameObject("Celebration FX").transform, show.glowMaterial, show.bubbleMaterial);
            DivaShowDirector.PhaseChanged += OnPhase;
        }

        void OnDestroy() => DivaShowDirector.PhaseChanged -= OnPhase;

        void OnPhase(ShowPhase p)
        {
            if (p == ShowPhase.Finish) { shownAt = Time.unscaledTime + 1.1f; Fill(); }
            else if (p != ShowPhase.Results) shownAt = -1;
            last = p;
        }

        void Fill()
        {
            title.text = DivaText.T("vr.finish.title");
            sub.text = DivaText.T("vr.finish.sub") + "  ·  " + show.SkinName;
            score.text = show.TotalScore.ToString("N0") + " PTS";
        }

        bool burst;
        void LateUpdate()
        {
            if (shownAt < 0) { group.alpha = Mathf.MoveTowards(group.alpha, 0, Time.unscaledDeltaTime * 3); burst = false; return; }
            float t = Time.unscaledTime - shownAt;
            if (t < 0) return;
            if (!burst)
            {
                burst = true;
                if (show.Sound != null) { show.Sound.Play("fanfare", .9f); show.Sound.Play("pop", .6f); }
                if (fx != null)
                {
                    var head = DivaVR.Head ? DivaVR.Head.position : transform.position;
                    var colours = new[] { new Color(1f, .45f, .8f), new Color(1f, .85f, .3f), new Color(.5f, .95f, .85f), new Color(.75f, .6f, 1f) };
                    for (int i = 0; i < 6; i++) fx.Firework(head + Quaternion.Euler(0, i * 60, 0) * Vector3.forward * 4 + Vector3.up * 2.5f, colours[i % colours.Length] * 1.5f);
                }
            }
            group.alpha = Mathf.Clamp01(t * 6);
            // 像图章一样砸进来：大→小带回弹，微微斜
            float pop = DivaUi.EaseOutBack(t / .45f);
            card.localScale = Vector3.one * (2.4f / 1400f) * Mathf.Lerp(2.2f, 1, pop);
            card.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-10, -3, pop));
            // 结算页出来后慢慢退开
            if (show.Phase == ShowPhase.Results && show.PhaseTime > 2.5f) group.alpha = Mathf.MoveTowards(group.alpha, 0, Time.unscaledDeltaTime * 2);
            if (show.Sound != null && show.Phase == ShowPhase.Finish) show.Sound.FadeMusic(1.5f);
        }
    }
}
