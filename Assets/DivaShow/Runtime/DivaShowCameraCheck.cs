using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    // Eva：开始流程改成 主页（选颜色 + 人数 → START）→ 大屏摄像头识别页 → 全员绿色保持 5 秒 → 自动保存站姿（校准）→ Eva 的开场。
    // 摄像头识别页只在 Camera 模式出现；Test controls / 键盘模式按 START 直接进开场。
    // 画面下方每个玩家一张卡：角色 + 一句怎么操作，和 DivaControlState 一致（P1 前进，P2 转弯，P3 瞄准/喷水/竖拇指吃喝）。
    // 进入识别页先在画面中央放 8 秒火柴人动画演示三个玩家的动作（DivaStickFigure，Eva 的手绘稿），然后淡出；
    // 只有白色线条、没有背景，摄像头画面一直看得到。演示期间不开始"保持 5 秒"的计时（免得大家跟着做动作时被存成站姿）。
    public partial class DivaShowDirector
    {
        /// <summary>Seconds every player must stay recognised before the neutral pose is saved and the game starts.</summary>
        const float CheckHoldSeconds = 5;
        /// <summary>Seconds the stick-figure demo plays in the middle of the picture when the page opens.</summary>
        const float DemoSeconds = 8;
        /// <summary>Camera picture area on the check page (the role cards sit below it).</summary>
        const float CheckWidth = 1104, CheckHeight = 621, CheckY = 27;

        RectTransform checkRoot;
        RawImage checkImage;
        RectTransform checkImageRect;
        DivaSlant checkFrame;
        TextMeshProUGUI checkTitle, checkSub, checkCount, checkStatus, checkUlt;
        readonly List<(DivaSlant plate, TextMeshProUGUI role, TextMeshProUGUI state, TextMeshProUGUI how)> checkCards =
            new List<(DivaSlant, TextMeshProUGUI, TextMeshProUGUI, TextMeshProUGUI)>();
        readonly List<(DivaStickFigure figure, TextMeshProUGUI caption)> checkFigures = new List<(DivaStickFigure, TextMeshProUGUI)>();
        readonly List<TextMeshProUGUI> checkFigureLabels = new List<TextMeshProUGUI>();
        RectTransform checkDemo;
        CanvasGroup checkDemoGroup;
        float checkEnteredAt;
        float checkHeldSince = -1;
        bool checkStarting;

        static readonly Color Recognised = new Color(.35f, .95f, .5f);
        static readonly Color Waiting = new Color(.55f, .55f, .62f);
        // Role card background: green while that player is recognised (like the old P1/P2/P3 chips), dark while waiting.
        static readonly Color CardOn = new Color(.13f, .58f, .3f, .92f), CardOnBottom = new Color(.62f, .62f, .62f, 1);

        // ------------------------------------------------------------------ build (called from BuildUi)
        void BuildCameraCheck()
        {
            checkRoot = DivaUi.Stretch(canvas.transform, "Camera Check");
            checkRoot.SetSiblingIndex(topRoot.GetSiblingIndex());
            var dim = DivaUi.Image(checkRoot, "Dim", new Vector2(.5f, .5f), Vector2.zero, new Vector2(4000, 3000), new Color(.04f, .02f, .1f, .72f));
            dim.raycastTarget = false;

            var top = new Vector2(.5f, 1);
            checkTitle = DivaUi.Text(checkRoot, "Title", top, new Vector2(0, -28), new Vector2(1600, 110), 84, Color.white);
            DivaUi.Localize(checkTitle, () => DivaText.T("check.title"));
            DivaUi.Glow(checkTitle, new Color(1, .3f, .7f, .9f));
            checkSub = DivaUi.Text(checkRoot, "Subtitle", top, new Vector2(0, -128), new Vector2(1700, 50), 36, new Color(1, 1, 1, .92f), TextAlignmentOptions.Center, false);

            var centre = new Vector2(.5f, .5f);
            var picture = new Vector2(0, CheckY);
            checkFrame = DivaUi.Panel(checkRoot, "Frame", centre, picture, new Vector2(CheckWidth + 24, CheckHeight + 24), Waiting, DivaUi.WithAlpha(Waiting, .6f), 0);
            checkImageRect = DivaUi.Rect(checkRoot, "Image", centre, picture, new Vector2(CheckWidth, CheckHeight));
            checkImage = checkImageRect.gameObject.AddComponent<RawImage>();
            checkImage.raycastTarget = false;
            // Stick-figure demo in the middle of the picture (P1 · P2 · P3 left to right, as players stand in the mirrored
            // preview): white lines only, no background, fades out after DemoSeconds.
            checkDemo = DivaUi.Rect(checkRoot, "Moves Demo", centre, new Vector2(0, CheckY), new Vector2(CheckWidth, CheckHeight));
            checkDemoGroup = checkDemo.gameObject.AddComponent<CanvasGroup>();
            checkDemoGroup.blocksRaycasts = false; checkDemoGroup.interactable = false;
            for (int i = 0; i < 3; i++)
            {
                float x = (i - 1) * 340;
                var spot = DivaUi.Rect(checkDemo, "Figure " + (i + 1), centre, new Vector2(x, -14), new Vector2(260, 300));
                spot.gameObject.AddComponent<CanvasRenderer>();
                var figure = spot.gameObject.AddComponent<DivaStickFigure>();
                figure.act = (DivaStickFigure.Act)i; figure.thickness = 7; figure.color = Color.white; figure.raycastTarget = false;
                var caption = DivaUi.Text(checkDemo, "Figure Caption " + (i + 1), centre, new Vector2(x, -186), new Vector2(320, 44), 34, Color.white);
                var label = DivaUi.Text(checkDemo, "Figure Label " + (i + 1), centre, new Vector2(x, 186), new Vector2(200, 50), 40, Color.white);
                checkFigures.Add((figure, caption));
                checkFigureLabels.Add(label);
            }
            checkStatus = DivaUi.Text(checkRoot, "Camera Status", centre, picture, new Vector2(1000, 200), 38, new Color(1, .85f, .7f), TextAlignmentOptions.Center, false);
            checkStatus.textWrappingMode = TextWrappingModes.Normal;
            checkCount = DivaUi.Text(checkRoot, "Countdown", centre, picture, new Vector2(1100, 300), 150, Color.white);
            DivaUi.Glow(checkCount, new Color(.2f, .9f, .45f, .9f), .7f, .45f, .12f);

            // Role cards: left stripe and READY / WAIT show whether that player is recognised.
            var bottom = new Vector2(.5f, 0);
            for (int i = 0; i < 3; i++)
            {
                var plate = DivaUi.Panel(checkRoot, "Player " + (i + 1), bottom, new Vector2((i - 1) * 600, 88), new Vector2(560, 144), Ink, DivaUi.WithAlpha(Ink, .5f), 14);
                plate.stripe = Waiting; plate.stripeWidth = 12;
                var role = DivaUi.Text(plate.transform, "Role", centre, new Vector2(10, 40), new Vector2(500, 50), 36, Color.white, TextAlignmentOptions.Left);
                var state = DivaUi.Text(plate.transform, "State", centre, new Vector2(10, 40), new Vector2(500, 50), 26, Waiting, TextAlignmentOptions.Right, false);
                var how = DivaUi.Text(plate.transform, "How", centre, new Vector2(10, -24), new Vector2(500, 76), 26, new Color(1, 1, 1, .9f), TextAlignmentOptions.TopLeft, false);
                how.textWrappingMode = TextWrappingModes.Normal;
                how.enableAutoSizing = true; how.fontSizeMin = 18; how.fontSizeMax = 26;
                checkCards.Add((plate, role, state, how));
            }
            checkUlt = DivaUi.Text(checkRoot, "Ult Hint", bottom, new Vector2(0, 48), new Vector2(1700, 32), 24, new Color(1, .85f, .4f, .9f), TextAlignmentOptions.Center, false);
            var keys = DivaUi.Text(checkRoot, "Key Hint", bottom, new Vector2(0, 14), new Vector2(1700, 32), 24, new Color(1, 1, 1, .65f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(keys, () => DivaText.T("check.keys"));
            checkRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ phase
        void EnterCameraCheck()
        {
            checkHeldSince = -1;
            checkStarting = false;
            checkEnteredAt = Time.unscaledTime;
            foreach (var (figure, _) in checkFigures) figure.startTime = checkEnteredAt;
            if (controller && controller.CalibrationPending) controller.CancelCalibrationCountdown();
            if (checkCount) checkCount.text = "";
        }

        void UpdateCameraCheck()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { sound.Play("tick"); Enter(ShowPhase.Select); return; }
            if (confirmEdge) { sound.Play("confirm"); StartAfterSelect(); return; }   // skip: keyboard play, no calibration

            orbitAngle = orbitCenter + orbitDrag + 40 * Mathf.Sin((PhaseTime + 20) * .25f);
            float now = Time.realtimeSinceStartup;
            int count = singlePlayer ? 1 : 3;

            // The camera picture, kept at its own aspect ratio inside the 1280x720 area.
            var tex = cameraPreview ? cameraPreview.PreviewTexture : null;
            checkImage.texture = tex;
            checkImage.color = tex ? Color.white : new Color(0, 0, 0, .55f);
            if (tex && tex.height > 0)
            {
                float aspect = (float)tex.width / tex.height;
                checkImageRect.sizeDelta = aspect >= CheckWidth / CheckHeight ? new Vector2(CheckWidth, CheckWidth / aspect) : new Vector2(CheckHeight * aspect, CheckHeight);
            }
            checkFrame.rectTransform.sizeDelta = checkImageRect.sizeDelta + new Vector2(24, 24);
            string problem = CameraProblem();
            checkStatus.text = problem ?? "";

            // Who is recognised (assigned by the camera bridge, fresh and confident enough).
            // One player: the three cards are that person's three jobs, all following player 1.
            demo.AllVisible(now);   // applies the confidence / timeout settings
            checkSub.text = DivaText.T(singlePlayer ? "check.sub1" : "check.sub3");
            checkUlt.text = DivaText.T(singlePlayer ? "check.ult1" : "check.ult3");
            int seen = 0;
            for (int i = 0; i < checkCards.Count; i++)
            {
                bool ok = demo.State.Visible(singlePlayer ? 1 : i + 1, now);
                if (ok && (!singlePlayer || i == 0)) seen++;
                ShowCheckCard(i, ok);
            }
            // The moves demo: fades in, plays DemoSeconds, fades out and switches off.
            float demoAge = Time.unscaledTime - checkEnteredAt;
            bool demoing = demoAge < DemoSeconds;
            if (checkDemo.gameObject.activeSelf != demoing) checkDemo.gameObject.SetActive(demoing);
            if (demoing)
            {
                checkDemoGroup.alpha = Mathf.Clamp01(demoAge / .3f) * Mathf.Clamp01((DemoSeconds - demoAge) / .5f);
                for (int i = 0; i < checkFigures.Count; i++)
                {
                    checkFigures[i].caption.text = DivaText.T(checkFigures[i].figure.CaptionKey);
                    checkFigureLabels[i].text = singlePlayer ? "" : "P" + (i + 1);
                }
            }
            bool all = problem == null && seen == count;
            var frame = all ? Recognised : Waiting;
            checkFrame.Set(frame, frame * .7f);

            if (checkStarting) return;
            // The 5-second hold starts after the demo, so nobody's pose is saved while they copy the moves.
            if (!all || demoing) { checkHeldSince = -1; checkCount.text = ""; return; }
            if (checkHeldSince < 0) { checkHeldSince = now; sound.Play("tick"); }
            float held = now - checkHeldSince;
            int left = Mathf.CeilToInt(CheckHoldSeconds - held);
            checkCount.text = left > 0 ? DivaText.F("check.hold", left) : DivaText.T("check.go");
            checkCount.alpha = .85f + .15f * Mathf.Sin(held * Mathf.PI * 2);
            if (held < CheckHoldSeconds) return;

            // Everyone stayed green for 5 s: save this standing pose as neutral, then start.
            if (controller && controller.CalibrateAt(now))
            {
                checkStarting = true;
                sound.Play("confirm");
                StartAfterSelect();
            }
            else checkHeldSince = -1;   // someone slipped out at the last moment: count again
        }

        /// <summary>Role card i (0..2): role name, how to do it, and whether that player is recognised.</summary>
        void ShowCheckCard(int i, bool ok)
        {
            var card = checkCards[i];
            var c = ok ? Recognised : Waiting;
            if (card.plate.stripe != c)
            {
                card.plate.stripe = c;
                if (ok) card.plate.Set(CardOn, CardOnBottom); else card.plate.Set(Ink, DivaUi.WithAlpha(Ink, .5f));
            }
            string role = DivaText.T("check.role" + (i + 1));
            card.role.text = singlePlayer ? role : "P" + (i + 1) + "  ·  " + role;
            card.state.text = DivaText.T(ok ? "check.ok" : "check.wait");
            card.state.color = ok ? Color.white : Waiting;   // white on the green card
            card.how.text = DivaText.T("check.how" + (i + 1));
        }

        /// <summary>Leave the home page / camera check: Eva's race intro (or the power-up when there is none).</summary>
        void StartAfterSelect() => Enter(raceIntro ? ShowPhase.RaceIntro : ShowPhase.PowerUp);
    }
}
