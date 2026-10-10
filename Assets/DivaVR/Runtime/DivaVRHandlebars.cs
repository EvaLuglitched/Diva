using UnityEngine;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// 体感把手：驾驶舱里两个握把（像摩托车把）。手放到握把上就算握住；
    /// 两手一起往前推 = 走，推得多 = 跑；一手前一手后（把手转动）或两手一起往一边挪 = 转弯；松手大象就停。
    /// 身体前倾不再管用：头的小动作太容易误触，而手放在把手上有实物参照，追踪也稳。
    /// 握把的位置：倒数时两只手在哪儿就把握把挪到哪儿（在默认位置 25 厘米以内才算），所以不同身高、坐姿都够得着。
    /// </summary>
    public class DivaVRHandlebars : MonoBehaviour
    {
        [Tooltip("手离握把多近算握住、多远算松开（米）。")]
        public float grabRadius = .11f, releaseRadius = .18f;
        [Tooltip("往前推多少米开始走、多少米跑满。")]
        public float pushDead = .02f, pushFull = .12f;
        [Tooltip("一手前一手后相差多少米算转满；两手一起往一边挪多少米算转满。")]
        public float twistFull = .14f, slideFull = .18f;
        [Tooltip("握把相对眼睛的默认位置：前方、左右、往下（米）。")]
        public float restForward = .36f, restSide = .17f, restDrop = .42f;

        /// <summary>这一帧的操作（0–1 / -1–1）。</summary>
        public float Go { get; private set; }
        public float Steer { get; private set; }
        /// <summary>握住的手有几只（0–2）；每只手往前推了多少（米），给指引牌的反馈条用。</summary>
        public int Held => (heldL ? 1 : 0) + (heldR ? 1 : 0);
        public float PushL => pushL;
        public float PushR => pushR;

        DivaVRRig rig;
        Transform bar, gripL, gripR;
        Material gripMatL, gripMatR;
        Vector3 restL, restR;   // 驾驶舱坐标里的握把位置
        bool heldL, heldR;
        float pushL, pushR, slide, lastHeldAt = -10, goHold;
        float visPush, visTwist;

        void Start()
        {
            rig = GetComponent<DivaVRRig>();
            float eye = rig ? rig.eyeHeight : .78f;
            restL = new Vector3(-restSide, eye - restDrop, restForward);
            restR = new Vector3(restSide, eye - restDrop, restForward);
            Build();
            Diva.Show.DivaShowDirector.PhaseChanged += OnPhase;
        }

        void OnDestroy() => Diva.Show.DivaShowDirector.PhaseChanged -= OnPhase;

        // 倒数时记下手的位置：握把挪到手上
        void OnPhase(Diva.Show.ShowPhase p) { if (p == Diva.Show.ShowPhase.Countdown) adoptAt = Time.unscaledTime + 1.5f; }
        float adoptAt = -1;

        void Build()
        {
            var root = new GameObject("Handlebars").transform;
            root.SetParent(transform, false);
            var body = Load("DivaVR Cockpit Body", "Universal Render Pipeline/Lit", new Color(.12f, .1f, .18f));
            gripMatL = Load("DivaVR Cockpit Glow", "Universal Render Pipeline/Unlit", new Color(1f, .4f, .8f) * .6f);
            gripMatR = new Material(gripMatL);
            // 杆子从仪表台伸出来，顶端一根横杆，两头是握把；整根杆子绕根部转动（推、转的反馈）
            var stemBase = (restL + restR) * .5f + new Vector3(0, -.1f, .08f);
            bar = new GameObject("Bar").transform;
            bar.SetParent(root, false);
            bar.localPosition = stemBase;
            var stem = Cylinder("Stem", bar, (restL + restR) * .5f - stemBase, .014f, body);
            Cylinder("Cross", bar, restR - restL, .012f, body, (restL + restR) * .5f - stemBase);
            gripL = Cylinder("Grip L", bar, new Vector3(-.11f, 0, 0), .02f, gripMatL, restL - stemBase + new Vector3(.055f, 0, 0)).transform;
            gripR = Cylinder("Grip R", bar, new Vector3(.11f, 0, 0), .02f, gripMatR, restR - stemBase - new Vector3(.055f, 0, 0)).transform;
        }

        /// <summary>一根从 centre 出发、沿 axis 的圆柱（axis 的长度就是圆柱的长度）。</summary>
        static GameObject Cylinder(string name, Transform parent, Vector3 axis, float radius, Material m, Vector3? centre = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = centre ?? axis * .5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            go.transform.localScale = new Vector3(radius * 2, axis.magnitude * .5f, radius * 2);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go;
        }

        static Material Load(string resource, string fallbackShader, Color c)
        {
            var asset = Resources.Load<Material>(resource);
            var m = asset ? new Material(asset) : new Material(Shader.Find(fallbackShader));
            m.SetColor("_BaseColor", c); m.color = c;
            return m;
        }

        /// <summary>每帧由 DivaVRHands 调用：按两只手的位置算出 Go / Steer。space：追踪空间（手的关节在这个空间里）。</summary>
        public void Read(XRHandSubsystem hands, Transform space)
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            bool gotL = Palm(hands?.leftHand, space, out var palmL), gotR = Palm(hands?.rightHand, space, out var palmR);
            // 倒数时握把挪到手上（手在默认位置 25 厘米以内才算，免得手在别处时把握把挪没了）
            if (adoptAt > 0 && now >= adoptAt)
            {
                adoptAt = -1;
                if (gotL && Vector3.Distance(palmL, restL) < .25f) restL = palmL;
                if (gotR && Vector3.Distance(palmR, restR) < .25f) restR = palmR;
                Debug.Log($"DIVA_VR handlebars rest L {restL:F2} R {restR:F2} (hands {gotL}/{gotR})");
            }
            heldL = Hold(gotL, palmL, restL, heldL);
            heldR = Hold(gotR, palmR, restR, heldR);
            pushL = heldL ? palmL.z - restL.z : 0;
            pushR = heldR ? palmR.z - restR.z : 0;
            float go = 0, steer = 0;
            if (heldL && heldR)
            {
                lastHeldAt = now;
                float push = (pushL + pushR) * .5f;
                go = Mathf.Clamp01((push - pushDead) / Mathf.Max(.01f, pushFull - pushDead));
                // 把手转动（左前右后 = 往右转）加上整体往一边挪
                slide = ((palmL.x - restL.x) + (palmR.x - restR.x)) * .5f;
                float twist = (pushL - pushR) / twistFull + slide / slideFull;
                steer = Mathf.Sign(twist) * Mathf.Clamp01((Mathf.Abs(twist) - .12f) / .88f);
                goHold = go;
            }
            else if (now - lastHeldAt < .3f) { go = goHold; }   // 一只手追踪丢了一下：先保持
            Go = go; Steer = steer;
            // 视觉反馈：杆子往前倒、左右转，握住的握把亮起来
            visPush = Mathf.Lerp(visPush, Go, 1 - Mathf.Exp(-dt * 10));
            visTwist = Mathf.Lerp(visTwist, Steer, 1 - Mathf.Exp(-dt * 10));
            if (bar) bar.localRotation = Quaternion.Euler(visPush * 22, visTwist * 18, 0);
            Glow(gripMatL, heldL, now);
            Glow(gripMatR, heldR, now);
        }

        bool Hold(bool tracked, Vector3 palm, Vector3 rest, bool was)
        {
            if (!tracked) return false;
            float d = Vector3.Distance(palm, rest);
            return was ? d < releaseRadius : d < grabRadius;
        }

        /// <summary>手掌中心在驾驶舱坐标里的位置（没追踪到就 false）。</summary>
        bool Palm(XRHand? hand, Transform space, out Vector3 local)
        {
            local = Vector3.zero;
            if (hand == null || !hand.Value.isTracked || !space) return false;
            if (!hand.Value.GetJoint(XRHandJointID.Palm).TryGetPose(out var pose) && !hand.Value.GetJoint(XRHandJointID.Wrist).TryGetPose(out pose)) return false;
            local = transform.InverseTransformPoint(space.TransformPoint(pose.position));
            return true;
        }

        void Glow(Material m, bool held, float t)
        {
            if (!m) return;
            var c = held ? new Color(1f, .5f, .85f) * 2.2f : new Color(1f, .4f, .8f) * (.5f + .2f * Mathf.Sin(t * 2.5f));
            m.SetColor("_BaseColor", c); m.color = c;
        }
    }
}
