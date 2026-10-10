using System.Linq;
using DigiPhant;
using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 比赛中的"教练"：永远知道下一步做什么。
    /// 下一个任务点上立一根光柱，名字和距离浮在上面；仪表台上一个箭头指过去；
    /// 第一次需要某个动作时弹出大字提示（前倾跑、看着靶子捏、手放嘴边喝、举手放大招），几秒后自己消失。
    /// </summary>
    public class DivaVRCoach : MonoBehaviour
    {
        DivaShowDirector show;
        DivaGameManager game;
        DigiPhantLocomotion loco;
        DivaDemo demo;
        DivaVRInput input;
        Camera cam;

        Transform pillar, label, arrow, tip;
        Material pillarMat, arrowMat;
        TextMeshProUGUI labelText, labelDist, tipTitle, tipSub, arrowDist;
        CanvasGroup tipGroup;
        CourseTask current;
        float tipUntil, tipShownAt, lastShootTip, lastDrinkTip, lastUltTip, runTipAt = -1;
        string tipKey;
        bool runTipDone, runShown;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            game = FindAnyObjectByType<DivaGameManager>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            demo = FindAnyObjectByType<DivaDemo>();
            input = GetComponent<DivaVRInput>();
            cam = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            if (!show || game == null || !loco || !loco.travelRoot) { enabled = false; return; }
            var glow = show.glowMaterial ? new Material(show.glowMaterial) : null;
            // 光柱
            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(p.GetComponent<Collider>());
            p.name = "Coach Beacon";
            pillar = p.transform;
            pillar.localScale = new Vector3(1.4f, 7, 1.4f);
            var pr = p.GetComponent<MeshRenderer>();
            pillarMat = glow ? new Material(glow) : null;
            if (pillarMat) { pillarMat.mainTexture = Texture2D.whiteTexture; if (pillarMat.HasProperty("_BaseMap")) pillarMat.SetTexture("_BaseMap", Texture2D.whiteTexture); if (pillarMat.HasProperty("_Cull")) pillarMat.SetFloat("_Cull", 0); }
            pr.sharedMaterial = pillarMat; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // 名字 + 距离
            label = Canvas("Coach Label", new Vector2(1200, 300), 4.5f / 1200f, null, 56);
            var ink = new Color(.06f, .05f, .12f);
            // 不透明底板（线性色彩空间里半透明的深色板会透出很多背景，字看不清）
            DivaUi.Panel((RectTransform)label, "Plate", new Vector2(.5f, .5f), Vector2.zero, new Vector2(1150, 260), ink, new Color(.6f, .6f, .75f, 1), 24).stripeWidth = 8;
            labelText = DivaUi.Text((RectTransform)label, "Text", new Vector2(.5f, .5f), new Vector2(0, 45), new Vector2(1100, 140), 92, Color.white);
            DivaUi.Glow(labelText, new Color(1, .35f, .75f, .9f));
            labelDist = DivaUi.Text((RectTransform)label, "Dist", new Vector2(.5f, .5f), new Vector2(0, -70), new Vector2(1100, 90), 64, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            // 仪表台上的箭头（指向下一站）和距离
            arrow = new GameObject("Coach Arrow", typeof(MeshFilter), typeof(MeshRenderer)).transform;
            arrow.SetParent(DivaVR.DashAnchor ? DivaVR.DashAnchor : transform, false);
            arrow.localPosition = new Vector3(-.28f, .13f, 0);
            var mesh = new Mesh { name = "Coach Arrow" };
            mesh.vertices = new[] { new Vector3(0, .05f, 0), new Vector3(-.03f, -.03f, 0), new Vector3(0, -.015f, 0), new Vector3(.03f, -.03f, 0) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            arrow.GetComponent<MeshFilter>().sharedMesh = mesh;
            arrowMat = glow ? new Material(glow) : null;
            if (arrowMat) { arrowMat.mainTexture = Texture2D.whiteTexture; if (arrowMat.HasProperty("_BaseMap")) arrowMat.SetTexture("_BaseMap", Texture2D.whiteTexture); if (arrowMat.HasProperty("_Cull")) arrowMat.SetFloat("_Cull", 0); }
            var ar = arrow.GetComponent<MeshRenderer>();
            ar.sharedMaterial = arrowMat; ar.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var ad = Canvas("Coach Arrow Dist", new Vector2(400, 80), .12f / 400f, arrow, 57);
            ad.localPosition = new Vector3(0, -.075f, 0); ad.localRotation = Quaternion.identity;
            arrowDist = DivaUi.Text((RectTransform)ad, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(400, 80), 54, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            // 大字提示：眼前偏下一点
            tip = Canvas("Coach Tip", new Vector2(1200, 260), .9f / 1200f, transform, 61);
            tip.localPosition = new Vector3(0, (GetComponent<DivaVRRig>()?.eyeHeight ?? .78f) - .12f, 1.5f);
            tipGroup = tip.gameObject.AddComponent<CanvasGroup>();
            tipGroup.alpha = 0; tipGroup.blocksRaycasts = false;
            DivaUi.Panel((RectTransform)tip, "Plate", new Vector2(.5f, .5f), new Vector2(0, 8), new Vector2(1120, 200), ink, new Color(.6f, .6f, .75f, 1), 22).stripeWidth = 8;
            tipTitle = DivaUi.Text((RectTransform)tip, "Title", new Vector2(.5f, .5f), new Vector2(0, 38), new Vector2(1100, 90), 64, Color.white);
            DivaUi.Glow(tipTitle, new Color(1, .3f, .7f, .9f));
            tipSub = DivaUi.Text((RectTransform)tip, "Sub", new Vector2(.5f, .5f), new Vector2(0, -36), new Vector2(1100, 60), 36, new Color(.55f, 1f, .9f), TextAlignmentOptions.Center, false);
            DivaShowDirector.PhaseChanged += OnPhase;
            SetVisible(false);
        }

        void OnDestroy() => DivaShowDirector.PhaseChanged -= OnPhase;

        void OnPhase(ShowPhase p)
        {
            if (p == ShowPhase.Play && !runShown) { runShown = true; Tip("vr.coach.run", 5); runTipDone = false; runTipAt = Time.unscaledTime + 8; }
            if (p == ShowPhase.Results) runShown = false;
        }

        Transform Canvas(string name, Vector2 size, float scale, Transform parent, int order)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.localScale = Vector3.one * scale;
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace; c.worldCamera = cam; c.sortingOrder = order;
            return rt;
        }

        void SetVisible(bool on)
        {
            if (pillar) pillar.gameObject.SetActive(on);
            if (label) label.gameObject.SetActive(on);
            if (arrow) arrow.gameObject.SetActive(on);
        }

        void Tip(string key, float seconds = 4.5f)
        {
            tipKey = key; tipShownAt = Time.unscaledTime; tipUntil = tipShownAt + seconds;
            tipTitle.text = DivaText.T(key);
            tipSub.text = DivaText.T(key + ".b");
            if (show.Sound != null) show.Sound.Play("tick", .4f);
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool play = show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Ult;
            var root = loco.travelRoot;
            var next = play ? game.tasks.FirstOrDefault(x => x != null && !x.Done && x.landmark) : null;
            if (next != current)
            {
                if (current != null && current.Done && show.Sound != null) show.Sound.PlayAt("combo", current.landmark ? current.landmark.position + Vector3.up * 3 : root.position, .5f);
                current = next;
                if (current != null) labelText.text = DivaText.Task(current.label);
            }
            SetVisible(current != null && show.Phase == ShowPhase.Play);
            if (current != null)
            {
                var at = current.landmark.position;
                var to = Vector3.ProjectOnPlane(at - root.position, Vector3.up);
                float dist = to.magnitude;
                pillar.position = at + Vector3.up * 7;
                float pulse = .8f + .2f * Mathf.Sin(t * 3);
                if (pillarMat) { var c = new Color(1f, .6f, .9f, .3f * pulse); pillarMat.color = c; if (pillarMat.HasProperty("_BaseColor")) pillarMat.SetColor("_BaseColor", c); }
                label.position = at + Vector3.up * 14.5f;
                var face = label.position - cam.transform.position; face.y = 0;
                if (face.sqrMagnitude > 1e-4f) label.rotation = Quaternion.LookRotation(face);
                label.localScale = Vector3.one * (4.5f / 1200f) * Mathf.Clamp(dist / 25f, .6f, 2.2f);
                labelDist.text = $"{dist:0} m";
                // 箭头：相对大象正前方的角度
                float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(root.forward, Vector3.up), to, Vector3.up);
                arrow.localRotation = Quaternion.Euler(0, 0, -angle);
                arrowDist.text = $"{dist:0} m";
                if (arrowMat) { var c = new Color(1f, .5f, .85f, 1) * (1.4f + .15f * Mathf.Sin(t * 3)); arrowMat.color = c; if (arrowMat.HasProperty("_BaseColor")) arrowMat.SetColor("_BaseColor", c); }
                // 到了任务点告诉人这里要做什么：吃 / 喝 = 手放嘴边；跨木头 = 继续前倾走过去；终点只要到达
                if (current.check == TaskCheck.CurlTrunk && dist < current.radius * 1.2f && t > lastDrinkTip + 10)
                {
                    lastDrinkTip = t;
                    var l = (current.label ?? "").ToLowerInvariant();
                    Tip(l.Contains("eat") || l.Contains("feed") ? "vr.coach.eat" : "vr.coach.drink");
                }
                if (current.check == TaskCheck.LiftLegs && dist < current.radius * 1.5f && t > lastDrinkTip + 10) { lastDrinkTip = t; Tip("vr.coach.log"); }
            }
            if (show.Phase == ShowPhase.Play)
            {
                // 开跑：还没动就提示前倾
                if (!runTipDone && runTipAt > 0 && t > runTipAt && loco.CurrentSpeed < .3f) { runTipDone = true; Tip("vr.coach.run", 5); }
                if (loco.CurrentSpeed > 1) runTipDone = true;
                // 前方不远有靶子、没在喷：看着它捏
                if (t > lastShootTip + 14 && !(input && input.Current.shoot))
                {
                    var head = DivaVR.Head ? DivaVR.Head.position : root.position;
                    var fwd = Vector3.ProjectOnPlane(root.forward, Vector3.up);
                    bool near = DivaTarget.All.Any(x => x && Vector3.Distance(x.transform.position, head) < 18 && Vector3.Angle(fwd, Vector3.ProjectOnPlane(x.transform.position - head, Vector3.up)) < 35);
                    if (near) { lastShootTip = t; Tip("vr.coach.shoot"); }
                }
                // 大招满了
                if (show.Charge >= 100 && t > lastUltTip + 12) { lastUltTip = t; Tip("vr.coach.ult", 5); }
            }
            bool showTip = t < tipUntil && (show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Countdown);
            tipGroup.alpha = Mathf.MoveTowards(tipGroup.alpha, showTip ? 1 : 0, dt * (showTip ? 4 : 2));
            tip.localScale = Vector3.one * (.9f / 1200f) * Mathf.Max(.01f, DivaUi.EaseOutBack((t - tipShownAt) / .45f));
        }
    }
}
