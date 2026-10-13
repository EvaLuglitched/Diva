using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Diva.VR;

namespace Diva.Show
{
    // 界面搭建：1920×1080 参考分辨率，按屏幕缩放。平时画在最上层（不受后期的暗角和色调影响），
    // 后台截图时画进主摄像机（截图才拍得到）。
    public partial class DivaShowDirector
    {
        static readonly Color Ink = new Color(.06f, .05f, .12f, .78f), InkLight = new Color(.12f, .1f, .2f, .55f);
        Canvas canvas;
        /// <summary>Batch mode only: renders the UI on top of a capture (tests and screenshots).</summary>
        public Camera UiCaptureCamera { get; private set; }
        RectTransform topRoot, selectRoot, hudRoot, bannerRoot, countdownRoot, highlightRoot, resultsRoot;

        void BuildUi()
        {
            var go = new GameObject("Diva Show UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            // VR：界面是驾驶舱前方的一块空间面板（还是 1920×1080 的布局，按米缩放），跟着驾驶舱走，不跟着头飘
            if (DivaVR.Active && DivaVR.HudAnchor)
            {
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = mainCamera;
                var rt = (RectTransform)go.transform;
                rt.SetParent(DivaVR.HudAnchor, false);
                rt.sizeDelta = new Vector2(1920, 1080);
                rt.localPosition = Vector3.zero; rt.localRotation = Quaternion.identity;
                rt.localScale = Vector3.one * (DivaVR.HudWidth / 1920f);
            }
            // 后台模式没有屏幕，最上层界面拍不到：改用一个只拍界面的摄像机（不动、不加后期），测试截图时叠在主镜头上
            else if (Application.isBatchMode)
            {
                var camGo = new GameObject("Diva UI Capture Camera");
                camGo.transform.SetParent(transform, false);
                camGo.transform.position = new Vector3(0, -5000, 0);
                UiCaptureCamera = camGo.AddComponent<Camera>();
                UiCaptureCamera.clearFlags = CameraClearFlags.SolidColor;
                UiCaptureCamera.backgroundColor = Color.clear;
                UiCaptureCamera.cullingMask = 1 << 5;
                UiCaptureCamera.targetTexture = new RenderTexture(1600, 900, 24) { name = "Diva UI Capture" };
                UiCaptureCamera.enabled = false;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = UiCaptureCamera;
                canvas.planeDistance = 1;
            }
            else canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            var root = go.transform;
            hudRoot = DivaUi.Stretch(root, "HUD");
            selectRoot = DivaUi.Stretch(root, "Select");
            highlightRoot = DivaUi.Stretch(root, "Highlight");
            resultsRoot = DivaUi.Stretch(root, "Results");
            heroRoot = DivaUi.Stretch(root, "Hero");
            countdownRoot = DivaUi.Stretch(root, "Countdown");
            bannerRoot = DivaUi.Stretch(root, "Banner");
            topRoot = DivaUi.Stretch(root, "Top");
            BuildTop(); BuildSelect(); BuildHud(); BuildBanner(); BuildCountdown(); BuildHighlightUi(); BuildResultsUi(); BuildHero(); BuildCameraFeed(); BuildCameraCheck();
            if (DivaVR.Active && DivaVR.MomentsAnchor) MoveMomentsForward();
            if (DivaVR.Active && DivaVR.DashAnchor) MoveSkillsToDash();
        }

        /// <summary>VR：倒数和技能横幅搬到前方 3 米多、3.6 米宽的空中面板上（更大、更有电影感；仪表盘只留数据）。</summary>
        void MoveMomentsForward()
        {
            var go = new GameObject("Diva Show Moments", typeof(RectTransform), typeof(Canvas));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(DivaVR.MomentsAnchor, false);
            rt.sizeDelta = new Vector2(1920, 1080);
            rt.localPosition = Vector3.zero; rt.localRotation = Quaternion.identity;
            rt.localScale = Vector3.one * (3.6f / 1920f);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace; c.worldCamera = mainCamera; c.sortingOrder = 55;
            bannerRoot.SetParent(rt, false);
            countdownRoot.SetParent(rt, false);
            BuildVrCard(rt);
        }

        // VR：技能栏和大招充能环搬到仪表台上斜放的小屏（低头看仪表就知道冲刺、水量、大招），漂浮面板只留分数和任务
        RectTransform dashDisplay;
        void MoveSkillsToDash()
        {
            var go = new GameObject("Diva Show Dash", typeof(RectTransform), typeof(Canvas));
            go.layer = 5;
            dashDisplay = (RectTransform)go.transform;
            dashDisplay.SetParent(DivaVR.DashAnchor, false);
            dashDisplay.sizeDelta = new Vector2(1920, 280);
            dashDisplay.localPosition = Vector3.zero; dashDisplay.localRotation = Quaternion.identity;
            dashDisplay.localScale = Vector3.one * (.7f / 1920f);
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace; c.worldCamera = mainCamera; c.sortingOrder = 52;
            var move = new List<Transform>();
            foreach (Transform child in hudRoot)
            {
                var n = child.name;
                if (n.StartsWith("Boost") || n.StartsWith("Cannon") || n.StartsWith("Bubble") || n.StartsWith("Fire ") && n != "Fire Glow" ||
                    n.StartsWith("Ult Back") || n.StartsWith("Ult Charge") || n.StartsWith("Ult Percent") || n.StartsWith("Ult Name") || n.StartsWith("Ult Key"))
                    move.Add(child);
            }
            foreach (var t in move) t.SetParent(dashDisplay, false);
        }

        // VR 操作卡：点亮和倒数时出现在前方横幅下面，开跑两秒后淡出（第一次戴头显的人最需要）
        CanvasGroup vrCard; RectTransform vrCardRect;
        readonly TextMeshProUGUI[] vrRowA = new TextMeshProUGUI[5], vrRowB = new TextMeshProUGUI[5];
        /// <summary>VR：没推杆之前卡片多留一会儿（DivaVRHowTo 管）。</summary>
        public bool VrCardHold;
        /// <summary>VR：教学演示时把选涂装的横幅藏起来（DivaVRGuide 管）。</summary>
        public bool VrSelectHidden;
        public RectTransform VrCardRect => vrCardRect;
        void BuildVrCard(RectTransform parent)
        {
            // 左边 1/3 留空给小动画（DivaVRHowTo 把 3D 小人放在那里，和亮着的那一行同步做动作）
            var card = DivaUi.Rect(parent, "VR Controls Card", new Vector2(.5f, .5f), new Vector2(0, -250), new Vector2(1180, 420));
            vrCardRect = card;
            vrCard = card.gameObject.AddComponent<CanvasGroup>();
            vrCard.alpha = 0; vrCard.blocksRaycasts = false;
            // VR 专用的卡片：底板不透明（线性色彩空间里 0.9 的深色板也会透出不少背景）
            DivaUi.Panel(card, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1180, 420), DivaUi.WithAlpha(Ink, 1), new Color(.6f, .6f, .75f, 1), 28).stripeWidth = 8;
            var title = DivaUi.Text(card, "Title", new Vector2(.5f, 1), new Vector2(170, -44), new Vector2(800, 60), 46, Color.white);
            DivaUi.Localize(title, () => DivaText.T("vr.card.title"));
            DivaUi.Glow(title, new Color(1, .3f, .7f, .9f));
            for (int i = 1; i <= 5; i++)
            {
                int n = i;
                float y = -100 - (i - 1) * 60;
                var a = DivaUi.Text(card, "Row " + i, new Vector2(.5f, 1), new Vector2(-40, y), new Vector2(400, 56), 36, Color.white, TextAlignmentOptions.Right, false);
                DivaUi.Localize(a, () => DivaText.T("vr.card." + n));
                var b = DivaUi.Text(card, "Row " + i + " b", new Vector2(.5f, 1), new Vector2(370, y), new Vector2(420, 56), 34, new Color(.55f, 1f, .9f), TextAlignmentOptions.Left, false);
                DivaUi.Localize(b, () => "— " + DivaText.T("vr.card." + n + "b"));
                vrRowA[i - 1] = a; vrRowB[i - 1] = b;
            }
        }

