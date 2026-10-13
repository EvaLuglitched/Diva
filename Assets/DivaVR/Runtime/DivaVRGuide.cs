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
    /// 第 1 步 怎么玩（每次打开 app 第一局才有）：什么都不用做，看一排 Eva 风格的火柴人（DivaVRStickFigure）轮流演示五个动作——
    ///        身体前倾、歪头、双手前推、竖大拇指、双手举到眼前，每个 2.8 秒，下面一行大字说明；可以跳过。光给一张文字卡片新手看不懂。
    /// 第 2 步 选涂装：捏一下 = 下一个；看着按钮捏 = 按按钮（&lt; 上一个、就选这个、&gt; 下一个）。
    /// 第 2 步 学开大象（每次打开 app 第一局才有）：前倾走、歪头转弯、双手前推喷水、竖拇指吃喝，每个动作做一次就打勾，
    ///        牌子上有实时的反馈条；可以跳过。
    /// 第 3 步 出发：双手举过头顶举住（比赛里这就是大招），或者看着"出发"捏一下。
    /// 结算：双手举过头顶或者按"再来一局"。
    /// 按钮用 Vision Pro 的"看 + 捏"：捏下去那一刻眼睛看的方向（DivaVRFrame.look）打到牌子上的哪个按钮就按哪个。
    /// 选涂装和结算时人的手势只给这块牌子用（DivaVRInput 不交给游戏），比赛里的提示由 DivaVRCoach 负责。
    /// </summary>
    public class DivaVRGuide : MonoBehaviour
    {
        enum Step { Hidden, Watch, Pick, Learn, Ready, Results, Pause }
        enum Move { Lean, Turn, Shoot, Drink }

        /// <summary>这次打开 app 已经学过开大象了（之后几局选完涂装直接到"出发"）；watched：演示已经看过一遍。</summary>
        static bool learned, watched;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => learned = watched = false;

        // 第 1 步的演示：5 个火柴人，每个 2.8 秒轮到一次
        const float ClipSeconds = 2.8f; const int Clips = 5;
        int clip; float clipAt; bool roundHadWatch;
        static string ClipKey(int c) => "vr.watch." + (c + 1);
        readonly DivaVRStickFigure[] figures = new DivaVRStickFigure[Clips];
        readonly TextMeshProUGUI[] captions = new TextMeshProUGUI[Clips];
        GameObject figuresRoot;
        DivaVRCockpit cockpit; DivaVRHologram holo; DivaVRHands hands;

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
        bool lastPinch;
        ShowPhase lastPhase = (ShowPhase)(-1);

        /// <summary>现在要学的动作（没打勾的第一个）。</summary>
        Move Current { get { for (int i = 0; i < rows.Length; i++) if (rows[i].doneAt < 0) return (Move)i; return Move.Drink; } }
        bool AllDone { get { foreach (var r in rows) if (r.doneAt < 0) return false; return true; } }

        /// <summary>"双手举高"现在能出发 / 再来一局吗（只在第 3 步和结算，免得学动作时不小心开始）。</summary>
        public bool AllowStart => step == Step.Ready || step == Step.Results;
        /// <summary>这一阶段人的手势归这块牌子管（选涂装、结算、暂停中）。</summary>
        public bool Handles(ShowPhase p) => p == ShowPhase.Select || p == ShowPhase.Results || step == Step.Pause;
        /// <summary>头正对着仪表台上的 PAUSE 按钮（这时捏一下 = 暂停，不开炮）。</summary>
        public bool PauseHover { get; private set; }
        public bool Paused => step == Step.Pause;

        // 仪表台上的 PAUSE 小牌（比赛里看着它捏一下）
        RectTransform pausePlate; DivaSlant pauseBg; TextMeshProUGUI pauseText; float pauseHoverSince = -1, quitArmedAt = -10;

        void BuildPausePlate()
        {
            var anchor = DivaVR.DashAnchor ? DivaVR.DashAnchor : transform;
            var go = new GameObject("Pause Button", typeof(RectTransform), typeof(Canvas)) { layer = 5 };
            pausePlate = (RectTransform)go.transform; pausePlate.SetParent(anchor, false);
            pausePlate.sizeDelta = new Vector2(320, 120); pausePlate.localScale = Vector3.one * (.13f / 320);
            pausePlate.localPosition = new Vector3(.34f, .12f, 0); pausePlate.localRotation = Quaternion.identity;
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.worldCamera = cam; c.sortingOrder = 60;
            pauseBg = DivaUi.Panel(pausePlate, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(320, 120), new Color(.06f, .05f, .12f), new Color(.26f, .22f, .4f), 18);
            pauseBg.stripe = new Color(.5f, .95f, 1f); pauseBg.stripeWidth = 10;
            pauseText = DivaUi.Text(pausePlate, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 100), 56, Color.white);
            pausePlate.gameObject.SetActive(false);
        }

        bool HitPausePlate(Vector3 origin, Vector3 dir)
        {
            if (!pausePlate || !pausePlate.gameObject.activeSelf) return false;
            var plane = new Plane(pausePlate.forward, pausePlate.position);
            var ray = new Ray(origin, dir.normalized);
            if (!plane.Raycast(ray, out float d) || d > 1.5f) return false;
            var local = (Vector2)pausePlate.InverseTransformPoint(ray.GetPoint(d));
            return Mathf.Abs(local.x) < 200 && Mathf.Abs(local.y) < 90;   // 比牌子本身大一圈，好对准
        }

        /// <summary>打开暂停板（世界冻住）。</summary>
        public void OpenPause() { if (step != Step.Hidden || show.Phase != ShowPhase.Play && show.Phase != ShowPhase.Ult) return; show.VrPause(true); Go(Step.Pause); }
        void ClosePause() { show.VrPause(false); Go(Step.Hidden); }

        /// <summary>按名字按一个按钮（自动驾驶 / 测试用）。</summary>
        public bool PressButton(string key) { var b = buttons.Find(x => x.key == key); if (b == null) return false; Press(b); return true; }

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            rig = GetComponent<DivaVRRig>();
            input = GetComponent<DivaVRInput>();
            simulator = GetComponent<DivaVRSimulator>();
            cam = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            if (!show || !rig) { enabled = false; return; }
            MakeSprites();
            BuildPausePlate();
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
            lineB = DivaUi.Text(panel, "Line B", new Vector2(.5f, .5f), new Vector2(0, -2), new Vector2(1100, 44), 30, new Color(.55f, 1f, .9f, .85f), TextAlignmentOptions.Center, false);

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
            // 第 1 步：一排五个火柴人（Eva 的画法），每个下面一个小字
            figuresRoot = new GameObject("Figures", typeof(RectTransform));
            var fr = (RectTransform)figuresRoot.transform; fr.SetParent(panel, false); fr.sizeDelta = new Vector2(1160, 330); fr.anchoredPosition = new Vector2(0, 20);
            for (int i = 0; i < Clips; i++)
            {
                float x = -456 + i * 228;
                var fgo = new GameObject("Figure " + i, typeof(RectTransform), typeof(CanvasRenderer), typeof(DivaVRStickFigure)) { layer = 5 };
                var frt = (RectTransform)fgo.transform; frt.SetParent(fr, false); frt.anchoredPosition = new Vector2(x, 30); frt.sizeDelta = new Vector2(200, 250);
                figures[i] = fgo.GetComponent<DivaVRStickFigure>(); figures[i].act = (DivaVRStickFigure.Act)i; figures[i].color = Color.white; figures[i].raycastTarget = false;
                captions[i] = DivaUi.Text(fr, "Caption " + i, new Vector2(.5f, .5f), new Vector2(x, -125), new Vector2(220, 40), 26, Color.white);
            }
            figuresRoot.SetActive(false);
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
                var lbl = DivaUi.Text(sr, "Stat " + i, new Vector2(.5f, .5f), new Vector2(x, 16), new Vector2(300, 36), 24, new Color(.55f, 1f, .9f, .6f), TextAlignmentOptions.Center, false);
                int k = i;   // for 的循环变量是同一个，闭包里直接用 i 会在切换语言时越界（statKeys[3]）
                DivaUi.Localize(lbl, () => DivaText.T(statKeys[k]));
                var back = DivaUi.Image(sr, "Bar " + i, new Vector2(.5f, .5f), new Vector2(x, -12), new Vector2(240, 10), new Color(1, 1, 1, .1f));
                var fillImg = DivaUi.Image(back.rectTransform, "Fill", new Vector2(0, .5f), Vector2.zero, new Vector2(240, 10), new Color(1f, .45f, .8f, .65f));
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
            cockpit = GetComponent<DivaVRCockpit>(); holo = GetComponent<DivaVRHologram>(); hands = GetComponent<DivaVRHands>();
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

        /// <summary>视觉层级：一块板只有一个主按钮（粉、大）；次要动作用深色小按钮（scale 0.8）。</summary>
        void AddButton(string key, float x, float width, bool primary, Action press, float y = 80, float scale = 1f)
        {
            var b = new Btn { key = key, primary = primary, press = press };
            float h = 104 * scale;
            b.rt = DivaUi.Rect(panel, "Button " + key, new Vector2(.5f, 0), new Vector2(x, y), new Vector2(width, h));
            b.baseTop = primary ? new Color(1f, .38f, .74f) : new Color(.26f, .22f, .4f);
            b.baseBottom = primary ? new Color(.82f, .22f, .6f) : new Color(.17f, .14f, .28f);
            b.plate = DivaUi.Panel(b.rt, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(width, h), b.baseTop, b.baseBottom, 18);
            b.label = DivaUi.Text(b.rt, "Label", new Vector2(.5f, .5f), new Vector2(0, 2), new Vector2(width - 20, 90 * scale), (primary ? 44 : 40) * scale, Color.white);
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
            ResetDemo();
            // 演示时把选涂装的横幅和全息底座藏起来，视野里只有发光的手和它做的事
            if (holo) holo.Hidden = next == Step.Watch;
            show.VrSelectHidden = next == Step.Watch;
            allDoneAt = -1;
            ClearButtons();
            if (next == Step.Learn) { foreach (var r in rows) { r.doneAt = -1; r.progress = 0; } if (hands) hands.Recenter(); }   // 学前倾：从现在的坐姿算
            switch (next)
            {
                case Step.Watch:
                    roundHadWatch = true; roundSteps = 4; clip = 0; clipAt = Time.unscaledTime;
                    foreach (var fg in figures) if (fg) fg.startTime = Time.unscaledTime;
                    AddButton("vr.guide.skip", 470, 170, false, () => { watched = true; Go(Step.Pick); }, 30, .8f);
                    break;
                case Step.Pick:
                    roundSteps = roundHadWatch ? 4 : learned ? 2 : 3;
                    AddButton("vr.guide.prev", -410, 150, false, () => show.VrStepSkin(-1));
                    AddButton("vr.guide.choose", 0, 540, true, () => { stampAt = Time.unscaledTime; stamp.text = DivaText.T("vr.guide.locked"); stamp.gameObject.SetActive(true); if (show.Sound != null) show.Sound.Play("powerup", .5f, 1.3f); Invoke(nameof(AfterStamp), .9f); });
                    AddButton("vr.guide.next", 410, 150, false, () => show.VrStepSkin(1));
                    break;
                case Step.Learn:
                    AddButton("vr.guide.skip", 420, 220, false, () => { learned = true; Go(Step.Ready); }, 80, .8f);
                    break;
                case Step.Ready:
                    AddButton("vr.guide.back", -380, 280, false, () => Go(Step.Pick), 80, .8f);
                    AddButton("vr.guide.start", 110, 520, true, () => show.VrConfirm());
                    break;
                case Step.Results:
                    AddButton("vr.guide.replay", -370, 360, false, () => { watched = learned = false; show.VrConfirm(); }, 70, .8f);
                    AddButton("vr.guide.again.btn", 150, 520, true, () => show.VrConfirm(), 70);
                    break;
                case Step.Pause:
                    // 主按钮"继续"居中在上；下面一排小的：重看教程 / EN 中文 / 回选涂装（两步确认）
                    AddButton("vr.pause.continue", 0, 560, true, ClosePause, 205);
                    AddButton("vr.pause.quit", 370, 400, false, () =>
                    {
                        var b = buttons.Find(x => x.key == "vr.pause.quit" || x.key == "vr.pause.quit2");
                        if (b != null && b.key == "vr.pause.quit2") { show.VrQuitToSelect(); return; }
                        if (b != null) { b.key = "vr.pause.quit2"; b.label.text = DivaText.T(b.key); quitArmedAt = Time.unscaledTime; }
                    }, 92, .8f);
                    AddButton("vr.pause.replay", -370, 400, false, () => { watched = learned = false; show.VrQuitToSelect(); }, 92, .8f);
                    AddButton("lang", -60, 220, false, () => { DivaText.Toggle(); Refresh(); }, 92, .8f);
                    // 坐姿变了（往前挪了椅子、换了姿势）：重新记一次"坐好"的位置
                    AddButton("vr.pause.recenter", 170, 220, false, () => { if (hands) hands.Recenter(); if (show.Sound != null) show.Sound.Play("confirm", .5f); }, 92, .8f);
                    break;
            }
            Fill();
            UpdateDemo();
            if (next != Step.Hidden && show.Sound != null) show.Sound.Play("tick", .4f);
            Debug.Log("DIVA_VR guide " + next);
        }

        void HideStamp() { if (step != Step.Pick) stamp.gameObject.SetActive(false); }

        // ------------------------------------------------------------------ 第 1 步：火柴人演示
        void ResetDemo()
        {
            if (cockpit) { cockpit.DemoSpeed = cockpit.DemoCharge = cockpit.DemoWater = null; }
            if (holo) holo.DemoYaw = 0;
        }

        /// <summary>每帧：第 clip 个火柴人演示中，其它的淡；仪表跟着做出反应（前倾 → 速度表，推手 → 水量掉，举手 → 能量条）。</summary>
        void AnimateClip(float t)
        {
            for (int i = 0; i < Clips; i++)
            {
                if (!figures[i]) continue;
                figures[i].emphasis = Mathf.MoveTowards(figures[i].emphasis, i == clip ? 1 : 0, Time.unscaledDeltaTime * 4);
                figures[i].rectTransform.localScale = Vector3.one * (i == clip ? 1.12f : 1f);
                if (captions[i]) { captions[i].text = DivaText.T(figures[i].CaptionKey); captions[i].color = i == clip ? new Color(1f, .9f, .4f) : new Color(1, 1, 1, .45f); }
            }
            float act = Mathf.Clamp01((t - .4f) / 1.2f), pulse = Mathf.SmoothStep(0, 1, Mathf.PingPong(act * 2f, 1f));
            if (cockpit)
            {
                cockpit.DemoSpeed = clip == 0 ? pulse : null;
                cockpit.DemoWater = clip == 2 ? Mathf.Max(.4f, 1f - act * .5f) : clip == 3 ? Mathf.Lerp(.25f, 1f, act) : null;
                cockpit.DemoCharge = clip == 4 ? Mathf.Clamp01(t / 1.6f) : null;
                if (clip == 0 && pulse > .8f) cockpit.Rumble(.15f);
            }
            if (holo) holo.DemoYaw = clip == 1 ? Mathf.Sin(t * 2f) * 35f : 0;
        }

        void AfterWatch() { stamp.gameObject.SetActive(false); if (step == Step.Watch) Go(Step.Pick); }
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
            // 第 1 步：整块牌子，中间一排火柴人，下面两行字说明当前这个动作
            bool watch = step == Step.Watch;
            figuresRoot.SetActive(watch);
            plate.rectTransform.sizeDelta = new Vector2(1160, 540);
            plate.rectTransform.anchoredPosition = Vector2.zero;
            plate.SetVerticesDirty();
            title.gameObject.SetActive(!watch);
            stepLabel.rectTransform.anchoredPosition = new Vector2(0, -38); stepLabel.rectTransform.sizeDelta = new Vector2(1100, 40);
            lineA.rectTransform.sizeDelta = new Vector2(1100, 64); lineA.fontSize = watch ? 40 : 46;
            lineA.textWrappingMode = TextWrappingModes.NoWrap;
            lineB.rectTransform.sizeDelta = new Vector2(1100, 44);
            bool pause = step == Step.Pause;   // 暂停板两排按钮，两行字往上挪
            lineA.rectTransform.anchoredPosition = new Vector2(0, compact ? 30 : watch ? -158 : pause ? 110 : 56);
            lineB.rectTransform.anchoredPosition = new Vector2(0, compact ? -40 : watch ? -204 : pause ? 62 : 8);
            lineA.gameObject.SetActive(step != Step.Learn);
            lineB.gameObject.SetActive(step != Step.Learn && step != Step.Results);   // 结算板矮，提示行放不下（英雄卡 + 名次一行就够）
            switch (step)
            {
                case Step.Watch:
                    stepLabel.text = DivaText.T("vr.watch") + "   ·   " + DivaText.F("vr.guide.step", 1, total) + "   ·   " + DivaText.T("vr.watch.sub");
                    if (clip < Clips)
                    {
                        lineA.text = $"<color=#8CFFE6>{clip + 1}/{Clips}</color>   {DivaText.T(ClipKey(clip))}";
                        lineB.text = DivaText.T(ClipKey(clip) + "b");
                    }
                    break;
                case Step.Pick:
                    stepLabel.text = DivaText.F("vr.guide.step", roundHadWatch ? 2 : 1, total);
                    title.text = DivaText.T("vr.guide.pick");
                    lineA.text = DivaText.T("vr.guide.pick.a");
                    lineB.text = DivaText.T("vr.guide.pick.b");
                    break;
                case Step.Learn:
                    stepLabel.text = DivaText.F("vr.guide.step", roundHadWatch ? 3 : 2, total);
                    title.text = DivaText.T("vr.guide.learn");
                    string[] keys = { "lean", "turn", "shoot", "drink" };
                    for (int i = 0; i < rows.Length; i++)
                        rows[i].text.text = $"{DivaText.T("vr.guide.learn." + keys[i])}\n<color=#8CFFE6><size=30>{DivaText.T("vr.guide.learn." + keys[i] + ".b")}</size></color>";
                    break;
                case Step.Ready:
                    stepLabel.text = DivaText.F("vr.guide.step", total, total);
                    title.text = DivaText.T("vr.guide.ready");
                    lineA.text = DivaText.T("vr.guide.ready.a");
                    lineB.text = DivaText.T("vr.guide.ready.b");
                    break;
                case Step.Pause:
                    stepLabel.text = "";
                    title.text = DivaText.T("vr.pause");
                    lineA.text = DivaText.T("vr.pause.a");
                    lineB.text = DivaText.T("vr.pause.b");
                    break;
                case Step.Results:
                    stepLabel.text = show.VrResultTeam;
                    title.text = DivaText.T("vr.guide.results");
                    lineA.text = show.VrResultLine;
                    lineB.text = DivaText.T("vr.guide.again") + "   ·   " + DivaText.T("vr.guide.again.b");
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
                if (phase == ShowPhase.Select && show.PhaseTime > .8f) { roundHadWatch = false; Go(watched ? Step.Pick : Step.Watch); }
                else if (phase == ShowPhase.Results && show.PhaseTime > 2.5f) Go(Step.Results);
            }
            var f = input ? input.Current : default;
            bool pinch = f.pinch && !lastPinch;
            lastPinch = f.pinch;
            // 仪表台 PAUSE：只在比赛里、牌子收起时露出来；头对着它 0.25 秒后才算悬停（扫一眼不算），捏一下暂停
            if (pausePlate)
            {
                bool showPause = (phase == ShowPhase.Play || phase == ShowPhase.Ult) && step == Step.Hidden && !show.VrPaused;
                if (pausePlate.gameObject.activeSelf != showPause) pausePlate.gameObject.SetActive(showPause);
                var ht = DivaVR.Head;
                bool over = showPause && ht && HitPausePlate(ht.position, ht.forward);
                if (over && pauseHoverSince < 0) pauseHoverSince = Time.unscaledTime; if (!over) pauseHoverSince = -1;
                PauseHover = over && Time.unscaledTime - pauseHoverSince > .25f;
                if (showPause)
                {
                    pauseText.text = DivaText.T("vr.pause.btn");
                    pausePlate.localScale = Vector3.one * (.13f / 320) * (PauseHover ? 1.15f : 1f);
                    pauseBg.Set(Color.Lerp(new Color(.06f, .05f, .12f), Color.white, PauseHover ? .35f : 0), Color.Lerp(new Color(.26f, .22f, .4f), Color.white, PauseHover ? .2f : 0));
                    if (pinch && PauseHover) { OpenPause(); if (show.Sound != null) show.Sound.Play("confirm", .6f); return; }
                }
            }
            if (step == Step.Pause && quitArmedAt > 0 && Time.unscaledTime - quitArmedAt > 4f)
            {
                // 4 秒没按第二下，"确定退出？"退回"回选涂装"
                quitArmedAt = -10; var b = buttons.Find(x => x.key == "vr.pause.quit2"); if (b != null) { b.key = "vr.pause.quit"; b.label.text = DivaText.T(b.key); }
            }
            if (step == Step.Hidden) return;
            // 第 0 步：演示一段一段自动往下走，放完盖个"该你试试了"的章再进选涂装
            if (step == Step.Watch && clip < Clips) AnimateClip(Time.unscaledTime - clipAt);
            if (step == Step.Watch && clip < Clips && Time.unscaledTime - clipAt > ClipSeconds)
            {
                clip++; clipAt = Time.unscaledTime; ResetDemo();
                if (clip >= Clips)
                {
                    watched = true; demo?.Hide();
                    stampAt = Time.unscaledTime; stamp.text = DivaText.T("vr.watch.done"); stamp.gameObject.SetActive(true);
                    if (show.Sound != null) show.Sound.Play("confirm", .6f);
                    Invoke(nameof(AfterWatch), 1.1f);
                }
                else { Fill(); UpdateDemo(); if (show.Sound != null) show.Sound.Play("tick", .35f); }
            }
            // 头朝向哪个按钮哪个就高亮；捏一下按下它（眼睛看的方向有的话优先）
            var headT = DivaVR.Head;
            hovered = headT ? HitButton(headT.position, headT.forward) : null;
            if (pinch && headT)
            {
                var head = headT;
                var b = (f.look.sqrMagnitude > .01f ? HitButton(head.position, f.look) : null) ?? hovered;
                if (b != null) Press(b);
                else if (step == Step.Pick) show.VrStepSkin(1);   // 捏一下 = 下一个涂装（不用先找按钮）
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
            // 第 0 步的小人站在牌子前面 35 厘米（牌子不透明，演示里的靶子 / 按钮在小人前方，再近就埋进牌子里）
            if (step == Step.Learn && !AllDone) { demo.Show(DemoMove(Current)); demo.transform.localScale = Vector3.one * 1.7f; demo.transform.localPosition = new Vector3(-.12f, rig.eyeHeight + .4f, 1.4f); }
            else if (step == Step.Ready) { demo.Show(DivaVRGestureDemo.Move.HandsUp); demo.transform.localScale = Vector3.one * 1.3f; demo.transform.localPosition = new Vector3(-.12f, rig.eyeHeight + .38f, 1.4f); }
            else demo.Hide();
        }

        static DivaVRGestureDemo.Move DemoMove(Move m) => m == Move.Lean ? DivaVRGestureDemo.Move.Lean : m == Move.Turn ? DivaVRGestureDemo.Move.Tilt
                                                        : m == Move.Shoot ? DivaVRGestureDemo.Move.Push : DivaVRGestureDemo.Move.ThumbsUp;

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
            Hold(Move.Shoot, f.shoot, .5f);
            Hold(Move.Drink, f.drink, .4f);
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
            var at = results ? new Vector3(.32f, rig.eyeHeight - .31f, 1.3f) : new Vector3(-.12f, rig.eyeHeight + .03f, 1.45f);
            panel.localPosition = group.alpha <= .01f ? at : Vector3.Lerp(panel.localPosition, at, 1 - Mathf.Exp(-dt * 6));
            panel.sizeDelta = new Vector2(Units, results ? 460 : 560);   // 结算板比教学板矮一点（别挡上面的英雄卡），但要放得下队名、标题、名次一行和两个按钮
            // 第 1 步的底板是顶上一条字幕（Fill 里定的），别在这里每帧改回去
            if (step != Step.Watch) plate.rectTransform.sizeDelta = new Vector2(1160, results ? 440 : 540);
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
                    // 实时反馈：前倾多少、往哪边歪、有没有推出手、有没有竖拇指
                    float live = i == (int)Move.Lean ? f.go : i == (int)Move.Turn ? f.steer : i == (int)Move.Drink ? (f.drink ? 1 : 0) : (f.shoot ? 1 : 0);
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
        /// 看着"&gt;"捏一下换涂装，看着"就选这个"捏，前倾、歪头、推手、竖拇指，最后举双手出发；结算时按"再来一局"。
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
                if (t - demoPressAt < .2f) { f.pinch = true; f.look = (ButtonWorld(b) - head.position).normalized; }
            }
            switch (step)
            {
                case Step.Watch:
                    break;   // 看着就行
                case Step.Pause:
                    if (inStep > 1.5f) PinchAt(Find("vr.pause.continue"));
                    break;
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
                        case Move.Shoot: f.shoot = true; break;
                        case Move.Drink: f.drink = true; break;
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
