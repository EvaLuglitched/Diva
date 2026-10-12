using UnityEngine;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// 操纵杆：中控台上一根看得见的杆（右手边、腿前）。伸手到杆头，捏住或握住都算抓住；握着往前推 = 走（3 厘米起步、12 厘米跑满），
    /// 左右推 = 转弯（3 厘米死区、10 厘米转满）；松手 = 停。手靠近时杆头发亮；倒数时杆会挪到手边（25 厘米内）。
    /// 头不参与（前倾那套把头当油门，头一动画面就动，最容易晕）。
    /// </summary>
    public class DivaVRLever : MonoBehaviour
    {
        [Tooltip("指尖离掌心小于这个距离算握拳 / 大于这个距离算松开（米）。")]
        public float fistClose = .06f, fistOpen = .085f;
        [Tooltip("往前推多少米开始走、多少米跑满；左右推多少米死区、多少米转满。")]
        public float pushDead = .03f, pushFull = .12f, sideDead = .03f, sideFull = .10f;
        [Tooltip("杆头相对眼睛的默认位置：右、下、前（米）；手离杆头多近能抓住。")]
        public float restSide = .20f, restDrop = .40f, restForward = .34f, grabRadius = .11f;
        [Tooltip("拇指尖和食指尖小于这个距离算捏住。")]
        public float pinchDistance = .025f;

        /// <summary>杆头的休息位置（驾驶舱坐标）。</summary>
        public Vector3 Rest => rest;
        /// <summary>教学演示：没人握的时候让杆按这个偏移倾斜、杆头猛亮（DivaVRGuide 的"怎么玩"用）。</summary>
        [System.NonSerialized] public Vector3 DemoOffset;
        [System.NonSerialized] public bool Highlight;

        public float Go { get; private set; }
        public float Steer { get; private set; }
        /// <summary>正在握杆的手：0 左 1 右，-1 没有。</summary>
        public int Hand { get; private set; } = -1;
        public bool Active => Hand >= 0;

        Transform stick, stickBase, knob;
        Material stickMat, baseMat;
        Vector3 grabLocal;           // 抓住时手在驾驶舱坐标里的位置（相对它推）
        Vector3 rest;                // 杆头休息位置（驾驶舱坐标）
        float adoptAt = -1; bool near;
        DivaVRRig rig;
        float lastSeen = -10, releaseAt = -1;
        Vector3 lastOffset;

        void Start()
        {
            rig = GetComponent<DivaVRRig>();
            float eye = rig ? rig.eyeHeight : .78f;
            rest = new Vector3(restSide, eye - restDrop, restForward);
            Diva.Show.DivaShowDirector.PhaseChanged += p => { if (p == Diva.Show.ShowPhase.Countdown) adoptAt = Time.unscaledTime + 1.5f; };
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow");
            var body = Resources.Load<Material>("DivaVR Cockpit Body");
            if (!glow || !body) return;
            var root = new GameObject("Lever").transform;
            root.SetParent(transform, false);
            baseMat = new Material(glow); Tint(baseMat, new Color(.5f, .95f, 1f) * 1.2f);
            stickMat = new Material(body); Tint(stickMat, new Color(.12f, .1f, .18f));
            stickBase = Prim(PrimitiveType.Cylinder, root, "Base", new Vector3(.07f, .006f, .07f), baseMat);
            stick = Prim(PrimitiveType.Cylinder, root, "Stick", new Vector3(.016f, .05f, .016f), stickMat);
            knob = Prim(PrimitiveType.Sphere, stick, "Knob", new Vector3(1.9f, .6f, 1.9f), baseMat);
            knob.localPosition = new Vector3(0, 1, 0);
            root.localPosition = rest - new Vector3(0, .05f, 0);
            leverRoot = root;
        }
        Transform leverRoot;

        static Transform Prim(PrimitiveType t, Transform parent, string name, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(parent, false); go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }
        static void Tint(Material m, Color c) { m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); }

        /// <summary>手的几个指尖离掌心有多近（米）：越小越像握拳。</summary>
        static float FistAmount(XRHand hand)
        {
            if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm)) return 1;
            float sum = 0; int n = 0;
            foreach (var id in new[] { XRHandJointID.IndexTip, XRHandJointID.MiddleTip, XRHandJointID.RingTip })
                if (hand.GetJoint(id).TryGetPose(out var tip)) { sum += Vector3.Distance(tip.position, palm.position); n++; }
            return n == 0 ? 1 : sum / n;
        }

        /// <summary>捏住（拇指食指）或握拳都算"抓着"。</summary>
        bool Holding(XRHand hand)
        {
            bool pinch = hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var t) && hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var i) && Vector3.Distance(t.position, i.position) < pinchDistance * (Hand >= 0 ? 1.6f : 1f);
            return pinch || FistAmount(hand) < (Hand >= 0 ? fistOpen : fistClose);
        }

        /// <summary>每帧由 DivaVRHands 调用。space：追踪空间；head：主摄像机。</summary>
        public void Read(XRHandSubsystem hands, Transform space, Transform head)
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            var list = new[] { hands?.leftHand, hands?.rightHand };
            // 倒数时：手离杆头 25 厘米内就把杆挪到手边
            if (adoptAt > 0 && now >= adoptAt)
            {
                adoptAt = -1;
                for (int i = 0; i < 2; i++)
                {
                    var h = list[i];
                    if (!h.HasValue || !h.Value.isTracked || !h.Value.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm)) continue;
                    var local = transform.InverseTransformPoint(space.TransformPoint(palm.position));
                    if (Vector3.Distance(local, rest) < .25f) { rest = local; if (leverRoot) leverRoot.localPosition = rest - new Vector3(0, .05f, 0); Debug.Log("DIVA_VR lever moved to the hand " + rest.ToString("F2")); break; }
                }
            }
            // 正在握的那只手：松开（既不捏也不握拳）或追踪丢 0.3 秒以上 → 放开
            if (Hand >= 0)
            {
                var h = list[Hand];
                bool tracked = h.HasValue && h.Value.isTracked;
                if (tracked) lastSeen = now;
                if (tracked && !Holding(h.Value) || now - lastSeen > .3f) Release();
                else if (tracked && h.Value.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm))
                {
                    var local = transform.InverseTransformPoint(space.TransformPoint(palm.position));
                    lastOffset = local - grabLocal;
                }
            }
            // 没在握：哪只手的掌心到了杆头附近并且捏住 / 握住，就抓住
            near = false;
            if (Hand < 0)
                for (int i = 0; i < 2; i++)
                {
                    var h = list[i];
                    if (!h.HasValue || !h.Value.isTracked || !h.Value.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm)) continue;
                    var local = transform.InverseTransformPoint(space.TransformPoint(palm.position));
                    float d = Vector3.Distance(local, rest + new Vector3(0, .02f, 0));
                    if (d < grabRadius * 1.8f) near = true;
                    if (d > grabRadius || !Holding(h.Value)) continue;
                    Hand = i; lastSeen = now;
                    grabLocal = local; lastOffset = Vector3.zero;
                    Debug.Log("DIVA_VR lever grabbed by " + (i == 0 ? "left" : "right"));
                    break;
                }
            // 推杆 → 油门 / 转向（松开后 0.4 秒内缓缓归零）
            float goT = 0, steerT = 0;
            if (Hand >= 0)
            {
                goT = Mathf.Clamp01((lastOffset.z - pushDead) / Mathf.Max(.01f, pushFull - pushDead));
                float sx = Mathf.Abs(lastOffset.x);
                steerT = Mathf.Sign(lastOffset.x) * Mathf.Clamp01((sx - sideDead) / Mathf.Max(.01f, sideFull - sideDead));
            }
            Go = Mathf.MoveTowards(Go, goT, dt * (goT > Go ? 4f : 2.5f));
            Steer = Mathf.MoveTowards(Steer, steerT, dt * 3f);
        }

        // 杆子的样子：底座在休息位置，杆往推的方向倾；手靠近时杆头亮起来，抓住时更亮；演示时按 DemoOffset 倾
        void LateUpdate()
        {
            if (!leverRoot) return;
            float now = Time.unscaledTime;
            var off = Hand >= 0 ? lastOffset : DemoOffset;
            var tilt = new Vector3(off.x, 0, off.z);
            stick.localRotation = Quaternion.Euler(Mathf.Clamp(tilt.z / pushFull, -1, 1) * 28, 0, -Mathf.Clamp(tilt.x / sideFull, -1, 1) * 28);
            stick.localPosition = new Vector3(0, .05f, 0);
            float glowK = Hand >= 0 ? 2.2f + .8f * Go : Highlight ? 2.4f + .7f * Mathf.Sin(now * 7) : near ? 1.8f + .3f * Mathf.Sin(now * 8) : .9f + .2f * Mathf.Sin(now * 2);
            Tint(baseMat, new Color(.5f, .95f, 1f) * glowK);
        }

        void Release()
        {
            if (Hand < 0) return;
            Debug.Log("DIVA_VR lever released");
            Hand = -1; releaseAt = Time.unscaledTime; lastOffset = Vector3.zero;
        }
    }
}
