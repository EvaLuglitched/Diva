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
    /// 大字提示（推杆跑、看着靶子捏、手放嘴边吃喝、水快没了）由 PC / VR 共用的 DivaShowCoach 决定什么时候弹
    /// （CoachTipped 事件），文字用 ".vr" 版本；举手放大招的提示是 VR 自己的。几秒后自己消失。
    /// </summary>
    public class DivaVRCoach : MonoBehaviour
    {
        DivaShowDirector show;
        DivaGameManager game;
        DigiPhantLocomotion loco;
        DivaDemo demo;
        Camera cam;

        Transform pillar, label, arrow, tip;
        Material pillarMat, arrowMat;
        TextMeshProUGUI labelText, labelDist, tipTitle, tipSub, arrowDist;
        CanvasGroup tipGroup;
        CourseTask current;
        float tipUntil, tipShownAt, lastUltTip;
        TextMeshProUGUI labelAction;
        string tipKey;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            game = FindAnyObjectByType<DivaGameManager>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            demo = FindAnyObjectByType<DivaDemo>();
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
            // 靠近时换成该做的动作（大字、闪）
            labelAction = DivaUi.Text((RectTransform)label, "Action", new Vector2(.5f, .5f), new Vector2(0, -70), new Vector2(1100, 90), 72, new Color(1f, .9f, .4f));
            DivaUi.Glow(labelAction, new Color(1f, .6f, .2f, .9f));
            labelAction.gameObject.SetActive(false);
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
            show.CoachTipped += OnCoachTip;
            SetVisible(false);
        }

        void OnDestroy() { DivaShowDirector.PhaseChanged -= OnPhase; if (show) show.CoachTipped -= OnCoachTip; }

        // 共用的教练决定了要提示什么（起跑、靶子在前方、到了吃 / 喝 / 木头、水快没了）；这里只负责显示
        void OnCoachTip(string key, float seconds) => Tip(key, seconds);

        void OnPhase(ShowPhase p)
        {
            if (p == ShowPhase.Ult && tipKey == "vr.coach.ult") tipUntil = 0;   // 大招已经放了，"举双手"的提示就撤
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

        public void TipNow(string key, float seconds) { if (Time.unscaledTime > tipUntil - 1f) Tip(key, seconds); }

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
                // 14 米以内：距离换成要做的动作，闪着；到了圈里写"到了"
                string act = current.check == TaskCheck.CurlTrunk ? ((current.label ?? "").ToLowerInvariant().Contains("drink") ? "coach.drink" : "coach.eat")
                           : current.check == TaskCheck.LiftLegs || current.check == TaskCheck.Pass ? "coach.log" : null;
                bool near = act != null && dist < 14;
                labelAction.gameObject.SetActive(near); labelDist.gameObject.SetActive(!near);
                if (near)
                {
                    labelAction.text = dist < current.radius ? DivaText.T("coach.here") + "  ·  " + DivaText.T(act) : DivaText.T(act);
                    labelAction.color = new Color(1f, .9f, .4f, .7f + .3f * Mathf.Sin(t * 6));
                    label.localScale = Vector3.one * (4.5f / 1200f) * Mathf.Clamp(dist / 25f, .9f, 2.2f);
                }
                // 箭头：相对大象正前方的角度
                float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(root.forward, Vector3.up), to, Vector3.up);
                arrow.localRotation = Quaternion.Euler(0, 0, -angle);
                arrowDist.text = $"{dist:0} m";
                if (arrowMat) { var c = new Color(1f, .5f, .85f, 1) * (1.4f + .15f * Mathf.Sin(t * 3)); arrowMat.color = c; if (arrowMat.HasProperty("_BaseColor")) arrowMat.SetColor("_BaseColor", c); }
            }
            // 大招满了（PC 版用底部的大招提示条；VR 这里提示举手）
            if (show.Phase == ShowPhase.Play && show.Charge >= 100 && t > lastUltTip + 12) { lastUltTip = t; Tip("vr.coach.ult", 5); }
            bool showTip = t < tipUntil && (show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Countdown) && !show.VrCardVisible;
            tipGroup.alpha = Mathf.MoveTowards(tipGroup.alpha, showTip ? 1 : 0, dt * (showTip ? 4 : 2));
            tip.localScale = Vector3.one * (.9f / 1200f) * Mathf.Max(.01f, DivaUi.EaseOutBack((t - tipShownAt) / .45f));
        }
    }
}
