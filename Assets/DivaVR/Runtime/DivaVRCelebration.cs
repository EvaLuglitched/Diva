using System.Collections;
using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 冲线的"英雄时刻"（参考守望先锋的 Play of the Game）：
    /// 0.0 s 慢动作（DivaShow 的 Finish 负责）+ 低音轰 + 象鸣 + 驾驶舱震动；四束聚光灯从天上扫下来汇聚到你身上；
    /// 0.6 s 横幅"PLAY OF THE RACE"砸进来；1.0 s 涂装名；1.3 s 分数从 0 嘀嘀嘀涨到最终分；
    /// 全息机甲在正前方升起旋转当奖杯；2.9 s 左右彩纸炮 + 烟花 + 欢呼；3.4 s "CHAMPION"图章。结算页出来后慢慢退开。
    /// </summary>
    public class DivaVRCelebration : MonoBehaviour
    {
        DivaShowDirector show;
        DivaVRHologram holo;
        DivaVRCockpit cockpit;
        RectTransform card; CanvasGroup group;
        TextMeshProUGUI title, sub, score, stamp;
        RectTransform band;
        DivaShowFx fx;
        float shownAt = -1;
        int finalScore; float countTick;
        readonly System.Collections.Generic.List<Transform> beams = new System.Collections.Generic.List<Transform>();
        Transform beamRoot;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            holo = GetComponent<DivaVRHologram>();
            cockpit = GetComponent<DivaVRCockpit>();
            if (!show) { enabled = false; return; }
            var anchor = DivaVR.MomentsAnchor ? DivaVR.MomentsAnchor : transform;
            var go = new GameObject("Celebration Card", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            card = (RectTransform)go.transform;
            card.SetParent(anchor, false);
            card.sizeDelta = new Vector2(1400, 520);
            card.localScale = Vector3.one * (2.4f / 1400f);
            card.localPosition = new Vector3(0, .95f, 0);   // 结算面板的上方，不和它叠
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.worldCamera = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main; c.sortingOrder = 70;
            group = go.GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false;
            var ink = new Color(.06f, .05f, .12f);
            var plate = DivaUi.Panel(card, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1360, 480), ink, new Color(.55f, .5f, .7f, 1), 60);
            plate.stripe = new Color(1f, .45f, .8f); plate.stripeWidth = 26;
            band = DivaUi.Panel(card, "Band", new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1200, 90), new Color(1f, .35f, .72f), new Color(.85f, .2f, .6f), 40).rectTransform;
            title = DivaUi.Text(card, "Title", new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(1200, 90), 64, Color.white);
            sub = DivaUi.Text(card, "Sub", new Vector2(.5f, 1), new Vector2(0, -150), new Vector2(1200, 60), 44, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            score = DivaUi.Text(card, "Score", new Vector2(.5f, 0), new Vector2(0, 60), new Vector2(1200, 150), 140, Color.white);
            stamp = DivaUi.Text(card, "Stamp", new Vector2(1, 1), new Vector2(-180, -175), new Vector2(560, 110), 80, new Color(1f, .9f, .4f));   // 右上角，不压分数
            DivaUi.Glow(score, new Color(1f, .85f, .3f, .95f));
            DivaUi.Glow(title, new Color(1, .3f, .7f, .8f));
            DivaUi.Glow(stamp, new Color(1f, .5f, .1f, .9f), .7f, .5f, .18f);
            stamp.gameObject.SetActive(false);
            if (show.glowMaterial) { fx = new DivaShowFx(new GameObject("Celebration FX").transform, show.glowMaterial, show.bubbleMaterial); BuildBeams(); }
            DivaShowDirector.PhaseChanged += OnPhase;
        }

        void OnDestroy() => DivaShowDirector.PhaseChanged -= OnPhase;

        // 聚光灯：四束很长的发光条，从 14 米高斜着指向大象，绕着慢慢转
        void BuildBeams()
        {
            beamRoot = new GameObject("Celebration Beams").transform;
            for (int i = 0; i < 4; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var col = q.GetComponent<Collider>(); if (col) Destroy(col);
                q.name = "Beam " + i; q.transform.SetParent(beamRoot, false);
                q.transform.localScale = new Vector3(.9f, 18f, 1);
                var r = q.GetComponent<MeshRenderer>();
                var m = new Material(show.glowMaterial);
                var cc = (i % 2 == 0 ? new Color(1f, .6f, .9f) : new Color(.6f, .95f, 1f)) * .5f; cc.a = .35f;
                m.color = cc; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", cc); if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", cc);
                r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                beams.Add(q.transform);
            }
            beamRoot.gameObject.SetActive(false);
        }

        void OnPhase(ShowPhase p)
        {
            if (p == ShowPhase.Finish) { shownAt = Time.unscaledTime; StartCoroutine(Sequence()); }
            else if (p != ShowPhase.Results) { shownAt = -1; if (holo) holo.HeroMoment(false); if (beamRoot) beamRoot.gameObject.SetActive(false); }
        }

        IEnumerator Wait(float s) { float e = Time.unscaledTime + s; while (Time.unscaledTime < e) yield return null; }

        IEnumerator Sequence()
        {
            var snd = show.Sound;
            finalScore = show.TotalScore;
            title.text = DivaText.T("vr.finish.title");
            sub.text = DivaText.T("vr.finish.sub") + "  ·  " + show.SkinName;
            score.text = "0"; sub.alpha = 0; stamp.gameObject.SetActive(false);
            // 0.0 轰、象鸣、震动、聚光灯亮
            if (snd != null) { snd.Play("boom", 1f); snd.Play("trumpet", .8f); }
            if (cockpit) cockpit.Rumble(1);
            if (beamRoot) beamRoot.gameObject.SetActive(true);
            if (holo) holo.HeroMoment(true);
            yield return Wait(.6f);
            // 0.6 横幅砸进来
            cardPop = Time.unscaledTime; if (snd != null) snd.Play("hit", .9f, .7f);
            yield return Wait(.4f);
            sub.alpha = 1;
            yield return Wait(.3f);
            // 1.3 分数嘀嘀嘀涨上去（1.4 秒）
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 1.4f)
            {
                float k = DivaUi.EaseOut((Time.unscaledTime - t0) / 1.4f);
                score.text = Mathf.RoundToInt(finalScore * k).ToString("N0") + " PTS";
                if (snd != null && Time.unscaledTime > countTick) { countTick = Time.unscaledTime + .06f; snd.Play("tick", .35f, 1 + k * .8f); }
                yield return null;
            }
            score.text = finalScore.ToString("N0") + " PTS";
            if (snd != null) snd.Play("confirm", .8f, 1.2f);
            yield return Wait(.2f);
            // 2.9 彩纸炮、烟花、欢呼
            var head = DivaVR.Head ? DivaVR.Head.position : transform.position;
            var fwd = DivaVR.Head ? Vector3.ProjectOnPlane(DivaVR.Head.forward, Vector3.up).normalized : Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            if (fx != null)
            {
                fx.Pyro(head + fwd * 3 - right * 2.5f - Vector3.up * 1.5f, new Color(1f, .5f, .85f) * 2.5f, true);
                fx.Pyro(head + fwd * 3 + right * 2.5f - Vector3.up * 1.5f, new Color(.5f, .95f, 1f) * 2.5f, true);
            }
            if (snd != null) { snd.Play("cheer", .9f); snd.Play("fanfare", .9f); }
            if (cockpit) cockpit.Rumble(.6f);
            var colours = new[] { new Color(1f, .45f, .8f), new Color(1f, .85f, .3f), new Color(.5f, .95f, .85f), new Color(.75f, .6f, 1f) };
            for (int i = 0; i < 6; i++)
            {
                if (fx != null) fx.Firework(head + Quaternion.Euler(0, i * 60 + 30, 0) * fwd * 8 + Vector3.up * 4, colours[i % colours.Length] * 1.3f);
                if (i % 2 == 1) yield return Wait(.35f);
            }
            // 3.4 图章
            stamp.text = DivaText.T("vr.finish.champion"); stamp.gameObject.SetActive(true); stampAt = Time.unscaledTime;
            if (snd != null) snd.Play("powerup", .6f, 1.4f);
        }

        float cardPop = -1, stampAt = -1;

        void LateUpdate()
        {
            if (shownAt < 0) { group.alpha = Mathf.MoveTowards(group.alpha, 0, Time.unscaledDeltaTime * 3); cardPop = -1; return; }
            float t = Time.unscaledTime;
            // 聚光灯：绕着大象转、从高处斜着扫下来汇聚到座位
            if (beamRoot && beamRoot.gameObject.activeSelf)
            {
                var focus = transform.position;
                for (int i = 0; i < beams.Count; i++)
                {
                    float a = (t * 25 + i * 90) * Mathf.Deg2Rad;
                    var top = focus + new Vector3(Mathf.Cos(a) * 9, 14, Mathf.Sin(a) * 9);
                    var b = beams[i];
                    b.position = (top + focus) * .5f;
                    b.rotation = Quaternion.LookRotation(Vector3.Cross(top - focus, Vector3.Cross(top - focus, (DivaVR.Head ? DivaVR.Head.position : focus) - b.position)), top - focus) * Quaternion.Euler(0, 0, 0);
                    b.up = (top - focus).normalized;
                    b.localScale = new Vector3(.9f + .3f * Mathf.Sin(t * 3 + i), Vector3.Distance(top, focus), 1);
                }
            }
            if (cardPop < 0) { group.alpha = 0; return; }
            float ct = t - cardPop;
            group.alpha = Mathf.Clamp01(ct * 6);
            float pop = DivaUi.EaseOutBack(ct / .45f);
            card.localScale = Vector3.one * (2.4f / 1400f) * Mathf.Lerp(2.2f, 1, pop);
            card.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-10, -3, pop));
            if (stamp.gameObject.activeSelf)
            {
                float st = t - stampAt; float sp = DivaUi.EaseOutBack(st / .35f);
                stamp.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.6f, 1, sp);
                stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-18, -8, sp));
            }
            // 结算页出来后慢慢退开
            if (show.Phase == ShowPhase.Results && show.PhaseTime > 2.5f)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, 0, Time.unscaledDeltaTime * 2);
                if (holo) holo.HeroMoment(false);
                if (beamRoot && group.alpha <= 0) beamRoot.gameObject.SetActive(false);
            }
            if (show.Sound != null && show.Phase == ShowPhase.Finish) show.Sound.FadeMusic(1.5f);
        }
    }
}
