using System;
using System.Linq;
using TMPro;
using UnityEngine;
using Diva.VR;

namespace Diva.Show
{
    /// <summary>
    /// 教练：永远知道下一步做什么。下一个任务点上立一根光柱；界面上一个路标（任务名 + 距离，出了画面就贴在边上指方向，
    /// 靠近了换成该做的动作）；第一次需要某个动作、或到了任务点时弹出大字提示（跑起来、靶子在前方、这里吃 / 喝、
    /// 跨过木头、水快没了），几秒后自己消失。VR 有自己的座舱版（DivaVRCoach）；触发规则共用 CoachTip。
    /// </summary>
    public partial class DivaShowDirector
    {
        RectTransform coachRoot, wayRect;
        DivaSlant tipPlate, wayPlate;
        TextMeshProUGUI tipTitle, tipSub, wayText;
        CanvasGroup tipGroup;
        Transform beacon; Material beaconMat;
        CourseTask coachTask;
        string tipKey = "", wayAct;
        float tipUntil, tipShownAt = -10, lastShootTip = -100, lastNearTip = -100, runTipAt = -1, wayDist;
        bool runTipDone, wayNear;
        Vector3 wayAt;

        void BuildCoachHud()
        {
            // 大字提示：底部中间，大招提示条的上面
            coachRoot = DivaUi.Rect(hudRoot, "Coach", new Vector2(.5f, 0), new Vector2(0, 340), new Vector2(1000, 78));
            tipGroup = coachRoot.gameObject.AddComponent<CanvasGroup>(); tipGroup.alpha = 0; tipGroup.blocksRaycasts = false;
            tipPlate = DivaUi.Panel(coachRoot, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1000, 78), DivaUi.WithAlpha(Ink, .88f), DivaUi.WithAlpha(Ink, .5f), 24);
            tipPlate.stripeWidth = 8;
            tipTitle = DivaUi.Text(coachRoot, "Title", new Vector2(.5f, .5f), new Vector2(0, 15), new Vector2(960, 40), 34, Color.white);
            tipSub = DivaUi.Text(coachRoot, "Sub", new Vector2(.5f, .5f), new Vector2(0, -17), new Vector2(960, 28), 22, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            // 路标：跟着下一个任务点
            wayRect = DivaUi.Rect(hudRoot, "Waypoint", new Vector2(.5f, .5f), Vector2.zero, new Vector2(560, 46));
            wayPlate = DivaUi.Panel(wayRect, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(560, 46), DivaUi.WithAlpha(Ink, .75f), DivaUi.WithAlpha(Ink, .35f), 14);
            wayPlate.stripeWidth = 6;
            wayText = DivaUi.Text(wayRect, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(540, 44), 26, Color.white);
            wayRect.gameObject.SetActive(false);
        }

        /// <summary>弹出了一条提示（key、秒数）。VR 的座舱教练（DivaVRCoach）接这个事件显示自己的版本。</summary>
        public event Action<string, float> CoachTipped;

        void CoachStart()
        {
            if (!glowMaterial || DivaVR.Active) return;   // VR 的光柱和路标在 DivaVRCoach
            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = p.GetComponent<Collider>(); if (col) Destroy(col);
            p.name = "Diva Coach Beacon"; p.transform.SetParent(transform, true);
            beacon = p.transform; beacon.localScale = new Vector3(1.4f, 7, 1.4f);
            beaconMat = new Material(glowMaterial) { name = "Diva Coach Beacon" };
            beaconMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            if (beaconMat.HasProperty("_Cull")) beaconMat.SetFloat("_Cull", 0);
            var r = p.GetComponent<MeshRenderer>();
            r.sharedMaterial = beaconMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            beacon.gameObject.SetActive(false);
        }

        void CoachStartRun()
        {
            coachTask = null; tipUntil = 0; lastShootTip = lastNearTip = -100; runTipDone = false;
            runTipAt = Time.unscaledTime + 2.5f;
        }

        /// <summary>弹一条教练提示（key 是标题，key + ".b" 是副标题；三人模式有 ".b3" 就用它）。正在显示别的提示时不插队（最后 1 秒除外）。</summary>
        public void CoachTip(string key, float seconds = 4.5f)
        {
            if (Time.unscaledTime < tipUntil - 1f && tipKey != key) return;
            tipKey = key; tipShownAt = Time.unscaledTime; tipUntil = tipShownAt + seconds;
            if (tipTitle)
            {
                tipTitle.text = DivaText.T(key);
                tipSub.text = DivaText.T(!singlePlayer && DivaText.Has(key + ".b3") ? key + ".b3" : key + ".b");
            }
            sound.Play("tick", .4f);
            Debug.Log("DIVA_SHOW coach " + key);
            CoachTipped?.Invoke(key, seconds);
        }