        /// <summary>卡片上亮哪一行（0–4；-1 全亮）。</summary>
        public void VrCardHighlight(int row)
        {
            for (int i = 0; i < 5; i++)
            {
                if (!vrRowA[i]) continue;
                bool on = row < 0 || row == i;
                vrRowA[i].color = on ? Color.white : new Color(1, 1, 1, .35f);
                vrRowB[i].color = on ? new Color(.55f, 1f, .9f) : new Color(.55f, 1f, .9f, .35f);
                vrRowA[i].fontSize = on && row >= 0 ? 40 : 36;
            }
        }

        void UpdateVrCard()
        {
            if (dashDisplay) dashDisplay.gameObject.SetActive(hudRoot.gameObject.activeSelf);
            if (!vrCard) return;
            // 只在起跑前后露一下；大招结束回到 Play 时 PhaseTime 会归零，不能再弹出来压住教练提示
            bool show = Phase == ShowPhase.PowerUp || Phase == ShowPhase.Countdown || Phase == ShowPhase.Play && PhaseTime < 3 && Time.time - runStart < 3 || VrCardHold;
            vrCard.alpha = Mathf.MoveTowards(vrCard.alpha, show ? 1 : 0, DivaClock.DeltaTime * (show ? 3 : 1.2f));
        }

