using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
            // 后台模式没有屏幕，最上层界面拍不到：改用一个只拍界面的摄像机（不动、不加后期），测试截图时叠在主镜头上
            if (Application.isBatchMode)
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
            countdownRoot = DivaUi.Stretch(root, "Countdown");
            bannerRoot = DivaUi.Stretch(root, "Banner");
            topRoot = DivaUi.Stretch(root, "Top");
            BuildTop(); BuildSelect(); BuildHud(); BuildBanner(); BuildCountdown(); BuildHighlightUi(); BuildResultsUi(); BuildCameraFeed();
        }

        void ShowRoots()
        {
            if (!canvas) return;
            selectRoot.gameObject.SetActive(Phase == ShowPhase.Select);
            hudRoot.gameObject.SetActive(Phase == ShowPhase.Play || Phase == ShowPhase.Countdown);
            countdownRoot.gameObject.SetActive(Phase == ShowPhase.Countdown || Phase == ShowPhase.Play && PhaseTime < 1);
            highlightRoot.gameObject.SetActive(Phase == ShowPhase.Highlight);
            resultsRoot.gameObject.SetActive(Phase == ShowPhase.Results);
            topRoot.gameObject.SetActive(Phase != ShowPhase.Highlight && Phase != ShowPhase.Intro && Phase != ShowPhase.RaceIntro);
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

        /// <summary>选涂装时右上角大一点（方便校准时看到自己），游戏中缩小放在左下角。</summary>
        void ShowCameraFeed(bool on)
        {
            if (!feedRoot) return;
            if (feedRoot.gameObject.activeSelf != on) feedRoot.gameObject.SetActive(on);
            if (!on) return;
            bool play = Phase == ShowPhase.Play;
            feedRoot.anchorMin = feedRoot.anchorMax = feedRoot.pivot = play ? new Vector2(0, 0) : new Vector2(1, 1);
            feedRoot.anchoredPosition = play ? new Vector2(40, 40) : new Vector2(-48, -100);
            feedRoot.localScale = Vector3.one * (play ? .6f : 1);
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
            bannerStart = Time.unscaledTime; bannerSeconds = seconds;
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
            float t = Time.unscaledTime - bannerStart;
            if (t > bannerSeconds) { bannerRoot.gameObject.SetActive(false); return; }
            float open = DivaUi.EaseOut(t / .18f), close = 1 - DivaUi.Smooth((t - bannerSeconds + .3f) / .3f);
            bannerPlate.rectTransform.localScale = new Vector3(open * close, 1, 1);
            float pop = DivaUi.EaseOutBack(t / .3f);
            bannerText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1, pop) * close;
            bannerText.alpha = Mathf.Clamp01(t / .12f) * close;
            bannerSub.alpha = Mathf.Clamp01((t - .15f) / .2f) * close;
            bannerText.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-40, 0, pop), 258);
        }
    }
}