        void CoachUpdate()
        {
            float t = Time.unscaledTime;
            bool play = Phase == ShowPhase.Play;
            var next = play && game ? game.tasks.FirstOrDefault(x => x != null && !x.Done && x.landmark) : null;
            if (next != coachTask) { coachTask = next; lastNearTip = -100; }
            bool showWay = coachTask != null && play;
            if (beacon) beacon.gameObject.SetActive(showWay);
            if (showWay)
            {
                wayAt = coachTask.landmark.position;
                var to = Vector3.ProjectOnPlane(wayAt - elephant.position, Vector3.up);
                wayDist = to.magnitude;
                if (beacon) { beacon.position = wayAt + Vector3.up * 7; Tint(beaconMat, new Color(1f, .6f, .9f, .3f * (.8f + .2f * Mathf.Sin(t * 3)))); }
                // 吃 / 喝 = 竖拇指；跨木头 = 继续跑过去；终点只要到达
                wayAct = coachTask.check == TaskCheck.CurlTrunk ? ((coachTask.label ?? "").ToLowerInvariant().Contains("drink") ? "coach.drink" : "coach.eat")
                       : coachTask.check == TaskCheck.LiftLegs ? "coach.log" : null;
                wayNear = wayAct != null && wayDist < 14;
                // 14 米内每 7 秒提醒一次，到了圈里立刻再提醒
                if (wayAct != null && (wayDist < 14 && t > lastNearTip + 7 || wayDist < coachTask.radius && t > lastNearTip + 3)) { lastNearTip = t; CoachTip(wayAct, 4); }
            }
            if (!play) return;
            // 开跑后还没动：提示跑起来（之后每 7 秒再看一次，直到真的跑起来）
            if (!runTipDone && runTipAt > 0 && t > runTipAt) { runTipAt = t + 7; if (locomotion.CurrentSpeed < .3f) CoachTip("coach.run", 5); }
            if (locomotion.CurrentSpeed > 1) runTipDone = true;
            // 前方不远有靶子、没在喷
            if (t > lastShootTip + 14 && !demo.State.Shoot)
            {
                var fwd = Forward; var from = elephant.position;
                bool target = DivaTarget.All.Any(x => x && x.IsUp && Vector3.Distance(x.AimPoint, from) < 18 &&
                                                      Vector3.Angle(fwd, Vector3.ProjectOnPlane(x.AimPoint - from, Vector3.up)) < 35);
                if (target) { lastShootTip = t; CoachTip("coach.shoot"); }
            }
        }

        void UpdateCoachHud()
        {
            if (!coachRoot) return;
            if (DivaVR.Active) { if (coachRoot.gameObject.activeSelf) { coachRoot.gameObject.SetActive(false); wayRect.gameObject.SetActive(false); } return; }
            float t = Time.unscaledTime;
            bool showTip = t < tipUntil && Phase == ShowPhase.Play;
            tipGroup.alpha = Mathf.MoveTowards(tipGroup.alpha, showTip ? 1 : 0, DivaClock.DeltaTime * (showTip ? 4 : 2));
            coachRoot.localScale = Vector3.one * Mathf.Max(.01f, DivaUi.EaseOutBack((t - tipShownAt) / .45f));
            tipPlate.stripe = tipKey == "coach.water" ? new Color(1, .4f, .4f) : Accent; tipPlate.SetVerticesDirty();
            bool way = coachTask != null && Phase == ShowPhase.Play;
            if (wayRect.gameObject.activeSelf != way) wayRect.gameObject.SetActive(way);
            if (!way) return;
            if (!ScreenPointClamped(wayAt + Vector3.up * 3.5f, out var p, out int side)) { wayRect.gameObject.SetActive(false); return; }
            wayRect.anchoredPosition = p;
            string text;
            Color colour;
            if (wayNear)
            {
                text = (wayDist < coachTask.radius ? DivaText.T("coach.here") + "  ·  " : "") + DivaText.T(wayAct);
                colour = new Color(1f, .9f, .4f, .75f + .25f * Mathf.Sin(t * 6));
            }
            else { text = DivaText.Task(coachTask.label) + "  ·  " + $"{wayDist:0} m"; colour = Color.white; }
            if (side < 0) text = "<  " + text; else if (side > 0) text += "  >";
            wayText.text = text; wayText.color = colour;
            wayPlate.stripe = wayNear ? new Color(1f, .9f, .4f) : Accent;
            wayRect.localScale = Vector3.one * (wayNear ? 1.1f : 1);
        }

        /// <summary>世界点换成界面坐标，但不丢掉画面外的点：贴到边上，side 说明它在左（-1）还是右（1）边外面；镜头后面的点也贴到下边。</summary>
        bool ScreenPointClamped(Vector3 world, out Vector2 anchored, out int side)
        {
            side = 0; anchored = Vector2.zero;
            if (!mainCamera) return false;
            var sp = mainCamera.WorldToScreenPoint(world);
            bool behind = sp.z <= 0;
            if (behind) { sp.x = mainCamera.pixelWidth - sp.x; sp.y = 0; }
            Camera cam = null;
            if (UiCaptureCamera)
            {
                cam = UiCaptureCamera;
                sp.x *= UiCaptureCamera.pixelWidth / (float)Mathf.Max(1, mainCamera.pixelWidth);
                sp.y *= UiCaptureCamera.pixelHeight / (float)Mathf.Max(1, mainCamera.pixelHeight);
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hudRoot, sp, cam, out anchored)) return false;
            float w = hudRoot.rect.width * .42f, h = hudRoot.rect.height * .34f;
            if (anchored.x < -w) { anchored.x = -w; side = -1; } else if (anchored.x > w) { anchored.x = w; side = 1; }
            anchored.y = Mathf.Clamp(anchored.y, -h, h);
            if (behind && side == 0) side = anchored.x < 0 ? -1 : 1;
            return true;
        }
    }
}