        /// <summary>VR 的驾驶方法卡片还在眼前（教练提示等它走了再出来，免得叠字）。</summary>
        public bool VrCardVisible => vrCard && vrCard.alpha > .05f;

        void ShowRoots()
        {
            if (!canvas) return;
            Show(selectRoot, Phase == ShowPhase.Select && !VrSelectHidden);
            if (checkRoot) checkRoot.gameObject.SetActive(Phase == ShowPhase.CameraCheck);   // Eva 的摄像头识别页（VR 里不会进这个阶段）
            Show(hudRoot, Phase == ShowPhase.Play || Phase == ShowPhase.Countdown);
            countdownRoot.gameObject.SetActive(Phase == ShowPhase.Countdown || Phase == ShowPhase.Play && PhaseTime < 1);
            highlightRoot.gameObject.SetActive(Phase == ShowPhase.Highlight);
            Show(resultsRoot, Phase == ShowPhase.Results && !DivaVR.Active);   // VR：结算只留英雄卡 + 教学牌的结算板，桌面版三块板不画
            heroRoot.gameObject.SetActive(Phase == ShowPhase.Finish && !DivaVR.Active);   // VR 的英雄时刻卡是驾驶舱前的空间版（DivaVRCelebration）
            topRoot.gameObject.SetActive(!DivaVR.Active && Phase != ShowPhase.Highlight && Phase != ShowPhase.Intro && Phase != ShowPhase.RaceIntro);
        }

