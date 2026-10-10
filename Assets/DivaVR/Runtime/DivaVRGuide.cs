using System;
using System.Collections.Generic;
using Diva.Show;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>
    /// 驾驶舱正前方的"教学牌"：坐进驾驶舱后一步一步教，永远知道下一步做什么，每一步都有按钮可以按。
    /// 第 1 步 选涂装：捏一下 = 下一个；看着按钮捏 = 按按钮（&lt; 上一个、就选这个、&gt; 下一个）。
    /// 第 2 步 学开大象（每次打开 app 第一局才有）：前倾走、左右倾转弯、看 + 捏 喷水、手放嘴边 吃喝，每个动作做一次就打勾，
    ///        牌子上有实时的反馈条；可以跳过。
    /// 第 3 步 出发：双手举过头顶举住（比赛里这就是大招），或者看着"出发"捏一下。
    /// 结算：双手举过头顶或者按"再来一局"。
    /// 按钮用 Vision Pro 的"看 + 捏"：捏下去那一刻眼睛看的方向（DivaVRFrame.look）打到牌子上的哪个按钮就按哪个。
    /// 选涂装和结算时人的手势只给这块牌子用（DivaVRInput 不交给游戏），比赛里的提示由 DivaVRCoach 负责。
    /// </summary>
    public class DivaVRGuide : MonoBehaviour
    {
        enum Step { Hidden, Pick, Learn, Ready, Results }
        enum Move { Lean, Turn, Pinch, Mouth }

        /// <summary>这次打开 app 已经学过开大象了（之后几局选完涂装直接到"出发"）。</summary>
        static bool learned;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => learned = false;

        const float Width = 1.1f;   // 牌子宽 1.1 米、在 1.45 米外：大约占视野 40°，一眼能读完
        const float Units = 1200;    // 牌子的画布宽（UI 单位）

        DivaShowDirector show;
        DivaVRRig rig;
        DivaVRInput input;
        DivaVRSimulator simulator;
        Camera cam;

        RectTransform panel;
        CanvasGroup group;
        DivaSlant plate;
        TextMeshProUGUI stepLabel, title, lineA, lineB;
        GameObject learnRoot, handsRow, statsRow;
        readonly RectTransform[] statFills = new RectTransform[3];
        TextMeshProUGUI stamp; float stampAt = -10;
        DivaMechSkins skinsRef;
        Image[] dots = new Image[0];
        RectTransform fill;

        class Row { public RectTransform rt; public TextMeshProUGUI text, number; public Image icon, meterBack, meter, meterMid; public float doneAt = -1, progress; }
        readonly Row[] rows = new Row[4];
        readonly Image[] dotsLearn = new Image[4];
        Sprite tick, circle;

        class Btn { public string key; public RectTransform rt; public DivaSlant plate; public TextMeshProUGUI label; public Action press; public bool primary; public float pressedAt = -10; public Color baseTop, baseBottom; }
        readonly List<Btn> buttons = new List<Btn>();

        DivaVRGestureDemo demo;
        Btn hovered;
        Step step = Step.Hidden;
        int roundSteps = 3;   // 这一局一共几步（学过了就 2 步：选涂装、出发）
        float stepAt, shownAt, allDoneAt = -1;
        bool lastShoot;
        ShowPhase lastPhase = (ShowPhase)(-1);

        /// <summary>现在要学的动作（没打勾的第一个）。</summary>
        Move Current { get { for (int i = 0; i < rows.Length; i++) if (rows[i].doneAt < 0) return (Move)i; return Move.Mouth; } }
        bool AllDone { get { foreach (var r in rows) if (r.doneAt < 0) return false; return true; } }

        /// <summary>"双手举高"现在能出发 / 再来一局吗（只在第 3 步和结算，免得学动作时不小心开始）。</summary>
        public bool AllowStart => step == Step.Ready || step == Step.Results;
        /// <summary>这一阶段人的手势归这块牌子管（选涂装、结算）。</summary>
        public bool Handles(ShowPhase p) => p == ShowPhase.Select || p == ShowPhase.Results;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            rig = GetComponent<DivaVRRig>();
            input = GetComponent<DivaVRInput>();
            simulator = GetComponent<DivaVRSimulator>();
            cam = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            if (!show || !rig) { enabled = false; return; }
            MakeSprites();
            var go = new GameObject("Next Step Guide", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            panel = (RectTransform)go.transform;
            panel.SetParent(transform, false);
            panel.sizeDelta = new Vector2(Units, 560);
            panel.localScale = Vector3.one * (Width / Units);
            panel.localPosition = new Vector3(0, rig.eyeHeight + .03f, 1.45f);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace; c.worldCamera = cam; c.sortingOrder = 62;
            group = go.GetComponent<CanvasGroup>();
            group.alpha = 0; group.blocksRaycasts = false; group.interactable = false;
            var ink = new Color(.06f, .05f, .12f);
            // 底板完全不透明：后面是热闹的小镇。工程是线性色彩空间，0.96 的透明度混合后小镇还透出 1/4 的亮度，字看不清
            plate = DivaUi.Panel(panel, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1160, 540), ink, new Color(.6f, .6f, .75f, 1), 24);
            plate.stripe = new Color(1f, .45f, .8f); plate.stripeWidth = 12;
            stepLabel = DivaUi.Text(panel, "Step", new Vector2(.5f, 1), new Vector2(0, -38), new Vector2(1100, 40), 30, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            title = DivaUi.Text(panel, "Title", new Vector2(.5f, 1), new Vector2(0, -92), new Vector2(1100, 80), 62, Color.white);
            DivaUi.Glow(title, new Color(1, .3f, .7f, .9f));
            lineA = DivaUi.Text(panel, "Line A", new Vector2(.5f, .5f), new Vector2(0, 50), new Vector2(1100, 64), 46, Color.white, TextAlignmentOptions.Center, false);
            lineB = DivaUi.Text(panel, "Line B", new Vector2(.5f, .5f), new Vector2(0, -2), new Vector2(1100, 44), 32, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);

            // 第 2 步的四行动作
            learnRoot = new GameObject("Learn", typeof(RectTransform));
            var lr = (RectTransform)learnRoot.transform; lr.SetParent(panel, false); lr.sizeDelta = new Vector2(1100, 290); lr.anchoredPosition = new Vector2(0, 8);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = new Row();
                row.rt = DivaUi.Rect(lr, "Row " + i, new Vector2(.5f, .5f), new Vector2(0, -20), new Vector2(1060, 120));
                row.icon = DivaUi.Image(row.rt, "Icon", new Vector2(0, .5f), new Vector2(150, 0), new Vector2(48, 48), new Color(1, 1, 1, .3f), null);
                row.number = DivaUi.Text(row.icon.rectTransform, "Number", new Vector2(.5f, .5f), Vector2.zero, new Vector2(48, 48), 30, new Color(.06f, .05f, .12f), TextAlignmentOptions.Center, false);
                row.number.text = (i + 1).ToString();
                row.text = DivaUi.Text(row.rt, "Text", new Vector2(0, .5f), new Vector2(460, 0), new Vector2(780, 120), 40, Color.white, TextAlignmentOptions.Left, false);
                row.meterBack = DivaUi.Image(row.rt, "Meter", new Vector2(1, .5f), new Vector2(-100, 0), new Vector2(180, 16), new Color(1, 1, 1, .15f));
                row.meter = DivaUi.Image(row.meterBack.rectTransform, "Fill", new Vector2(0, .5f), Vector2.zero, new Vector2(180, 16), new Color(1f, .45f, .8f));
                row.meter.rectTransform.pivot = new Vector2(0, .5f); row.meter.rectTransform.anchoredPosition = Vector2.zero;
                // 左右倾那一行：中间一条线，往哪边倾就往哪边长
                row.meterMid = DivaUi.Image(row.meterBack.rectTransform, "Mid", new Vector2(.5f, .5f), Vector2.zero, new Vector2(4, 28), new Color(1, 1, 1, .7f));
                row.meterMid.gameObject.SetActive(i == (int)Move.Turn);
                rows[i] = row;
            }

            // 教学进度点（4 个动作）
            for (int i = 0; i < 4; i++)
                dotsLearn[i] = DivaUi.Image(lr, "Dot " + i, new Vector2(.5f, 0), new Vector2(-54 + i * 36, 44), new Vector2(18, 18), new Color(1, 1, 1, .3f), null);
            foreach (var d in dotsLearn) d.sprite = circle;
            // 第 3 步：两只手的指示灯和按住进度条
            handsRow = new GameObject("Hands", typeof(RectTransform));
            var hr = (RectTransform)handsRow.transform; hr.SetParent(panel, false); hr.anchoredPosition = new Vector2(0, -60); hr.sizeDelta = new Vector2(500, 60);
            dots = new[]
            {
                DivaUi.Image(hr, "Left", new Vector2(.5f, .5f), new Vector2(-210, 0), new Vector2(40, 40), new Color(1, 1, 1, .25f), null),
                DivaUi.Image(hr, "Right", new Vector2(.5f, .5f), new Vector2(210, 0), new Vector2(40, 40), new Color(1, 1, 1, .25f), null)
            };
            foreach (var d in dots) d.sprite = circle;
            foreach (var r in rows) r.icon.sprite = circle;
            var bar = DivaUi.Image(hr, "Hold", new Vector2(.5f, .5f), Vector2.zero, new Vector2(340, 14), new Color(1, 1, 1, .18f));
            fill = DivaUi.Image(bar.transform, "Fill", new Vector2(0, .5f), Vector2.zero, new Vector2(340, 14), new Color(1f, .45f, .8f)).rectTransform;
            fill.pivot = new Vector2(0, .5f); fill.anchoredPosition = Vector2.zero; fill.localScale = new Vector3(0, 1, 1);
            // 选涂装页：三条属性（装饰用，按涂装变化）
            statsRow = new GameObject("Stats", typeof(RectTransform));
            var sr = (RectTransform)statsRow.transform; sr.SetParent(panel, false); sr.anchoredPosition = new Vector2(0, -70); sr.sizeDelta = new Vector2(1000, 60);
            string[] statKeys = { "vr.stat.speed", "vr.stat.cannon", "vr.stat.bubble" };
            for (int i = 0; i < 3; i++)
            {
                float x = -330 + i * 330;
                var lbl = DivaUi.Text(sr, "Stat " + i, new Vector2(.5f, .5f), new Vector2(x, 16), new Vector2(300, 36), 26, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
                DivaUi.Localize(lbl, () => DivaText.T(statKeys[i]));
                var back = DivaUi.Image(sr, "Bar " + i, new Vector2(.5f, .5f), new Vector2(x, -12), new Vector2(240, 12), new Color(1, 1, 1, .15f));
                var fillImg = DivaUi.Image(back.rectTransform, "Fill", new Vector2(0, .5f), Vector2.zero, new Vector2(240, 12), new Color(1f, .45f, .8f));
                fillImg.rectTransform.pivot = new Vector2(0, .5f); fillImg.rectTransform.anchoredPosition = Vector2.zero;
                statFills[i] = fillImg.rectTransform;
            }
            // 确认时砸下来的图章
            stamp = DivaUi.Text(panel, "Stamp", new Vector2(.5f, .5f), new Vector2(0, 40), new Vector2(1100, 160), 110, new Color(1f, .9f, .4f));
            DivaUi.Glow(stamp, new Color(1f, .5f, .1f, .9f), .7f, .5f, .18f);
            stamp.gameObject.SetActive(false);
            skinsRef = FindAnyObjectByType<DivaMechSkins>();
            learnRoot.SetActive(false); handsRow.SetActive(false); statsRow.SetActive(false);
            // 动作演示：牌子上方
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow"); var body = Resources.Load<Material>("DivaVR Cockpit Body");
            if (glow && body) demo = DivaVRGestureDemo.Create(transform, new Vector3(-.12f, rig.eyeHeight + .36f, 1.4f), glow, body);
            DivaText.Changed += Refresh;
        }

        void OnDestroy() => DivaText.Changed -= Refresh;

        void MakeSprites()
        {
            var dotTex = DivaUi.MakeTexture("Diva Guide Dot", 64, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); return new Color(1, 1, 1, Mathf.Clamp01((1 - r) * 6)); });
            circle = Sprite.Create(dotTex, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
            // 打勾：两段粗线
            var tickTex = DivaUi.MakeTexture("Diva Guide Tick", 64, (x, y) =>
            {
                float d = Mathf.Min(Segment(new Vector2(x, y), new Vector2(-.55f, .02f), new Vector2(-.15f, -.42f)),
                                    Segment(new Vector2(x, y), new Vector2(-.15f, -.42f), new Vector2(.6f, .5f)));
                return new Color(1, 1, 1, Mathf.Clamp01((.14f - d) * 20));
            });
            tick = Sprite.Create(tickTex, new Rect(0, 0, 64, 64), new Vector2(.5f, .5f));
        }

        static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ------------------------------------------------------------------ buttons
        void ClearButtons()
        {
            foreach (var b in buttons) if (b.rt) Destroy(b.rt.gameObject);
            buttons.Clear();
        }

        void AddButton(string key, float x, float width, bool primary, Action press)
        {
            var b = new Btn { key = key, primary = primary, press = press };
            b.rt = DivaUi.Rect(panel, "Button " + key, new Vector2(.5f, 0), new Vector2(x, 80), new Vector2(width, 104));
            b.baseTop = primary ? new Color(1f, .38f, .74f) : new Color(.26f, .22f, .4f);
            b.baseBottom = primary ? new Color(.82f, .22f, .6f) : new Color(.17f, .14f, .28f);
            b.plate = DivaUi.Panel(b.rt, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(width, 104), b.baseTop, b.baseBottom, 18);
            b.label = DivaUi.Text(b.rt, "Label", new Vector2(.5f, .5f), new Vector2(0, 2), new Vector2(width - 20, 90), primary ? 44 : 40, Color.white);
            b.label.text = DivaText.T(key);
            buttons.Add(b);
        }

        /// <summary>从眼睛沿 dir 看过去，打到牌子上的哪个按钮（按钮四周多算 30 个单位：眼睛看的方向有一两度误差）。</summary>
        Btn HitButton(Vector3 origin, Vector3 dir)
        {
            if (!panel || group.alpha < .5f || dir.sqrMagnitude < 1e-4f) return null;
            var plane = new Plane(panel.forward, panel.position);
            var ray = new Ray(origin, dir.normalized);
            if (!plane.Raycast(ray, out float d)) return null;
            var p = ray.GetPoint(d);
            Btn best = null; float bestDist = float.MaxValue;
            foreach (var b in buttons)
            {
                var local = (Vector2)b.rt.InverseTransformPoint(p);
                var r = b.rt.rect;
                float dx = Mathf.Max(0, Mathf.Abs(local.x - r.center.x) - r.width * .5f), dy = Mathf.Max(0, Mathf.Abs(local.y - r.center.y) - r.height * .5f);
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist < 70 && dist < bestDist) { best = b; bestDist = dist; }
            }
            return best;
        }

        Vector3 ButtonWorld(Btn b) => b.rt.TransformPoint(b.rt.rect.center);

        // ------------------------------------------------------------------ steps
        void Go(Step next)
        {
            step = next; stepAt = Time.unscaledTime;
            allDoneAt = -1;
            ClearButtons();
            if (next == Step.Learn) foreach (var r in rows) { r.doneAt = -1; r.progress = 0; }
            switch (next)
            {
                case Step.Pick:
                    roundSteps = learned ? 2 : 3;
                    AddButton("vr.guide.prev", -410, 150, false, () => show.VrStepSkin(-1));
                    AddButton("vr.guide.choose", 0, 540, true, () => { stampAt = Time.unscaledTime; stamp.text = DivaText.T("vr.guide.locked"); stamp.gameObject.SetActive(true); if (show.Sound != null) show.Sound.Play("powerup", .5f, 1.3f); Invoke(nameof(AfterStamp), .9f); });
                    AddButton("vr.guide.next", 410, 150, false, () => show.VrStepSkin(1));
                    break;
                case Step.Learn:
                    AddButton("vr.guide.skip", 400, 240, false, () => { learned = true; Go(Step.Ready); });
                    break;
                case Step.Ready:
                    AddButton("vr.guide.back", -340, 300, false, () => Go(Step.Pick));
                    AddButton("vr.guide.start", 140, 460, true, () => show.VrConfirm());
                    break;
                case Step.Results:
                    AddButton("vr.guide.again.btn", 0, 480, true, () => show.VrConfirm());
                    break;
            }
            Fill();
            UpdateDemo();
            if (next != Step.Hidden && show.Sound != null) show.Sound.Play("tick", .4f);
            Debug.Log("DIVA_VR guide " + next);
        }

        void HideStamp() { if (step != Step.Pick) stamp.gameObject.SetActive(false); }
        void AfterStamp() { stamp.gameObject.SetActive(false); if (step == Step.Pick) Go(learned ? Step.Ready : Step.Learn); }

        void Refresh()
        {
            if (step == Step.Hidden) return;
            foreach (var b in buttons) b.label.text = DivaText.T(b.key);
            Fill();
        }

        void Fill()
        {
            int total = roundSteps;
            learnRoot.SetActive(step == Step.Learn);
            handsRow.SetActive(step == Step.Ready);
            statsRow.SetActive(step == Step.Pick);
            // 结算的牌子矮一些：两行字往下挪，不和标题叠在一起
            bool compact = step == Step.Results;
            lineA.rectTransform.anchoredPosition = new Vector2(0, compact ? 34 : 56);
            lineB.rectTransform.anchoredPosition = new Vector2(0, compact ? -40 : 8);
            lineA.gameObject.SetActive(step != Step.Learn);
            lineB.gameObject.SetActive(step != Step.Learn && step != Step.Results);
            switch (step)
            {
                case Step.Pick:
                    stepLabel.text = DivaText.F("vr.guide.step", 1, total);
                    title.text = DivaText.T("vr.guide.pick");
                    lineA.text = DivaText.T("vr.guide.pick.a");
                    lineB.text = DivaText.T("vr.guide.pick.b");
                    break;
                case Step.Learn:
                    stepLabel.text = DivaText.F("vr.guide.step", 2, 3);
                    title.text = DivaText.T("vr.guide.learn");
                    string[] keys = { "lean", "turn", "pinch", "mouth" };
                    for (int i = 0; i < rows.Length; i++)
                        rows[i].text.text = $"{DivaText.T("vr.guide.learn." + keys[i])}\n<color=#8CFFE6><size=30>{DivaText.T("vr.guide.learn." + keys[i] + ".b")}</size></color>";
                    break;
                case Step.Ready:
                    stepLabel.text = DivaText.F("vr.guide.step", total, total);
                    title.text = DivaText.T("vr.guide.ready");
                    lineA.text = DivaText.T("vr.guide.ready.a");
                    lineB.text = DivaText.T("vr.guide.ready.b");
                    break;
                case Step.Results:
                    stepLabel.text = "";
                    title.text = DivaText.T("vr.guide.results");
                    lineA.text = DivaText.T("vr.guide.again");
                    lineB.text = DivaText.T("vr.guide.again.b");
                    break;
            }
            foreach (var x in new[] { stepLabel, title, lineA, lineB }) x.ForceMeshUpdate();
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            var phase = show.Phase;
            if (phase != lastPhase) { lastPhase = phase; if (step != Step.Hidden) Go(Step.Hidden); }
            // 开场还没演完时不出来
            bool arrived = DivaVR.ArrivalDone || !GetComponent<DivaVRBubbleTour>();
            if (step == Step.Hidden && arrived)
            {
                if (phase == ShowPhase.Select && show.PhaseTime > .8f) Go(Step.Pick);
                else if (phase == ShowPhase.Results && show.PhaseTime > 3.5f) Go(Step.Results);
            }
            var f = input ? input.Current : default;
            bool pinch = f.shoot && !lastShoot;
            lastShoot = f.shoot;
            if (step == Step.Hidden) return;
            // 头朝向哪个按钮哪个就高亮；捏一下按下它（眼睛看的方向有的话优先）
            var headT = DivaVR.Head;
            hovered = headT ? HitButton(headT.position, headT.forward) : null;
            if (pinch && headT)
            {
                var head = headT;
                var b = (f.look.sqrMagnitude > .01f ? HitButton(head.position, f.look) : null) ?? hovered;
                if (b != null) Press(b);
                else if (step == Step.Pick) show.VrStepSkin(1);   // 捏一下 = 下一个涂装（不用先找按钮）
                else if (step == Step.Learn) Done(Move.Pinch);
            }
            if (step == Step.Learn) Practice(f);
        }

        void Press(Btn b)
        {
            b.pressedAt = Time.unscaledTime;
            if (show.Sound != null) show.Sound.Play(b.primary ? "confirm" : "tick", b.primary ? .7f : .5f);
            Debug.Log("DIVA_VR guide button " + b.key);
            b.press?.Invoke();
        }

        void UpdateDemo()
        {
            if (!demo) return;
            if (step == Step.Learn && !AllDone) { demo.Show((DivaVRGestureDemo.Move)(int)Current); demo.transform.localScale = Vector3.one * 1.7f; demo.transform.localPosition = new Vector3(-.12f, rig.eyeHeight + .4f, 1.4f); }
            else if (step == Step.Ready) { demo.Show(DivaVRGestureDemo.Move.HandsUp); demo.transform.localScale = Vector3.one * 1.3f; demo.transform.localPosition = new Vector3(-.12f, rig.eyeHeight + .38f, 1.4f); }
            else demo.Hide();
        }

        void Done(Move m)
        {
            var r = rows[(int)m];
            if (r.doneAt >= 0) return;
            r.doneAt = Time.unscaledTime; r.progress = 1;
            stampAt = Time.unscaledTime; stamp.text = DivaText.T("vr.guide.nice"); stamp.gameObject.SetActive(true); CancelInvoke(nameof(HideStamp)); Invoke(nameof(HideStamp), .8f);
            UpdateDemo();
            if (show.Sound != null) show.Sound.Play("combo", .5f);
            Debug.Log("DIVA_VR guide learned " + m);
            if (AllDone) { allDoneAt = Time.unscaledTime; learned = true; }
        }

        float noGripSince = -1;
        void Practice(DivaVRFrame f)
        {
            float dt = Time.unscaledDeltaTime;
            // 学推把手：4 秒还没握到，把握把挪到手的位置（手放在哪儿都行）
            var bars = GetComponent<DivaVRHandlebars>();
            if (bars && Current == Move.Lean && rows[(int)Move.Lean].doneAt < 0)
            {
                if (bars.Held == 2) noGripSince = -1;
                else if (noGripSince < 0) noGripSince = Time.unscaledTime;
                else if (Time.unscaledTime - noGripSince > 4f) { bars.AdoptNow(.45f); noGripSince = Time.unscaledTime + 4f; }
            }
            // 每个动作做够一小会儿才算（不是一碰就算）；做的时候反馈条跟着动
            void Hold(Move m, bool on, float seconds)
            {
                var r = rows[(int)m];
                if (r.doneAt >= 0) return;
                r.progress = on ? r.progress + dt / seconds : Mathf.Max(0, r.progress - dt);
                if (r.progress >= 1) Done(m);
            }
            Hold(Move.Lean, f.go > .45f, .6f);
            Hold(Move.Turn, Mathf.Abs(f.steer) > .45f, .5f);
            Hold(Move.Mouth, f.drink, .4f);
            if (allDoneAt > 0 && Time.unscaledTime > allDoneAt + 1.2f) Go(Step.Ready);
        }

        void LateUpdate()
        {
            if (!panel) return;
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool want = step != Step.Hidden;
            if (want && group.alpha <= 0) shownAt = t;
            group.alpha = Mathf.MoveTowards(group.alpha, want ? 1 : 0, dt * (want ? 3 : 5));
            float pop = want ? DivaUi.EaseOutBack((t - shownAt) / .5f) : 1;
            panel.localScale = Vector3.one * (Width / Units) * (step == Step.Results ? .72f : 1) * Mathf.Max(.01f, pop);
            // 结算时牌子放低一点、矮一点，不挡分数
            bool results = step == Step.Results;
            // 牌子稍微偏左：右边留给全息大象
            // 结算：右下角（原来电脑版"再来一局"按钮的位置），小一点，不挡分数和排行榜
            var at = results ? new Vector3(.32f, rig.eyeHeight - .34f, 1.3f) : new Vector3(-.12f, rig.eyeHeight + .03f, 1.45f);
            panel.localPosition = group.alpha <= .01f ? at : Vector3.Lerp(panel.localPosition, at, 1 - Mathf.Exp(-dt * 6));
            panel.sizeDelta = new Vector2(Units, results ? 400 : 560);
            plate.rectTransform.sizeDelta = new Vector2(1160, results ? 380 : 540);
            // 牌子正对眼睛
            var eye = new Vector3(0, rig.eyeHeight, 0);
            panel.localRotation = Quaternion.LookRotation(panel.localPosition - eye);
            if (group.alpha <= 0) return;

            // 属性条跟着涂装变（装饰：每个涂装一套固定的数）
            if (step == Step.Pick && skinsRef)
            {
                int k = Mathf.Max(0, skinsRef.Current);
                float[] vals = { .55f + .4f * Mathf.PerlinNoise(k * 1.7f, .3f), .55f + .4f * Mathf.PerlinNoise(k * 1.7f, 1.3f), .55f + .4f * Mathf.PerlinNoise(k * 1.7f, 2.3f) };
                for (int i = 0; i < 3; i++) statFills[i].localScale = new Vector3(Mathf.Lerp(statFills[i].localScale.x, vals[i], 1 - Mathf.Exp(-dt * 8)), 1, 1);
            }
            // 图章：大→小砸下来；图章在的时候把下面的字收起来（不叠在一起）
            bool stamping = stamp.gameObject.activeSelf;
            float textAlpha = stamping ? 0 : 1;
            title.alpha = Mathf.MoveTowards(title.alpha, textAlpha, dt * 12); lineA.alpha = title.alpha; lineB.alpha = title.alpha; stepLabel.alpha = title.alpha;
            foreach (var r in rows) if (r.rt.gameObject.activeSelf) { r.text.alpha = stamping ? 0 : (r.doneAt >= 0 ? .55f : 1); }
            if (statsRow.activeSelf) statsRow.transform.localScale = Vector3.one * (stamping ? 0 : 1);
            if (stamping)
            {
                float st = t - stampAt; float sp = DivaUi.EaseOutBack(st / .35f);
                stamp.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.6f, 1, sp);
                stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-14, -6, sp));
            }
            // 按钮：主按钮轻轻呼吸，按下时缩一下、亮一下
            foreach (var b in buttons)
            {
                float since = t - b.pressedAt;
                float press = since < .25f ? 1 - .12f * Mathf.Sin(since / .25f * Mathf.PI) : 1;
                float breathe = b.primary ? 1 + .025f * Mathf.Sin(t * 3) : 1;
                b.rt.localScale = Vector3.one * press * breathe;
                float lit = since < .3f ? 1 - since / .3f : 0;
                bool hov = b == hovered;
                b.rt.localScale *= hov ? 1.08f : 1f;
                b.plate.Set(Color.Lerp(b.baseTop, Color.white, Mathf.Max(lit * .5f, hov ? .28f : 0)), Color.Lerp(b.baseBottom, Color.white, Mathf.Max(lit * .3f, hov ? .18f : 0)));
            }

            if (step == Step.Learn)
            {
                var f = input ? input.Current : default;
                var current = Current;
                for (int i = 0; i < dotsLearn.Length; i++)
                    dotsLearn[i].color = rows[i].doneAt >= 0 ? new Color(.5f, 1f, .7f) : (Move)i == current ? new Color(1f, .5f, .85f) : new Color(1, 1, 1, .3f);
                for (int i = 0; i < rows.Length; i++)
                {
                    var r = rows[i];
                    bool done = r.doneAt >= 0, now = !done && (Move)i == current;
                    // 一次只看一个动作：当前的居中放大，做完的一瞬间留着看打勾
                    bool visible = now || (done && t - r.doneAt < 1f);
                    r.rt.gameObject.SetActive(visible);
                    if (!visible) continue;
                    r.rt.localScale = Vector3.one * 1.15f;
                    r.icon.sprite = done ? tick : circle;
                    r.number.gameObject.SetActive(!done);
                    r.icon.color = Color.Lerp(r.icon.color, done ? new Color(.5f, 1f, .7f) : now ? new Color(1f, .5f, .85f) : new Color(1, 1, 1, .3f), 1 - Mathf.Exp(-dt * 10));
                    float pulse = now ? 1 + .06f * Mathf.Sin(t * 5) : 1;
                    float popDone = done ? DivaUi.EaseOutBack((t - r.doneAt) / .4f) : 1;
                    r.icon.rectTransform.localScale = Vector3.one * pulse * Mathf.Max(.2f, popDone);
                    r.text.alpha = stamp.gameObject.activeSelf ? 0 : done ? .55f : now ? 1 : .45f;
                    // 实时反馈：前倾多少、往哪边倾、手在不在嘴边、有没有捏
                    float live = i == (int)Move.Lean ? f.go : i == (int)Move.Turn ? f.steer : i == (int)Move.Mouth ? (f.drink ? 1 : 0) : (f.shoot ? 1 : 0);
                    var m = r.meter.rectTransform;
                    if (i == (int)Move.Turn && !done)
                    {
                        float v = Mathf.Clamp(live, -1, 1);
                        m.anchorMin = m.anchorMax = new Vector2(.5f, .5f);
                        m.pivot = new Vector2(v < 0 ? 1 : 0, .5f);
                        m.anchoredPosition = Vector2.zero;
                        m.sizeDelta = new Vector2(90, 16);
                        m.localScale = new Vector3(Mathf.Abs(v), 1, 1);
                    }
                    else
                    {
                        m.anchorMin = m.anchorMax = new Vector2(0, .5f);
                        m.pivot = new Vector2(0, .5f);
                        m.anchoredPosition = Vector2.zero;
                        m.sizeDelta = new Vector2(180, 16);
                        m.localScale = new Vector3(done ? 1 : Mathf.Clamp01(Mathf.Max(live, r.progress)), 1, 1);
                    }
                    if (i == (int)Move.Turn) r.meterMid.gameObject.SetActive(!done);
                    r.meter.color = done ? new Color(.5f, 1f, .7f) : new Color(1f, .45f, .8f);
                }
            }

            if (step == Step.Ready)
            {
                int up = rig.Hands ? rig.Hands.HandsUp : simulator ? simulator.HandsUp : 0;
                float hold = rig.Hands ? rig.Hands.UltProgress : up == 2 ? 1 : 0;
                for (int i = 0; i < dots.Length; i++)
                {
                    bool lit = up > i;
                    dots[i].color = Color.Lerp(dots[i].color, lit ? new Color(1f, .5f, .85f, 1) : new Color(1, 1, 1, .25f), 1 - Mathf.Exp(-dt * 12));
                    dots[i].rectTransform.localScale = Vector3.one * (lit ? 1.15f : 1);
                }
                fill.localScale = new Vector3(Mathf.Clamp01(hold), 1, 1);
            }
        }

        // ------------------------------------------------------------------ autopilot
        float demoPressAt = -1; Btn demoTarget;

        /// <summary>
        /// 自动驾驶（测试、无人值守演示）在选涂装和结算时的操作：像人一样按教学走一遍——
        /// 看着"&gt;"捏一下换涂装，看着"就选这个"捏，前倾、左右倾、捏、手放嘴边，最后举双手出发；结算时按"再来一局"。
        /// </summary>
        public DivaVRFrame Demo()
        {
            var f = new DivaVRFrame();
            float t = Time.unscaledTime, inStep = t - stepAt;
            var head = DivaVR.Head;
            Btn Find(string key) => buttons.Find(b => b.key == key);
            void PinchAt(Btn b)
            {
                if (b == null || !head) return;
                if (demoTarget != b) { demoTarget = b; demoPressAt = t; }
                // 捏 0.2 秒，看着按钮
                if (t - demoPressAt < .2f) { f.shoot = true; f.look = (ButtonWorld(b) - head.position).normalized; }
            }
            switch (step)
            {
                case Step.Pick:
                    if (inStep > 1.5f && inStep < 2.2f) PinchAt(Find("vr.guide.next"));
                    else if (inStep > 3.2f) PinchAt(Find("vr.guide.choose"));
                    break;
                case Step.Learn:
                    if (inStep < 1f || allDoneAt > 0) break;
                    switch (Current)
                    {
                        case Move.Lean: f.go = 1; break;
                        case Move.Turn: f.steer = -1; break;
                        // 看左边（不看按钮）捏一下
                        case Move.Pinch:
                            if (head && t % .6f < .25f) { f.shoot = true; f.look = Quaternion.AngleAxis(-35, Vector3.up) * head.forward; }
                            break;
                        case Move.Mouth: f.drink = true; break;
                    }
                    break;
                case Step.Ready:
                    f.ult = inStep > 1.5f;
                    break;
                case Step.Results:
                    if (inStep > 4) PinchAt(Find("vr.guide.again.btn"));
                    break;
            }
            return f;
        }
    }
}
