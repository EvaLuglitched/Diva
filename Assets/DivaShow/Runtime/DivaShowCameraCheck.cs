using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    // Eva：开始流程改成 主页（选颜色 + 人数 → START）→ 大屏摄像头识别页 → 全员绿色保持 5 秒 → 自动保存站姿（校准）→ Eva 的开场。
    // 摄像头识别页只在 Camera 模式出现；Test controls / 键盘模式按 START 直接进开场。
    public partial class DivaShowDirector
    {
        /// <summary>Seconds every player must stay recognised before the neutral pose is saved and the game starts.</summary>
        const float CheckHoldSeconds = 5;

        RectTransform checkRoot;
        RawImage checkImage;
        RectTransform checkImageRect;
        DivaSlant checkFrame;
        TextMeshProUGUI checkTitle, checkSub, checkCount, checkStatus;
        readonly List<(DivaSlant plate, TextMeshProUGUI label)> checkChips = new List<(DivaSlant, TextMeshProUGUI)>();
        float checkHeldSince = -1;
        bool checkStarting;

        static readonly Color Recognised = new Color(.35f, .95f, .5f);
        static readonly Color Waiting = new Color(.55f, .55f, .62f);

        // ------------------------------------------------------------------ build (called from BuildUi)
        void BuildCameraCheck()
        {
            checkRoot = DivaUi.Stretch(canvas.transform, "Camera Check");
            checkRoot.SetSiblingIndex(topRoot.GetSiblingIndex());
            var dim = DivaUi.Image(checkRoot, "Dim", new Vector2(.5f, .5f), Vector2.zero, new Vector2(4000, 3000), new Color(.04f, .02f, .1f, .72f));
            dim.raycastTarget = false;

            var top = new Vector2(.5f, 1);
            checkTitle = DivaUi.Text(checkRoot, "Title", top, new Vector2(0, -78), new Vector2(1600, 110), 84, Color.white);
            DivaUi.Localize(checkTitle, () => DivaText.T("check.title"));
            DivaUi.Glow(checkTitle, new Color(1, .3f, .7f, .9f));
            checkSub = DivaUi.Text(checkRoot, "Subtitle", top, new Vector2(0, -150), new Vector2(1700, 56), 36, new Color(1, 1, 1, .92f), TextAlignmentOptions.Center, false);

            var centre = new Vector2(.5f, .5f);
            checkFrame = DivaUi.Panel(checkRoot, "Frame", centre, new Vector2(0, -6), new Vector2(1304, 744), Waiting, DivaUi.WithAlpha(Waiting, .6f), 0);
            checkImageRect = DivaUi.Rect(checkRoot, "Image", centre, new Vector2(0, -6), new Vector2(1280, 720));
            checkImage = checkImageRect.gameObject.AddComponent<RawImage>();
            checkImage.raycastTarget = false;
            checkStatus = DivaUi.Text(checkRoot, "Camera Status", centre, new Vector2(0, -6), new Vector2(1100, 200), 38, new Color(1, .85f, .7f), TextAlignmentOptions.Center, false);
            checkStatus.textWrappingMode = TextWrappingModes.Normal;
            checkCount = DivaUi.Text(checkRoot, "Countdown", centre, new Vector2(0, 0), new Vector2(1200, 300), 150, Color.white);
            DivaUi.Glow(checkCount, new Color(.2f, .9f, .45f, .9f), .7f, .45f, .12f);

            var bottom = new Vector2(.5f, 0);
            for (int i = 0; i < 3; i++)
            {
                var plate = DivaUi.Panel(checkRoot, "Player " + (i + 1), bottom, new Vector2((i - 1) * 300, 78), new Vector2(260, 70), Waiting, DivaUi.WithAlpha(Waiting, .6f), 14);
                var label = DivaUi.Text(plate.transform, "Label", centre, Vector2.zero, new Vector2(260, 70), 36, Color.white);
                checkChips.Add((plate, label));
            }
            var keys = DivaUi.Text(checkRoot, "Key Hint", bottom, new Vector2(0, 22), new Vector2(1700, 34), 24, new Color(1, 1, 1, .65f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(keys, () => DivaText.T("check.keys"));
            checkRoot.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ phase
        void EnterCameraCheck()
        {
            checkHeldSince = -1;
            checkStarting = false;
            if (controller && controller.CalibrationPending) controller.CancelCalibrationCountdown();
            if (checkSub) checkSub.text = DivaText.T(singlePlayer ? "check.sub1" : "check.sub3");
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
                checkImageRect.sizeDelta = aspect >= 16f / 9 ? new Vector2(1280, 1280 / aspect) : new Vector2(720 * aspect, 720);
            }
            checkFrame.rectTransform.sizeDelta = checkImageRect.sizeDelta + new Vector2(24, 24);
            string problem = CameraProblem();
            checkStatus.text = problem ?? "";

            // Who is recognised (assigned by the camera bridge, fresh and confident enough).
            demo.AllVisible(now);   // applies the confidence / timeout settings
            int seen = 0;
            for (int i = 0; i < checkChips.Count; i++)
            {
                bool used = i < count;
                checkChips[i].plate.gameObject.SetActive(used);
                if (!used) continue;
                bool ok = demo.State.Visible(i + 1, now);
                if (ok) seen++;
                var c = ok ? Recognised : Waiting;
                checkChips[i].plate.Set(c, c * .7f);
                checkChips[i].label.text = (singlePlayer ? DivaText.T("check.you") : "P" + (i + 1)) + "  " + DivaText.T(ok ? "check.ok" : "check.wait");
            }
            bool all = problem == null && seen == count;
            var frame = all ? Recognised : Waiting;
            checkFrame.Set(frame, frame * .7f);

            if (checkStarting) return;
            if (!all) { checkHeldSince = -1; checkCount.text = ""; return; }
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

        /// <summary>Leave the home page / camera check: Eva's race intro (or the power-up when there is none).</summary>
        void StartAfterSelect() => Enter(raceIntro ? ShowPhase.RaceIntro : ShowPhase.PowerUp);
    }
}
