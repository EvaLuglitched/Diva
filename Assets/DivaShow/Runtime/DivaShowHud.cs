using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    // 游戏中的 FPS 风格界面：左上分数和用时、左边任务、右上事件栏、底部技能栏（大招在中间）、
    // 锁定标记、命中飘字、连击数、速度线、画中画特写。
    public partial class DivaShowDirector
    {
        class SkillSlot
        {
            public DivaSlant plate, bar;
            public TextMeshProUGUI name, hint;
            public float flash = -10;
        }

        TextMeshProUGUI scoreText, scoreLabel, timeText, comboText, comboLabel, ultPercent, ultPrompt;
        readonly List<(TextMeshProUGUI mark, TextMeshProUGUI label)> taskRows = new List<(TextMeshProUGUI, TextMeshProUGUI)>();
        SkillSlot boostSlot, cannonSlot, bubbleSlot;
        DivaRing ultRing, ultBack, lockRing;
        DivaSlant ultPromptPlate;
        RectTransform lockMarker;
        RawImage speedLines, pipImage;
        DivaSlant pipFrame;
        TextMeshProUGUI pipLabel;
        readonly List<(TextMeshProUGUI text, DivaSlant plate, float born)> feed = new List<(TextMeshProUGUI, DivaSlant, float)>();
        readonly List<(TextMeshProUGUI text, Vector3 world, float born)> popups = new List<(TextMeshProUGUI, Vector3, float)>();
        float fovKick;

        void BuildHud()
        {
            var tl = new Vector2(0, 1);
            DivaUi.Panel(hudRoot, "Score Plate", tl, new Vector2(40, -36), new Vector2(380, 150), Ink, DivaUi.WithAlpha(Ink, .35f), 24).stripeWidth = 0;
            scoreLabel = DivaUi.Text(hudRoot, "Score Label", tl, new Vector2(64, -44), new Vector2(330, 40), 32, Color.white, TextAlignmentOptions.Left);
            DivaUi.Localize(scoreLabel, () => DivaText.T("hud.score"));
            scoreText = DivaUi.Text(hudRoot, "Score", tl, new Vector2(60, -78), new Vector2(340, 100), 96, Color.white, TextAlignmentOptions.Left);
            DivaUi.Glow(scoreText, new Color(1, .35f, .75f, .8f), .6f, .3f, .1f);
            DivaUi.Panel(hudRoot, "Time Plate", tl, new Vector2(40, -196), new Vector2(330, 60), InkLight, DivaUi.WithAlpha(InkLight, .2f), 18);
            timeText = DivaUi.Text(hudRoot, "Time", tl, new Vector2(62, -200), new Vector2(320, 54), 40, Color.white, TextAlignmentOptions.Left);

            var tasks = game ? game.tasks : new CourseTask[0];
            DivaUi.Panel(hudRoot, "Task Plate", tl, new Vector2(40, -272), new Vector2(380, 56 + 46 * tasks.Length), InkLight, DivaUi.WithAlpha(InkLight, .1f), 18);
            var taskTitle = DivaUi.Text(hudRoot, "Tasks", tl, new Vector2(62, -278), new Vector2(330, 44), 30, new Color(1, 1, 1, .8f), TextAlignmentOptions.Left);
            DivaUi.Localize(taskTitle, () => DivaText.T("hud.tasks"));
            for (int i = 0; i < tasks.Length; i++)
            {
                var mark = DivaUi.Text(hudRoot, "Task Mark " + i, tl, new Vector2(62, -322 - 46 * i), new Vector2(40, 44), 34, Color.white, TextAlignmentOptions.Left, false);
                var label = DivaUi.Text(hudRoot, "Task " + i, tl, new Vector2(104, -322 - 46 * i), new Vector2(300, 44), 30, Color.white, TextAlignmentOptions.Left, false);
                taskRows.Add((mark, label));
            }

            var bottom = new Vector2(.5f, 0);
            boostSlot = Slot("Boost", new Vector2(-470, 34), () => DivaText.T("skill.boost"), () => DivaText.T("key.boost"));
            cannonSlot = Slot("Cannon", new Vector2(-215, 34), () => DivaText.T("skill.cannon"), () => DivaText.T("key.cannon"));
            bubbleSlot = Slot("Bubble", new Vector2(215, 34), () => DivaText.T("skill.bubble"), () => DivaText.T("key.bubble"));
            ultBack = Ring(hudRoot, "Ult Back", bottom, new Vector2(0, 40), 168, 16, new Color(0, 0, 0, .55f));
            ultRing = Ring(hudRoot, "Ult Charge", bottom, new Vector2(0, 40), 168, 16, Color.white);
            ultPercent = DivaUi.Text(hudRoot, "Ult Percent", bottom, new Vector2(0, 92), new Vector2(160, 70), 54, Color.white);
            var ultName = DivaUi.Text(hudRoot, "Ult Name", bottom, new Vector2(0, 66), new Vector2(160, 30), 20, new Color(1, 1, 1, .85f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(ultName, () => DivaText.T("skill.ult"));
            var ultKey = DivaUi.Text(hudRoot, "Ult Key", bottom, new Vector2(0, 12), new Vector2(260, 26), 18, new Color(1, 1, 1, .6f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(ultKey, () => DivaText.T("key.ult"));
            ultPromptPlate = DivaUi.Panel(hudRoot, "Ult Prompt Plate", bottom, new Vector2(0, 222), new Vector2(1080, 58), DivaUi.WithAlpha(Ink, .8f), DivaUi.WithAlpha(Ink, .4f), 24);
            ultPrompt = DivaUi.Text(hudRoot, "Ult Prompt", bottom, new Vector2(0, 222), new Vector2(1500, 50), 40, Color.white);
            DivaUi.Localize(ultPrompt, () => DivaText.T("ult.ready"));

            lockMarker = DivaUi.Rect(hudRoot, "Lock", new Vector2(.5f, .5f), Vector2.zero, new Vector2(70, 70));
            lockMarker.gameObject.AddComponent<CanvasRenderer>();
            lockRing = lockMarker.gameObject.AddComponent<DivaRing>(); lockRing.thickness = 4; lockRing.raycastTarget = false;
            var dot = Ring(lockMarker, "Dot", new Vector2(.5f, .5f), Vector2.zero, 14, 7, Color.white);
            dot.raycastTarget = false;

            comboText = DivaUi.Text(hudRoot, "Combo", new Vector2(1, .5f), new Vector2(-70, 60), new Vector2(260, 110), 104, Color.white, TextAlignmentOptions.Right);
            comboLabel = DivaUi.Text(hudRoot, "Combo Label", new Vector2(1, .5f), new Vector2(-74, -6), new Vector2(360, 40), 30, Color.white, TextAlignmentOptions.Right);

            var speedRt = DivaUi.Stretch(hudRoot, "Speed Lines");
            speedLines = speedRt.gameObject.AddComponent<RawImage>();
            speedLines.raycastTarget = false;
            speedLines.texture = DivaUi.MakeTexture("Diva Speed Lines", 256, (x, y) =>
            {
                float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
                float streak = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 37 + Mathf.Sin(a * 13) * 2)), 30);
                return new Color(1, 1, 1, streak * Mathf.Clamp01((r - .72f) / .35f) * .8f);
            });
            speedLines.color = new Color(1, 1, 1, 0);
            speedRt.SetAsFirstSibling();

            var right = new Vector2(1, .5f);
            pipFrame = DivaUi.Panel(hudRoot, "Pip Frame", right, new Vector2(-30, -150), new Vector2(510, 300), Ink, Ink, 0);
            pipFrame.stripeWidth = 8;
            var pipRt = DivaUi.Rect(hudRoot, "Pip", right, new Vector2(-45, -150), new Vector2(480, 270));
            pipImage = pipRt.gameObject.AddComponent<RawImage>(); pipImage.raycastTarget = false;
            pipLabel = DivaUi.Text(hudRoot, "Pip Label", right, new Vector2(-60, 0), new Vector2(480, 44), 34, Color.white, TextAlignmentOptions.Right);
            SetPip(false);
        }

        SkillSlot Slot(string name, Vector2 pos, System.Func<string> title, System.Func<string> hint)
        {
            var bottom = new Vector2(.5f, 0);
            var s = new SkillSlot
            {
                plate = DivaUi.Panel(hudRoot, name + " Plate", bottom, pos, new Vector2(240, 104), Ink, DivaUi.WithAlpha(Ink, .4f), 22),
            };
            s.plate.stripeWidth = 8;
            s.name = DivaUi.Text(hudRoot, name + " Name", bottom, pos + new Vector2(6, 52), new Vector2(230, 40), 30, Color.white);
            DivaUi.Localize(s.name, title);
            s.hint = DivaUi.Text(hudRoot, name + " Hint", bottom, pos + new Vector2(4, 28), new Vector2(230, 26), 18, new Color(1, 1, 1, .65f), TextAlignmentOptions.Center, false);
            DivaUi.Localize(s.hint, hint);
            DivaUi.Panel(hudRoot, name + " Bar Back", bottom, pos + new Vector2(0, 10), new Vector2(190, 10), new Color(0, 0, 0, .5f), new Color(0, 0, 0, .5f), 6);
            s.bar = DivaUi.Panel(hudRoot, name + " Bar", new Vector2(.5f, 0), pos + new Vector2(-95, 10), new Vector2(190, 10), Color.white, Color.white, 6);
            s.bar.rectTransform.pivot = new Vector2(0, 0);
            return s;
        }

        static DivaRing Ring(Transform parent, string name, Vector2 anchor, Vector2 pos, float size, float thickness, Color color)
        {
            var rt = DivaUi.Rect(parent, name, anchor, pos, new Vector2(size, size));
            rt.gameObject.AddComponent<CanvasRenderer>();
            var r = rt.gameObject.AddComponent<DivaRing>();
            r.thickness = thickness; r.color = color; r.raycastTarget = false;
            return r;
        }

        void UpdateSlot(SkillSlot s, bool active, float fill, Color color)
        {
            float flash = Mathf.Clamp01(1 - (DivaClock.Time - s.flash) / .6f);
            var top = Color.Lerp(Ink, DivaUi.WithAlpha(color * .7f, .85f), active ? .85f : flash);
            s.plate.Set(top, DivaUi.WithAlpha(top * .6f, top.a * .5f));
            s.plate.stripe = color;
            s.plate.SetVerticesDirty();
            s.bar.color = color;
            s.bar.rectTransform.sizeDelta = new Vector2(190 * Mathf.Clamp01(fill), 10);
            s.plate.rectTransform.localScale = Vector3.one * (1 + .08f * flash);
        }

        void UpdateHud()
        {
            if (!hudRoot || !hudRoot.gameObject.activeSelf) return;
            scoreText.text = TotalScore.ToString();
            scoreLabel.color = Accent;
            float t = RunTime;
            timeText.text = timeLimit > 0 ? DivaText.T("hud.left") + "  " + DivaUi.Clock(timeLimit - t) : DivaText.T("hud.time") + "  " + DivaUi.Clock(t);
            timeText.color = timeLimit > 0 && timeLimit - t < 10 ? new Color(1, .4f, .4f) : Color.white;
            var tasks = game ? game.tasks : new CourseTask[0];
            for (int i = 0; i < taskRows.Count && i < tasks.Length; i++)
            {
                bool done = tasks[i].Done;
                taskRows[i].mark.text = done ? "<color=#7CFC9A>●</color>" : "<alpha=#88>○";
                taskRows[i].label.text = DivaText.Task(tasks[i].label) + (done ? $"  <alpha=#88>{tasks[i].doneAt:0.0}s" : "");
                taskRows[i].label.color = done ? new Color(.75f, 1, .8f) : Color.white;
            }

            float speed = locomotion ? Mathf.Abs(locomotion.CurrentSpeed) : 0;
            float run = locomotion ? Mathf.Max(.1f, locomotion.runSpeed) : 4.5f;
            UpdateSlot(boostSlot, boosting, Mathf.Clamp01(speed / run), Primary);
            UpdateSlot(cannonSlot, demo.State.Shoot, demo.State.Water, new Color(.35f, .8f, 1f));
            UpdateSlot(bubbleSlot, demo.State.Drink, bubbleCooldown, new Color(.85f, .6f, 1f));
            ultRing.SetFill(charge / 100f);
            bool ready = charge >= 100;
            float pulse = .5f + .5f * Mathf.Sin(DivaClock.Time * 8);
            ultRing.color = ready ? Color.Lerp(Accent, Color.white, pulse * .6f) : Accent;
            ultBack.color = ready ? DivaUi.WithAlpha(Accent * .5f, .5f + .3f * pulse) : new Color(0, 0, 0, .55f);
            ultPercent.text = ready ? "<size=70%>100</size>" : Mathf.FloorToInt(charge) + "<size=50%>%</size>";
            ultPercent.color = ready ? Color.white : new Color(1, 1, 1, .85f);
            ultPrompt.gameObject.SetActive(ready && Phase == ShowPhase.Play);
            ultPromptPlate.gameObject.SetActive(ultPrompt.gameObject.activeSelf);
            if (ready) { ultPrompt.alpha = .75f + .25f * pulse; ultPrompt.color = Color.Lerp(Accent, Color.white, .55f); ultPromptPlate.stripe = Accent; ultPromptPlate.stripeWidth = 8; ultPromptPlate.SetVerticesDirty(); }

            // 锁定标记：水枪会打的那个靶子（和 DivaLaserBlaster.FindTarget 同一个判定）
            var target = Phase == ShowPhase.Play && laser ? laser.FindTarget(demo.State.Aim * demo.trunkAimDegrees) : null;
            bool show = target && ScreenPoint(target.AimPoint, out var p);
            lockMarker.gameObject.SetActive(show);
            if (show && ScreenPoint(target.AimPoint, out p))
            {
                lockMarker.anchoredPosition = p;
                lockMarker.localRotation = Quaternion.Euler(0, 0, DivaClock.Time * 90);
                lockRing.SetFill(.85f);
                lockRing.color = demo.State.Shoot ? new Color(1, .35f, .35f) : DivaUi.WithAlpha(Color.white, .8f);
                lockMarker.localScale = Vector3.one * (demo.State.Shoot ? .85f : 1);
            }

            float comboAge = DivaClock.Time - lastHitTime;
            bool comboOn = combo >= 2 && comboAge < ComboWindow;
            comboText.gameObject.SetActive(comboOn); comboLabel.gameObject.SetActive(comboOn);
            if (comboOn)
            {
                comboText.text = "x" + combo;
                comboText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1, DivaUi.EaseOut(comboAge / .25f));
                comboText.color = Color.Lerp(Color.white, Accent, .3f);
                comboLabel.text = DivaText.Chinese ? "连击" : "COMBO";
                comboLabel.alpha = comboText.alpha = 1 - DivaUi.Smooth((comboAge - ComboWindow + .5f) / .5f);
            }

            // 速度线只在推进冲刺时出现（一直跑的时候不挡画面）
            float lines = boosting ? Mathf.Clamp01((speed / run - .75f) / .25f) : 0;
            speedLines.color = new Color(1, 1, 1, Mathf.Lerp(speedLines.color.a, lines * .2f, 1 - Mathf.Exp(-DivaClock.DeltaTime * 4)));
            speedLines.uvRect = new Rect(Random.Range(-.01f, .01f), Random.Range(-.01f, .01f), 1, 1);
            fovKick = Mathf.Lerp(fovKick, boosting ? 8 : 0, 1 - Mathf.Exp(-DivaClock.DeltaTime * 4));

            UpdateFeed();
            UpdatePopups();
        }

        bool ScreenPoint(Vector3 world, out Vector2 anchored)
        {
            anchored = Vector2.zero;
            if (!mainCamera) return false;
            var sp = mainCamera.WorldToScreenPoint(world);
            if (sp.z <= 0) return false;
            // 主镜头的屏幕点换算成界面坐标（截图用的界面摄像机和主镜头分辨率可能不同，按比例换算）
            Camera cam = null;
            if (UiCaptureCamera)
            {
                cam = UiCaptureCamera;
                sp.x *= UiCaptureCamera.pixelWidth / (float)Mathf.Max(1, mainCamera.pixelWidth);
                sp.y *= UiCaptureCamera.pixelHeight / (float)Mathf.Max(1, mainCamera.pixelHeight);
            }
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, sp, cam, out anchored) &&
                   Mathf.Abs(anchored.x) < hudRoot.rect.width * .55f && Mathf.Abs(anchored.y) < hudRoot.rect.height * .55f;
        }

        // ------------------------------------------------------------------ feed (top right)
        void Feed(string text, Color color)
        {
            var right = new Vector2(1, 1);
            var plate = DivaUi.Panel(hudRoot, "Feed", right, new Vector2(-30, -110), new Vector2(560, 46), DivaUi.WithAlpha(Ink, .7f), DivaUi.WithAlpha(Ink, .3f), 14);
            plate.stripe = color; plate.stripeWidth = 6;
            var t = DivaUi.Text(plate.transform, "Text", new Vector2(1, .5f), new Vector2(-18, 0), new Vector2(530, 44), 28, Color.white, TextAlignmentOptions.Right, false);
            t.text = text;
            feed.Insert(0, (t, plate, DivaClock.Time));
            while (feed.Count > 5) { Destroy(feed[feed.Count - 1].plate.gameObject); feed.RemoveAt(feed.Count - 1); }
        }

        void UpdateFeed()
        {
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                var (t, plate, born) = feed[i];
                float age = DivaClock.Time - born;
                if (age > 5 || !plate) { if (plate) Destroy(plate.gameObject); feed.RemoveAt(i); continue; }
                var rt = plate.rectTransform;
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(-30, -110 - 52 * i), 1 - Mathf.Exp(-DivaClock.DeltaTime * 12));
                float a = Mathf.Clamp01(age / .15f) * (1 - DivaUi.Smooth((age - 4.4f) / .6f));
                plate.color = DivaUi.WithAlpha(plate.color, .7f * a); t.alpha = a;
                rt.localScale = new Vector3(Mathf.Lerp(.6f, 1, DivaUi.EaseOut(age / .2f)), 1, 1);
            }
        }

        // ------------------------------------------------------------------ "+10" popups
        void Popup(string text, Vector3 world, Color color)
        {
            var t = DivaUi.Text(hudRoot, "Popup", new Vector2(.5f, .5f), Vector2.zero, new Vector2(300, 80), 60, color);
            t.text = text;
            DivaUi.Glow(t, new Color(0, 0, 0, .6f), .4f, .2f, .15f);
            popups.Add((t, world, DivaClock.Time));
        }

        void UpdatePopups()
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                var (t, world, born) = popups[i];
                float age = DivaClock.Time - born;
                if (age > 1.1f || !t) { if (t) Destroy(t.gameObject); popups.RemoveAt(i); continue; }
                if (ScreenPoint(world, out var p))
                {
                    t.rectTransform.anchoredPosition = p + new Vector2(0, 40 + age * 90);
                    t.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1, DivaUi.EaseOut(age / .2f));
                    t.alpha = 1 - DivaUi.Smooth((age - .7f) / .4f);
                }
                else t.alpha = 0;
            }
        }

        // ------------------------------------------------------------------ picture-in-picture close-up
        Camera pipCamera;
        RenderTexture pipTexture;
        float pipUntil = -1;
        string pipShot;

        /// <summary>普通技能用画中画特写（约 1.6 秒），不切走主镜头；大招才全屏。</summary>
        void Pip(string shot, string label, float seconds = 1.6f)
        {
            if (!pipCamera)
            {
                pipTexture = new RenderTexture(480, 270, 24, RenderTextureFormat.ARGB32) { name = "Diva Pip" };
                var go = new GameObject("Diva Pip Camera");
                go.transform.SetParent(transform, false);
                pipCamera = go.AddComponent<Camera>();
                pipCamera.targetTexture = pipTexture;
                pipCamera.fieldOfView = 34; pipCamera.nearClipPlane = .05f; pipCamera.farClipPlane = 300;
                if (mainCamera) { pipCamera.cullingMask = mainCamera.cullingMask; pipCamera.clearFlags = mainCamera.clearFlags; pipCamera.backgroundColor = mainCamera.backgroundColor; }
                pipCamera.enabled = false;
                pipImage.texture = pipTexture;
            }
            pipShot = shot;
            pipUntil = DivaClock.Time + seconds;
            pipLabel.text = label;
            DivaUi.Glow(pipLabel, DivaUi.WithAlpha(Accent, .9f), .6f, .3f, .1f);
            pipFrame.stripe = Accent;
            SetPip(true);
        }

        // 特写镜头被路边道具挡住时：先换到另一边，再试升高，最后才拉近
        Vector3 PipUnblocked(Vector3 look, Vector3 from, Vector3 right)
        {
            float d = Vector3.Distance(look, from);
            bool Clear(Vector3 p) => ClearDistance(look, p, .25f) >= Vector3.Distance(look, p) - .01f;
            if (Clear(from)) return from;
            var mirrored = look + Vector3.Reflect(from - look, right.normalized);
            if (Clear(mirrored)) return mirrored;
            foreach (var p in new[] { from + Vector3.up * 1.5f, mirrored + Vector3.up * 1.5f })
                if (Clear(p)) return p;
            return look + (from - look) / d * Mathf.Max(1.2f, ClearDistance(look, from, .25f));
        }

        void SetPip(bool on)
        {
            pipImage.gameObject.SetActive(on); pipFrame.gameObject.SetActive(on); pipLabel.gameObject.SetActive(on);
            if (pipCamera) pipCamera.enabled = on;
        }

        void UpdatePip()
        {
            if (!pipCamera || !pipCamera.enabled) return;
            if (DivaClock.Time > pipUntil || Phase != ShowPhase.Play) { SetPip(false); return; }
            float age = pipUntil - DivaClock.Time;
            var rt = pipImage.rectTransform;
            float slide = DivaUi.EaseOut((DivaClock.Time - (pipUntil - 1.6f)) / .2f);
            rt.anchoredPosition = new Vector2(Mathf.Lerp(500, -45, slide), -150);
            pipFrame.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(500, -30, slide), -150);
            pipLabel.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(500, -60, slide), 0);
            Vector3 from, look;
            var fwd = Forward; var right = Vector3.Cross(Vector3.up, fwd);
            switch (pipShot)
            {
                case "boost":
                    look = boosters && boosters.cores.Length > 0 && boosters.cores[0] ? boosters.cores[0].transform.position : ElephantCenter - fwd;
                    from = look - fwd * 3.2f + right * 2.2f + Vector3.up * .9f; break;
                case "bubble":
                    look = bubbles && bubbles.bubbles.Length > 0 && bubbles.bubbles[0] ? bubbles.bubbles[0].transform.position : ElephantCenter;
                    from = look + right * 3f + fwd * 1.5f + Vector3.up * .6f; break;
                default:
                    look = demo.trunkTip ? demo.trunkTip.position : ElephantCenter + fwd * 2;
                    from = look + fwd * 2.6f + right * 1.6f + Vector3.up * .5f;
                    look = Vector3.Lerp(look, ElephantCenter + fwd * 1.5f, .35f); break;
            }
            from += right * Mathf.Sin(age * .8f) * .4f;
            from = PipUnblocked(look, from, right);
            pipCamera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(look - from, Vector3.up));
        }
    }
}