        // VR：大面板（结算、选涂装、比赛 HUD）一下出现 / 消失，占了大半个视野，整个画面一下变暗变亮就是"闪"。淡入淡出 0.35 秒
        void Show(RectTransform root, bool on)
        {
            if (!DivaVR.Active) { root.gameObject.SetActive(on); return; }
            var g = root.GetComponent<CanvasGroup>();
            if (!g) { g = root.gameObject.AddComponent<CanvasGroup>(); g.alpha = on ? 0 : 1; g.blocksRaycasts = false; }
            if (on && !root.gameObject.activeSelf) { root.gameObject.SetActive(true); g.alpha = 0; }
            g.alpha = Mathf.MoveTowards(g.alpha, on ? 1 : 0, Time.unscaledDeltaTime / .35f);
            if (!on && g.alpha <= 0) root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ language + setup (top right)
        void BuildTop()
        {
            var a = new Vector2(1, 1);
            DivaUi.Button(topRoot, "Language", a, new Vector2(-28, -24), new Vector2(190, 58), () => DivaText.T("lang"),
                          Ink, Color.white, 30, DivaText.Toggle, 14);
            DivaUi.Button(topRoot, "Setup", a, new Vector2(-236, -24), new Vector2(150, 58), () => DivaText.T("setup"),
                          InkLight, new Color(1, 1, 1, .8f), 28, ToggleSetup, 14);
        }

        // ------------------------------------------------------------------ select
        TextMeshProUGUI selectTitle, skinNameText, skinIndexText, statusText;
        readonly List<(DivaSlant a, DivaSlant b, DivaSlant c, Image mark)> chips = new List<(DivaSlant, DivaSlant, DivaSlant, Image)>();
        Button onePlayer, threePlayers;

        void BuildSelect()
        {
            var tl = new Vector2(0, 1);
            // 有 Eva 的开场时，游戏标题由她的开场展示，这里只写"选择机甲涂装"
            bool evaTitle = raceIntro;
            selectTitle = DivaUi.Text(selectRoot, "Title", tl, new Vector2(64, -40), new Vector2(1000, 130), evaTitle ? 96 : 112, Color.white, TextAlignmentOptions.Left);
            DivaUi.Localize(selectTitle, () => DivaText.T(evaTitle ? "select.title" : "game"));
            DivaUi.Glow(selectTitle, new Color(1, .3f, .7f, .9f));
            if (!evaTitle)
            {
                var sub = DivaUi.Text(selectRoot, "Subtitle", tl, new Vector2(70, -168), new Vector2(1000, 60), 46, new Color(1, 1, 1, .92f), TextAlignmentOptions.Left);
                DivaUi.Localize(sub, () => DivaText.T("select.title"));
            }

            var bottom = new Vector2(.5f, 0);
            DivaUi.Panel(selectRoot, "Name Plate", bottom, new Vector2(0, 182), new Vector2(1180, 150), Ink, DivaUi.WithAlpha(Ink, .2f), 40);
            skinNameText = DivaUi.Text(selectRoot, "Skin Name", bottom, new Vector2(0, 190), new Vector2(1100, 140), 118, Color.white);
            skinIndexText = DivaUi.Text(selectRoot, "Skin Index", bottom, new Vector2(0, 318), new Vector2(600, 50), 34, new Color(1, 1, 1, .75f));
            DivaUi.Button(selectRoot, "Previous", bottom, new Vector2(-660, 190), new Vector2(110, 110), () => "<", Ink, Color.white, 80, () => SwitchSkin(-1), 20);
            DivaUi.Button(selectRoot, "Next", bottom, new Vector2(660, 190), new Vector2(110, 110), () => ">", Ink, Color.white, 80, () => SwitchSkin(1), 20);
            int n = skins ? skins.skins.Length : 0;
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * .5f) * 120;
                int index = i;
                var a = DivaUi.Panel(selectRoot, "Chip " + i, bottom, new Vector2(x - 28, 118), new Vector2(40, 34), skins.skins[i].primary, skins.skins[i].primary * .85f, 10);
                var b = DivaUi.Panel(selectRoot, "Chip " + i + " b", bottom, new Vector2(x + 8, 118), new Vector2(28, 34), skins.skins[i].secondary, skins.skins[i].secondary * .85f, 10);
                var c = DivaUi.Panel(selectRoot, "Chip " + i + " c", bottom, new Vector2(x + 34, 118), new Vector2(18, 34), skins.skins[i].accent, skins.skins[i].accent, 10);
                a.raycastTarget = true;
                a.gameObject.AddComponent<Button>().onClick.AddListener(() => PickSkin(index));
                var mark = DivaUi.Image(selectRoot, "Chip " + i + " mark", bottom, new Vector2(x, 92), new Vector2(96, 6), Color.white);
                chips.Add((a, b, c, mark));
            }

