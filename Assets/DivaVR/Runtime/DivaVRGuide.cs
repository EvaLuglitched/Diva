using Diva.Show;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>
    /// 驾驶舱里的"下一步"指引牌：坐进驾驶舱后永远知道该做什么。
    /// 选涂装：① 捏一下 换涂装（看左边捏 = 上一个）② 双手举过头顶 出发——第二步有两只手的指示灯和按住进度条。
    /// 结算：双手举过头顶 再来一局。比赛里的提示由 DivaVRCoach 负责。
    /// 牌子在眼前 1.4 米、正前方（选涂装时中间是空的：涂装名字在下面，全息大象在右边）。
    /// </summary>
    public class DivaVRGuide : MonoBehaviour
    {
        DivaShowDirector show;
        DivaVRRig rig;
        DivaVRSimulator simulator;
        Camera cam;

        RectTransform panel;
        CanvasGroup group;
        TextMeshProUGUI title, step1, step1b, step2, step2b;
        Image[] dots = new Image[0];
        RectTransform fill;
        GameObject row1, handsRow;
        ShowPhase shownFor = (ShowPhase)(-1);
        const float Width = 1.05f;   // 牌子宽 1.05 米、在 1.4 米外：大约占视野 40°，一眼能读完
        float shownAt;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            rig = GetComponent<DivaVRRig>();
            simulator = GetComponent<DivaVRSimulator>();
            cam = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            if (!show || !rig) { enabled = false; return; }
            var go = new GameObject("Next Step Guide", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            panel = (RectTransform)go.transform;
            panel.SetParent(transform, false);
            panel.sizeDelta = new Vector2(1200, 420);
            panel.localScale = Vector3.one * (Width / 1200f);
            panel.localPosition = new Vector3(0, rig.eyeHeight + .02f, 1.4f);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace; c.worldCamera = cam; c.sortingOrder = 62;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
            var ink = new Color(.06f, .05f, .12f);
            // 底板几乎不透明：后面是热闹的小镇，半透明的话字看不清
            var plate = DivaUi.Panel(panel, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1160, 400), DivaUi.WithAlpha(ink, .96f), DivaUi.WithAlpha(ink, .92f), 24);
            plate.stripe = new Color(1f, .45f, .8f); plate.stripeWidth = 12;
            title = DivaUi.Text(panel, "Title", new Vector2(.5f, 1), new Vector2(0, -58), new Vector2(1100, 90), 64, Color.white);
            DivaUi.Glow(title, new Color(1, .3f, .7f, .9f));
            row1 = new GameObject("Step 1", typeof(RectTransform));
            var r1 = (RectTransform)row1.transform; r1.SetParent(panel, false); r1.anchoredPosition = new Vector2(0, 20); r1.sizeDelta = new Vector2(1100, 110);
            step1 = DivaUi.Text(r1, "Main", new Vector2(.5f, .5f), new Vector2(0, 18), new Vector2(1100, 70), 52, Color.white, TextAlignmentOptions.Center, false);
            step1b = DivaUi.Text(r1, "Sub", new Vector2(.5f, .5f), new Vector2(0, -32), new Vector2(1100, 50), 36, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            var r2 = DivaUi.Rect(panel, "Step 2", new Vector2(.5f, .5f), new Vector2(0, -105), new Vector2(1100, 150));
            step2 = DivaUi.Text(r2, "Main", new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(1100, 70), 52, Color.white, TextAlignmentOptions.Center, false);
            step2b = DivaUi.Text(r2, "Sub", new Vector2(.5f, .5f), new Vector2(0, -8), new Vector2(1100, 50), 36, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            // 两只手的指示灯和按住进度条
            handsRow = new GameObject("Hands", typeof(RectTransform));
            var hr = (RectTransform)handsRow.transform; hr.SetParent(r2, false); hr.anchoredPosition = new Vector2(0, -58); hr.sizeDelta = new Vector2(500, 60);
            var dotTex = DivaUi.MakeTexture("Diva Guide Dot", 64, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); return new Color(1, 1, 1, Mathf.Clamp01((1 - r) * 6)); });
            var dot = Sprite.Create(dotTex, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
            dots = new[]
            {
                DivaUi.Image(hr, "Left", new Vector2(.5f, .5f), new Vector2(-210, 0), new Vector2(40, 40), new Color(1, 1, 1, .25f), dot),
                DivaUi.Image(hr, "Right", new Vector2(.5f, .5f), new Vector2(210, 0), new Vector2(40, 40), new Color(1, 1, 1, .25f), dot)
            };
            var bar = DivaUi.Image(hr, "Hold", new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 14), new Color(1, 1, 1, .18f));
            fill = DivaUi.Image(bar.transform, "Fill", new Vector2(0, .5f), Vector2.zero, new Vector2(340, 14), new Color(1f, .45f, .8f)).rectTransform;
            fill.pivot = new Vector2(0, .5f); fill.anchoredPosition = Vector2.zero; fill.localScale = new Vector3(0, 1, 1);
            DivaText.Changed += Refresh;
        }

        void OnDestroy() => DivaText.Changed -= Refresh;

        void Refresh() { if (show) Fill(show.Phase); }

        void Fill(ShowPhase p)
        {
            if (p == ShowPhase.Select)
            {
                title.text = DivaText.T("vr.guide.select");
                step1.text = DivaText.T("vr.guide.switch"); step1b.text = DivaText.T("vr.guide.switch.b");
                step2.text = DivaText.T("vr.guide.go"); step2b.text = DivaText.T("vr.guide.go.b");
                row1.SetActive(true);
            }
            else
            {
                title.text = DivaText.T("vr.guide.results");
                row1.SetActive(false);
                step2.text = DivaText.T("vr.guide.again"); step2b.text = DivaText.T("vr.guide.go.b");
            }
            foreach (var x in new[] { title, step1, step1b, step2, step2b }) x.ForceMeshUpdate();
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            var phase = show.Phase;
            // 结算页先让人看几秒分数再出现
            bool want = phase == ShowPhase.Select && show.PhaseTime > .8f || phase == ShowPhase.Results && show.PhaseTime > 3.5f;
            if (want && shownFor != phase)
            {
                shownFor = phase; shownAt = t;
                Fill(phase);
                if (show.Sound != null) show.Sound.Play("tick", .4f);
            }
            if (!want) shownFor = (ShowPhase)(-1);
            group.alpha = Mathf.MoveTowards(group.alpha, want ? 1 : 0, dt * (want ? 3 : 5));
            float pop = want ? DivaUi.EaseOutBack((t - shownAt) / .5f) : 1;
            panel.localScale = Vector3.one * (Width / 1200f) * Mathf.Max(.01f, pop);
            // 牌子微微后仰正对眼睛
            var eye = new Vector3(0, rig.eyeHeight, 0);
            panel.localRotation = Quaternion.LookRotation(panel.localPosition - eye);
            if (group.alpha <= 0) return;
            int up = rig.Hands ? rig.Hands.HandsUp : simulator ? simulator.HandsUp : 0;
            float hold = rig.Hands ? rig.Hands.UltProgress : up == 2 ? 1 : 0;
            for (int i = 0; i < dots.Length; i++)
            {
                bool lit = up > i;
                dots[i].color = Color.Lerp(dots[i].color, lit ? new Color(1f, .5f, .85f, 1) : new Color(1, 1, 1, .25f), 1 - Mathf.Exp(-dt * 12));
                dots[i].rectTransform.localScale = Vector3.one * (lit ? 1.2f + .1f * Mathf.Sin(t * 9) : 1);
            }
            fill.localScale = new Vector3(Mathf.Clamp01(hold), 1, 1);
            // 现在该做的那一步轻轻闪
            float pulse = .75f + .25f * Mathf.Sin(t * 4);
            step2.color = new Color(1, 1, 1, up > 0 ? 1 : pulse);
        }
    }
}
