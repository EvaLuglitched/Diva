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
    /// 第 1 步 怎么玩（每次打开 app 第一局才有）：什么都不用做，看一遍 6 个小动画——看着按钮捏、推杆、左右推杆、看靶子捏、手放嘴边、双手举高，
    ///        每段 2.8 秒，右边一行大字说明；可以跳过。光给一张文字卡片新手看不懂。
    /// 第 2 步 选涂装：捏一下 = 下一个；看着按钮捏 = 按按钮（&lt; 上一个、就选这个、&gt; 下一个）。
    /// 第 2 步 学开大象（每次打开 app 第一局才有）：前倾走、左右倾转弯、看 + 捏 喷水、手放嘴边 吃喝，每个动作做一次就打勾，
    ///        牌子上有实时的反馈条；可以跳过。
    /// 第 3 步 出发：双手举过头顶举住（比赛里这就是大招），或者看着"出发"捏一下。
    /// 结算：双手举过头顶或者按"再来一局"。
    /// 按钮用 Vision Pro 的"看 + 捏"：捏下去那一刻眼睛看的方向（DivaVRFrame.look）打到牌子上的哪个按钮就按哪个。
    /// 选涂装和结算时人的手势只给这块牌子用（DivaVRInput 不交给游戏），比赛里的提示由 DivaVRCoach 负责。
    /// </summary>
    public class DivaVRGuide : MonoBehaviour
    {
        enum Step { Hidden, Watch, Pick, Learn, Ready, Results, Pause }
        enum Move { Lean, Turn, Pinch, Mouth }

        /// <summary>这次打开 app 已经学过开大象了（之后几局选完涂装直接到"出发"）；watched：演示已经看过一遍。</summary>
        static bool learned, watched;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => learned = watched = false;

        // 第 0 步的演示：6 段，每段 2.8 秒
        const float ClipSeconds = 3.6f; const int Clips = 6;
        int clip; float clipAt; bool roundHadWatch;
        readonly Image[] dotsWatch = new Image[Clips];
        static string ClipKey(int c) => "vr.watch." + (c + 1);
        // 演示用的道具：两只幽灵手、视线、全息靶子、泡泡；demoHover = 演示里"看着"的按钮
        DivaVRGhostHand ghostR, ghostL; Transform gazeRay, holoTarget, demoBubble, stickArrow, demoStick, demoStickRod; Material holoMat; DivaVRLever lever;
        DivaVRCockpit cockpit; DivaVRHologram holo; readonly Transform[] jet = new Transform[8];
        // 可爱的小反应：吃东西冒的小爱心、打中靶子爆的星星、泡泡周围的闪光、推杆时的速度线
        readonly Transform[] hearts = new Transform[5], stars = new Transform[10], sparks = new Transform[8], speedLines = new Transform[4];
        float starAt = -10; Vector3 starFrom;
        Btn demoHover; bool clipFired;

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
            // 第 0 步的 6 个进度点（牌子下方，按钮那排的上面）
            for (int i = 0; i < Clips; i++)
            {
                dotsWatch[i] = DivaUi.Image(panel, "Watch Dot " + i, new Vector2(.5f, 0), new Vector2(-90 + i * 36, 118), new Vector2(18, 18), new Color(1, 1, 1, .3f), null);
                dotsWatch[i].sprite = circle; dotsWatch[i].gameObject.SetActive(false);
            }
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
            lever = GetComponent<DivaVRLever>();
            if (glow) BuildWatchProps(glow);
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
            if (next == Step.Learn) foreach (var r in rows) { r.doneAt = -1; r.progress = 0; }
            switch (next)
            {
                case Step.Watch:
                    roundHadWatch = true; roundSteps = 4; clip = 0; clipAt = Time.unscaledTime;
                    AddButton("vr.guide.skip", 470, 170, false, () => { watched = true; Go(Step.Pick); }, 420, .8f);
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
                    AddButton("lang", 0, 300, false, () => { DivaText.Toggle(); Refresh(); }, 92, .8f);
                    break;
            }
            Fill();
            UpdateDemo();
            if (next != Step.Hidden && show.Sound != null) show.Sound.Play("tick", .4f);
            Debug.Log("DIVA_VR guide " + next);
        }

        void HideStamp() { if (step != Step.Pick) stamp.gameObject.SetActive(false); }

        // ------------------------------------------------------------------ 第 1 步：幽灵手演示
        void BuildWatchProps(Material glow)
        {
            ghostR = DivaVRGhostHand.Create(transform, false, glow);
            ghostL = DivaVRGhostHand.Create(transform, true, glow);
            Transform Prim(PrimitiveType t, string name, Transform parent, Vector3 scale, Material m)
            {
                var go = GameObject.CreatePrimitive(t); var c = go.GetComponent<Collider>(); if (c) Destroy(c);
                go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = scale;
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                return go.transform;
            }
            var rayMat = new Material(glow); var rc = new Color(.5f, .95f, 1f) * 1.6f; rayMat.color = rc; rayMat.SetColor("_BaseColor", rc);
            gazeRay = Prim(PrimitiveType.Cylinder, "Demo Gaze", transform, new Vector3(.004f, .1f, .004f), rayMat);
            // 全息靶子：三个同心圆盘（红白红），正对眼睛
            holoTarget = new GameObject("Demo Target").transform; holoTarget.SetParent(transform, false);
            holoMat = new Material(glow); var white = new Material(glow); white.color = Color.white * 1.3f; white.SetColor("_BaseColor", Color.white * 1.3f);
            Prim(PrimitiveType.Cylinder, "Ring", holoTarget, new Vector3(.4f, .006f, .4f), holoMat);
            Prim(PrimitiveType.Cylinder, "Mid", holoTarget, new Vector3(.26f, .007f, .26f), white).localPosition = new Vector3(0, .004f, 0);
            Prim(PrimitiveType.Cylinder, "Bull", holoTarget, new Vector3(.11f, .008f, .11f), holoMat).localPosition = new Vector3(0, .008f, 0);
            var bubble = Resources.Load<Material>("DivaVR Bubble");
            demoBubble = Prim(PrimitiveType.Sphere, "Demo Bubble", transform, Vector3.one * .1f, bubble ? bubble : glow);
            // 水柱：一串小水珠沿弧线飞向靶子
            var waterMat = new Material(glow); var wc = new Color(.6f, .9f, 1f) * 1.6f; waterMat.color = wc; waterMat.SetColor("_BaseColor", wc);
            for (int i = 0; i < jet.Length; i++) jet[i] = Prim(PrimitiveType.Sphere, "Jet " + i, transform, Vector3.one * .045f, waterMat);
            // 指向操纵杆的箭头（牌子左下角伸向右膝旁的杆）
            stickArrow = new GameObject("Stick Arrow").transform; stickArrow.SetParent(transform, false);
            Prim(PrimitiveType.Cylinder, "Shaft", stickArrow, new Vector3(.012f, .11f, .012f), rayMat).localPosition = new Vector3(0, .11f, 0);
            Prim(PrimitiveType.Sphere, "Head", stickArrow, Vector3.one * .05f, rayMat).localPosition = Vector3.zero;
            // 示范杆：和真杆一个样子，立在眼前（真杆在膝旁、看牌子时不在视野里），幽灵手握着它推
            var body = Resources.Load<Material>("DivaVR Cockpit Body");
            var rodMat = body ? new Material(body) : new Material(glow); rodMat.color = new Color(.12f, .1f, .18f); if (rodMat.HasProperty("_BaseColor")) rodMat.SetColor("_BaseColor", new Color(.12f, .1f, .18f));
            demoStick = new GameObject("Demo Stick").transform; demoStick.SetParent(transform, false);
            Prim(PrimitiveType.Cylinder, "Base", demoStick, new Vector3(.07f, .006f, .07f), rayMat);
            demoStickRod = Prim(PrimitiveType.Cylinder, "Rod", demoStick, new Vector3(.016f, .05f, .016f), rodMat); demoStickRod.localPosition = new Vector3(0, .05f, 0);
            Prim(PrimitiveType.Sphere, "Knob", demoStickRod, new Vector3(1.9f, .6f, 1.9f), rayMat).localPosition = new Vector3(0, 1, 0);
            Material Glow(Color c) { var m = new Material(glow); m.color = c * 1.6f; m.SetColor("_BaseColor", c * 1.6f); return m; }
            var pink = Glow(new Color(1f, .5f, .75f)); var yellow = Glow(new Color(1f, .9f, .4f)); var whiteM = Glow(Color.white); var cyan = Glow(new Color(.5f, .95f, 1f));
            for (int i = 0; i < hearts.Length; i++)
            {
                // 小爱心：两个球 + 一个转 45° 的方块
                var h = new GameObject("Heart " + i).transform; h.SetParent(transform, false);
                Prim(PrimitiveType.Sphere, "L", h, Vector3.one * .024f, pink).localPosition = new Vector3(-.0105f, .008f, 0);
                Prim(PrimitiveType.Sphere, "R", h, Vector3.one * .024f, pink).localPosition = new Vector3(.0105f, .008f, 0);
                var cube = Prim(PrimitiveType.Cube, "V", h, Vector3.one * .021f, pink); cube.localPosition = new Vector3(0, -.006f, 0); cube.localRotation = Quaternion.Euler(0, 0, 45);
                hearts[i] = h;
            }
            for (int i = 0; i < stars.Length; i++) stars[i] = Prim(PrimitiveType.Sphere, "Star " + i, transform, Vector3.one * .04f, yellow);
            for (int i = 0; i < sparks.Length; i++) sparks[i] = Prim(PrimitiveType.Sphere, "Spark " + i, transform, Vector3.one * .02f, whiteM);
            for (int i = 0; i < speedLines.Length; i++) speedLines[i] = Prim(PrimitiveType.Cylinder, "Speed " + i, transform, new Vector3(.006f, .05f, .006f), cyan);
            cockpit = GetComponent<DivaVRCockpit>(); holo = GetComponent<DivaVRHologram>();
            ResetDemo();
        }

        void ResetDemo()
        {
            if (ghostR) { ghostR.Show(false); ghostR.Curl = ghostR.Pinch = 0; ghostR.SetColour(DivaVRGhostHand.Idle); }
            if (ghostL) { ghostL.Show(false); ghostL.Curl = ghostL.Pinch = 0; ghostL.SetColour(DivaVRGhostHand.Idle); }
            if (gazeRay) gazeRay.gameObject.SetActive(false);
            if (holoTarget) holoTarget.gameObject.SetActive(false);
            if (demoBubble) demoBubble.gameObject.SetActive(false);
            if (lever) { lever.DemoOffset = Vector3.zero; lever.Highlight = false; }
            if (stickArrow) stickArrow.gameObject.SetActive(false);
            if (demoStick) demoStick.gameObject.SetActive(false);
            foreach (var x in hearts) if (x) x.gameObject.SetActive(false);
            foreach (var x in stars) if (x) x.gameObject.SetActive(false);
            foreach (var x in sparks) if (x) x.gameObject.SetActive(false);
            foreach (var x in speedLines) if (x) x.gameObject.SetActive(false);
            starAt = -10;
            foreach (var j in jet) if (j) j.gameObject.SetActive(false);
            if (cockpit) { cockpit.DemoSpeed = cockpit.DemoCharge = cockpit.DemoWater = null; }
            if (holo) holo.DemoYaw = 0;
            demoHover = null; clipFired = false;
        }

        void Gaze(Vector3 to)
        {
            var from = new Vector3(0, rig.eyeHeight, 0); var d = to - from; var dir = d.normalized;
            var a = from + dir * .2f; var b = to - dir * .05f;
            gazeRay.gameObject.SetActive(true);
            gazeRay.localPosition = (a + b) * .5f; gazeRay.localRotation = Quaternion.FromToRotation(Vector3.up, b - a);
            gazeRay.localScale = new Vector3(.004f, Vector3.Distance(a, b) * .5f, .004f);
        }

        /// <summary>每帧：第 1 步的第 clip 段演示进行到 t 秒。手从腿上出发 0.9 秒到位，之后一来一回做动作。</summary>
        void AnimateClip(float t)
        {
            if (!ghostR) return;
            float eye = rig.eyeHeight, now = Time.unscaledTime;
            Vector3 lapR = new Vector3(.24f, eye - .42f, .22f), lapL = new Vector3(-.24f, eye - .42f, .22f);
            Vector3 front = new Vector3(.12f, eye - .22f, .55f);
            Vector3 stickBase = new Vector3(.02f, eye - .26f, .6f), knob = stickBase + new Vector3(0, .125f, 0);
            Vector3 realKnob = (lever ? lever.Rest : new Vector3(.2f, eye - .4f, .34f)) + new Vector3(0, .025f, 0);
            Vector3 mouth = new Vector3(.03f, eye - .13f, .18f);
            Vector3 ultR = new Vector3(.17f, eye - .12f, .55f), ultL = new Vector3(-.17f, eye - .12f, .55f);
            float move = Mathf.SmoothStep(0, 1, t / .9f);
            float act = Mathf.Clamp01((t - 1.0f) / 1.5f);
            float pulse = Mathf.SmoothStep(0, 1, Mathf.PingPong(act * 2f, 1f));
            ghostR.Show(true); ghostL.Show(clip == 5);
            var pos = lapR; var rot = Quaternion.identity;
            switch (clip)
            {
                case 0:   // 看着按钮，捏一下：视线连到 SKIP，按钮高亮，捏到底时按钮按下去一下
                {
                    pos = Vector3.Lerp(lapR, front, move);
                    // 刚出来先挥挥手指打个招呼，再捏
                    ghostR.Curl = t < 1.2f ? .25f + .25f * Mathf.Sin(t * 14) : 0;
                    ghostR.Pinch = t > 1.3f ? Mathf.SmoothStep(0, 1, Mathf.PingPong((t - 1.3f) / .7f, 1f)) : 0; ghostR.SetColour(ghostR.Pinch > .6f ? DivaVRGhostHand.PinchC : DivaVRGhostHand.Idle);
                    pulse = ghostR.Pinch;
                    var skip = buttons.Find(b => b.key == "vr.guide.skip");
                    if (skip != null && t > .6f) { demoHover = skip; Gaze(transform.InverseTransformPoint(ButtonWorld(skip))); }
                    if (skip != null && pulse > .9f && !clipFired) { clipFired = true; skip.pressedAt = now; if (show.Sound != null) show.Sound.Play("tick", .4f); }
                    if (pulse < .5f) clipFired = false;
                    break;
                }
                case 1:   // 握住杆往前推：手走到杆头、握拳变青色，杆跟着往前倾；速度表指针上去、座舱震、全息大象弹
                case 2:   // 杆往左右推：全息大象跟着转头
                {
                    pos = Vector3.Lerp(lapR, knob, move);
                    ghostR.Curl = Mathf.Clamp01((t - .7f) / .4f); ghostR.SetColour(ghostR.Curl > .5f ? DivaVRGhostHand.Grip : DivaVRGhostHand.Idle);
                    float side = Mathf.Sin(act * Mathf.PI * 2);
                    var off = clip == 1 ? new Vector3(0, 0, .10f * pulse) : new Vector3(.08f * side, 0, 0);
                    pos += off;
                    // 眼前的示范杆跟着倾，膝旁的真杆也一起倾并且猛亮
                    demoStick.gameObject.SetActive(true); demoStick.localPosition = stickBase;
                    demoStickRod.localRotation = Quaternion.Euler(Mathf.Clamp(off.z / .12f, -1, 1) * 28, 0, -Mathf.Clamp(off.x / .10f, -1, 1) * 28);
                    if (lever) { lever.DemoOffset = off; lever.Highlight = true; }
                    // 示范杆旁一支箭头指向膝旁的真杆，一跳一跳的
                    var tail = stickBase + new Vector3(.12f, .02f, -.05f); var dir = (realKnob - tail).normalized;
                    stickArrow.gameObject.SetActive(true);
                    stickArrow.localPosition = tail + dir * (.25f + .05f * Mathf.Sin(now * 6)); stickArrow.localRotation = Quaternion.FromToRotation(Vector3.up, -dir);
                    if (cockpit)
                    {
                        cockpit.DemoSpeed = clip == 1 ? pulse : .35f;
                        if (clip == 1 && pulse > .8f) cockpit.Rumble(.25f);
                    }
                    // 推得越多，杆子两边往后飞的速度线越密
                    for (int i = 0; i < speedLines.Length; i++)
                    {
                        bool on = clip == 1 ? pulse > .3f : Mathf.Abs(side) > .3f;
                        speedLines[i].gameObject.SetActive(on);
                        if (!on) continue;
                        float u = Mathf.Repeat(now * (1.5f + 2f * pulse) + i * .25f, 1f);
                        var sp = stickBase + new Vector3(i % 2 == 0 ? -.16f : .16f, .04f + .03f * (i / 2), .3f - u * .6f);
                        speedLines[i].localPosition = sp; speedLines[i].localRotation = Quaternion.Euler(90, 0, 0); speedLines[i].localScale = new Vector3(.006f, .05f * (1 - Mathf.Abs(u - .5f) * 2), .006f);
                    }
                    if (holo)
                    {
                        holo.DemoYaw = clip == 2 ? -side * 40f : 0;
                        if (clip == 1 && pulse > .9f && !clipFired) { clipFired = true; holo.Bump(); if (show.Sound != null) show.Sound.Play("tick", .3f, 1.3f); }
                        if (pulse < .5f) clipFired = false;
                    }
                    break;
                }
                case 3:   // 看着靶子捏：全息靶子在牌子左边远处，视线连过去，捏住时象鼻喷出水柱飞过去，打中靶子闪白、飘 +10、水量表掉一点
                {
                    var tp = new Vector3(-.9f, eye + .05f, 1.6f);
                    holoTarget.gameObject.SetActive(true); holoTarget.localPosition = tp;
                    holoTarget.localRotation = Quaternion.LookRotation(tp - new Vector3(0, eye, 0)) * Quaternion.Euler(90, 0, 0);
                    pos = Vector3.Lerp(lapR, front, move);
                    ghostR.Pinch = t > 1 ? pulse : 0; ghostR.SetColour(ghostR.Pinch > .6f ? DivaVRGhostHand.PinchC : DivaVRGhostHand.Idle);
                    if (t > .6f) Gaze(tp);
                    // 水柱：捏住期间从象鼻（牌子下方）沿弧线飞到靶子，8 颗水珠错开
                    bool spraying = ghostR.Pinch > .6f;
                    var from = new Vector3(.05f, eye - .6f, 1.0f); var mid = (from + tp) * .5f + Vector3.up * .5f;
                    float flight = 0; 
                    for (int i = 0; i < jet.Length; i++)
                    {
                        float u = Mathf.Repeat(t * 1.6f - i * .09f, 1f);
                        bool on = spraying && u > .02f;
                        jet[i].gameObject.SetActive(on);
                        if (on) { var p = Vector3.Lerp(Vector3.Lerp(from, mid, u), Vector3.Lerp(mid, tp, u), u); jet[i].localPosition = p; flight = Mathf.Max(flight, u); }
                    }
                    bool hit = spraying && flight > .95f;
                    if (hit && !clipFired) { clipFired = true; starAt = now; starFrom = tp; DivaVRFloatText.Show("+10", transform.TransformPoint(tp + Vector3.up * .3f), new Color(1f, .85f, .3f), .9f); if (show.Sound != null) show.Sound.Play("skill", .35f); }
                    // 打中：一圈星星从靶子炸开
                    for (int i = 0; i < stars.Length; i++)
                    {
                        float age = now - starAt; bool on = age < .6f;
                        stars[i].gameObject.SetActive(on);
                        if (!on) continue;
                        var dir = Quaternion.AngleAxis(i * 36f, Vector3.forward) * Vector3.up;
                        stars[i].localPosition = starFrom + holoTarget.localRotation * Quaternion.Euler(-90, 0, 0) * dir * (.1f + age * 1.1f);
                        stars[i].localScale = Vector3.one * .045f * (1 - age / .6f);
                    }
                    if (!spraying) clipFired = false;
                    var tc = Color.Lerp(new Color(1f, .35f, .35f), Color.white, hit ? 1 : 0) * 1.5f; holoMat.color = tc; holoMat.SetColor("_BaseColor", tc);
                    if (cockpit) cockpit.DemoWater = spraying ? Mathf.Max(.4f, 1f - act * .5f) : null;
                    break;
                }
                case 4:   // 手放到嘴边：手从前面抬到嘴边，半握，到位变蓝；水量表从 1/4 慢慢回满
                    pos = Vector3.Lerp(lapR, mouth, move); rot = Quaternion.Euler(30, 180, 0);   // 手指朝嘴、手背朝外：掌心对着眼睛会弹出系统的 Home 按钮
                    ghostR.Curl = .45f; ghostR.SetColour(move > .9f ? DivaVRGhostHand.Drink : DivaVRGhostHand.Idle);
                    if (cockpit) cockpit.DemoWater = Mathf.Lerp(.25f, 1f, act);
                    // 到嘴边以后冒小爱心，一个个往上飘
                    for (int i = 0; i < hearts.Length; i++)
                    {
                        bool on = move > .95f;
                        hearts[i].gameObject.SetActive(on);
                        if (!on) continue;
                        float u = Mathf.Repeat((t - .9f) * .7f + i * .2f, 1f);
                        hearts[i].localPosition = mouth + new Vector3(Mathf.Sin(u * 7 + i) * .06f + .04f, u * .2f, .06f + u * .08f);
                        hearts[i].localRotation = Quaternion.Euler(0, Mathf.Sin(now * 3 + i) * 25, 0);
                        hearts[i].localScale = Vector3.one * (.55f * Mathf.Sin(u * Mathf.PI));
                    }
                    break;
                case 5:   // 双手举到眼前举住：两只手到位变粉，1.6 秒后眼前鼓出一个泡泡
                {
                    pos = Vector3.Lerp(lapR, ultR, move); rot = Quaternion.Euler(-90, 0, 0);
                    ghostL.Place(Vector3.Lerp(lapL, ultL, move), rot);
                    var c = move > .9f ? DivaVRGhostHand.Ult : DivaVRGhostHand.Idle; ghostR.SetColour(c); ghostL.SetColour(c);
                    if (cockpit) cockpit.DemoCharge = Mathf.Clamp01(t / 1.4f);
                    bool burst = t > 1.6f && t < 3.2f;
                    demoBubble.gameObject.SetActive(burst);
                    var bc = new Vector3(0, eye - .18f, 1.0f); float bs = .1f + .4f * Mathf.Clamp01((t - 1.6f) / .8f);
                    if (burst) { demoBubble.localPosition = bc; demoBubble.localScale = Vector3.one * bs; }
                    for (int i = 0; i < sparks.Length; i++)
                    {
                        sparks[i].gameObject.SetActive(burst);
                        if (!burst) continue;
                        float a = now * 3f + i * Mathf.PI / 4;
                        sparks[i].localPosition = bc + new Vector3(Mathf.Cos(a), Mathf.Sin(a * 1.3f) * .6f, Mathf.Sin(a)) * (bs * .6f);
                        sparks[i].localScale = Vector3.one * (.012f + .012f * Mathf.Abs(Mathf.Sin(now * 9 + i)));
                    }
                    break;
                }
            }
            ghostR.Place(pos, rot);
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
            // 第 1 步不是一块牌子而是顶上一条字幕：底板缩成 230 高贴在面板顶部，中间整个视野留给发光的手和它做的事
            bool watch = step == Step.Watch;
            foreach (var d in dotsWatch) d.gameObject.SetActive(watch);
            plate.rectTransform.sizeDelta = new Vector2(1160, watch ? 230 : 540);
            plate.rectTransform.anchoredPosition = new Vector2(0, watch ? 155 : 0);
            plate.SetVerticesDirty();
            title.gameObject.SetActive(!watch);
            stepLabel.rectTransform.anchoredPosition = new Vector2(watch ? -80 : 0, -38); stepLabel.rectTransform.sizeDelta = new Vector2(watch ? 800 : 1100, 40);
            for (int i = 0; i < Clips; i++) dotsWatch[i].rectTransform.anchoredPosition = new Vector2(-90 + i * 36, 352);
            lineA.rectTransform.sizeDelta = new Vector2(watch ? 760 : 1100, watch ? 100 : 64); lineA.fontSize = watch ? 42 : 46;
            lineA.textWrappingMode = watch ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            lineB.rectTransform.sizeDelta = new Vector2(watch ? 760 : 1100, 44);
            bool pause = step == Step.Pause;   // 暂停板两排按钮，两行字往上挪
            lineA.rectTransform.anchoredPosition = new Vector2(watch ? -80 : 0, compact ? 30 : watch ? 178 : pause ? 110 : 56);
            lineB.rectTransform.anchoredPosition = new Vector2(watch ? -80 : 0, compact ? -40 : watch ? 118 : pause ? 62 : 8);
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
                    for (int i = 0; i < Clips; i++) dotsWatch[i].color = i < clip ? new Color(1f, .45f, .8f) : i == clip ? Color.white : new Color(1, 1, 1, .3f);
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
            bool pinch = f.shoot && !lastShoot;
            lastShoot = f.shoot;
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
            // 第 0 步的小人站在牌子前面 35 厘米（牌子不透明，演示里的靶子 / 按钮在小人前方，再近就埋进牌子里）
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
                bool hov = b == hovered || b == demoHover;
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