            var right = new Vector2(1, .5f);
            onePlayer = DivaUi.Button(selectRoot, "One Player", right, new Vector2(-300, 70), new Vector2(240, 64), () => DivaText.T("select.players1"),
                                      Ink, Color.white, 34, () => SetPlayers(true), 14);
            threePlayers = DivaUi.Button(selectRoot, "Three Players", right, new Vector2(-48, 70), new Vector2(240, 64), () => DivaText.T("select.players3"),
                                         Ink, Color.white, 34, () => SetPlayers(false), 14);
            statusText = DivaUi.Text(selectRoot, "Status", right, new Vector2(-48, -12), new Vector2(720, 60), 32, Color.white, TextAlignmentOptions.Right, false);
            DivaUi.Button(selectRoot, "Start", right, new Vector2(-48, -112), new Vector2(420, 110), () => DivaText.T("select.start"),
                          new Color(1, .33f, .66f, .95f), Color.white, 64, ConfirmSelect, 26);
            var hint = DivaUi.Text(selectRoot, "Gesture Hint", bottom, new Vector2(0, 46), new Vector2(1700, 40), 30, new Color(1, 1, 1, .9f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(hint, () => DivaText.T("select.gesture"));
            var keys = DivaUi.Text(selectRoot, "Key Hint", bottom, new Vector2(0, 12), new Vector2(1700, 34), 24, new Color(1, 1, 1, .6f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(keys, () => DivaText.T("select.keys"));
            // VR：玩家人数、开始按钮、键盘提示和标题都不要（点不到，挤）；驾驶舱里的"下一步"指引牌说该做什么
            if (DivaVR.Active)
                foreach (var hide in new[] { "One Player", "Three Players", "Status", "Start", "Gesture Hint", "Key Hint", "Title", "Subtitle", "Previous", "Next" })
                    selectRoot.Find(hide)?.gameObject.SetActive(false);
        }

        void RefreshSelect()
        {
            if (!skinNameText) return;
            int n = skins ? skins.skins.Length : 0, i = skins ? Mathf.Max(0, skins.Current) : 0;
            skinNameText.text = SkinName;
            DivaUi.Glow(skinNameText, DivaUi.WithAlpha(Accent, .95f), .7f, .4f, .1f);
            skinIndexText.text = DivaText.F("select.skin", i + 1, Mathf.Max(1, n));
            for (int k = 0; k < chips.Count; k++)
            {
                bool on = k == i;
                chips[k].mark.color = on ? Color.white : new Color(1, 1, 1, .15f);
                chips[k].a.rectTransform.localScale = chips[k].b.rectTransform.localScale = chips[k].c.rectTransform.localScale = Vector3.one * (on ? 1.15f : .9f);
            }
            if (onePlayer)
            {
                ((DivaSlant)onePlayer.targetGraphic).Set(singlePlayer ? Accent * new Color(1, 1, 1, .9f) : Ink, singlePlayer ? Accent * .7f : DivaUi.WithAlpha(Ink, .5f));
                ((DivaSlant)threePlayers.targetGraphic).Set(!singlePlayer ? Accent * new Color(1, 1, 1, .9f) : Ink, !singlePlayer ? Accent * .7f : DivaUi.WithAlpha(Ink, .5f));
            }
            fx?.TintRing(Accent * 1.5f);
        }

        // ------------------------------------------------------------------ camera feed (players see themselves)
        RectTransform feedRoot;
        RawImage feedImage;
        TextMeshProUGUI feedStatus;
        DivaSlant feedFrame;

        void BuildCameraFeed()
        {
            var root = DivaUi.Stretch(canvas.transform, "Camera Feed");
            root.SetSiblingIndex(topRoot.GetSiblingIndex());
            feedRoot = DivaUi.Rect(root, "Feed", new Vector2(1, 1), new Vector2(-48, -100), new Vector2(380, 285 + 40));
            feedFrame = DivaUi.Panel(feedRoot, "Frame", new Vector2(.5f, .5f), Vector2.zero, new Vector2(380, 325), Ink, DivaUi.WithAlpha(Ink, .6f), 0);
            feedFrame.stripeWidth = 6;
            var label = DivaUi.Text(feedRoot, "Label", new Vector2(0, 1), new Vector2(14, -4), new Vector2(300, 34), 26, new Color(1, 1, 1, .85f), TextAlignmentOptions.Left);
            DivaUi.Localize(label, () => DivaText.Chinese ? "摄像头" : "CAMERA");
            feedImage = DivaUi.Rect(feedRoot, "Image", new Vector2(.5f, 0), new Vector2(0, 8), new Vector2(364, 273)).gameObject.AddComponent<RawImage>();
            feedImage.raycastTarget = false;
            feedStatus = DivaUi.Text(feedRoot, "Status", new Vector2(.5f, 0), new Vector2(0, 130), new Vector2(340, 80), 24, new Color(1, 1, 1, .7f), TextAlignmentOptions.Center, false);
            feedStatus.textWrappingMode = TextWrappingModes.Normal;
            feedRoot.gameObject.SetActive(false);
        }

        const float PlayFeedScale = .8f;

        /// <summary>Right edge offset for the skill feed: left of the camera picture while it is showing in play.</summary>
        float FeedRight => feedRoot && feedRoot.gameObject.activeSelf && Phase == ShowPhase.Play ? -30 - 380 * PlayFeedScale - 18 : -30;

        /// <summary>游戏中在右上角（按钮下方，0.8 倍）；技能提示条移到它左边。</summary>
        void ShowCameraFeed(bool on)
        {
            if (!feedRoot) return;
            if (feedRoot.gameObject.activeSelf != on) feedRoot.gameObject.SetActive(on);
            if (!on) return;
            bool play = Phase == ShowPhase.Play;
            // Eva: in play the camera sits at the top right under the language/SETUP buttons, a little bigger than
            // before (was bottom left at 60%); the skill feed moves to its left (see FeedRight).
            feedRoot.anchorMin = feedRoot.anchorMax = feedRoot.pivot = new Vector2(1, 1);
            feedRoot.anchoredPosition = play ? new Vector2(-30, -96) : new Vector2(-48, -100);
            feedRoot.localScale = Vector3.one * (play ? PlayFeedScale : 1);
            feedFrame.stripe = Accent;
            var tex = cameraPreview ? cameraPreview.PreviewTexture : null;
            feedImage.texture = tex;
            feedImage.color = tex ? Color.white : new Color(0, 0, 0, .6f);
            feedStatus.text = tex ? "" : cameraPreview ? cameraPreview.Status : "";
        }

        // ------------------------------------------------------------------ countdown + banner
        TextMeshProUGUI countdownText;
        void BuildCountdown()
        {
            countdownText = DivaUi.Text(countdownRoot, "Count", new Vector2(.5f, .5f), new Vector2(0, 60), new Vector2(900, 400), 300, Color.white);
            DivaUi.Glow(countdownText, new Color(1, .35f, .75f, .9f), .7f, .45f, .1f);
        }

        DivaSlant bannerPlate;
        TextMeshProUGUI bannerText, bannerSub;
        float bannerStart = -100, bannerSeconds;
        void BuildBanner()
        {
            var c = new Vector2(.5f, .5f);
            bannerPlate = DivaUi.Panel(bannerRoot, "Plate", c, new Vector2(0, 250), new Vector2(980, 130), Color.white, Color.white, 46);
            bannerText = DivaUi.Text(bannerRoot, "Name", c, new Vector2(0, 258), new Vector2(1600, 140), 96, Color.white);
            bannerSub = DivaUi.Text(bannerRoot, "Sub", c, new Vector2(0, 168), new Vector2(1400, 60), 40, Color.white);
            bannerRoot.gameObject.SetActive(false);
        }

        /// <summary>技能横幅：斜切底板从中间展开，技能名放大弹出并发光。</summary>
        void Banner(string title, string sub, Color color, float seconds = 1.8f, string sfx = "skill")
        {
            bannerRoot.gameObject.SetActive(true);
            bannerStart = DivaClock.Time; bannerSeconds = seconds;
            Debug.Log("DIVA_SHOW_BANNER " + title + (string.IsNullOrEmpty(sub) ? "" : " · " + sub));
            bannerText.text = title; bannerSub.text = sub ?? "";
            DivaUi.Glow(bannerText, DivaUi.WithAlpha(color, .95f), .75f, .45f, .12f);
            bannerPlate.Set(DivaUi.WithAlpha(color * .55f, .78f), DivaUi.WithAlpha(color * .25f, .55f));
            bannerPlate.stripe = Color.white; bannerPlate.stripeWidth = 10;
            if (!string.IsNullOrEmpty(sfx)) sound.Play(sfx);
        }

        void UpdateBanners()
        {
            if (!bannerRoot.gameObject.activeSelf) return;
            float t = DivaClock.Time - bannerStart;
            if (t > bannerSeconds) { bannerRoot.gameObject.SetActive(false); return; }
            float open = DivaUi.EaseOut(t / .18f), close = 1 - DivaUi.Smooth((t - bannerSeconds + .3f) / .3f);
            // VR：横幅就在眼前 3 米、很大，0.18 秒弹开像闪一下：慢慢展开、淡入淡出
            if (DivaVR.Active) { open = DivaUi.Smooth(t / .4f); bannerPlate.canvasRenderer.SetAlpha(open * close); }
            bannerPlate.rectTransform.localScale = new Vector3(open * close, 1, 1);
            float pop = DivaUi.EaseOutBack(t / .3f);
            bannerText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1, pop) * close;
            bannerText.alpha = Mathf.Clamp01(t / .12f) * close;
            bannerSub.alpha = Mathf.Clamp01((t - .15f) / .2f) * close;
            bannerText.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-40, 0, pop), 258);
        }
    }
}
