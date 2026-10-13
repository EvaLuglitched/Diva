using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// 手的影子：完全沉浸模式下看不到自己的手，捏合、手放嘴边都是在"盲做"。把每只手的 26 个关节画成小光点
    /// （指尖大一点、手掌更大），捏合时变亮黄，喝水时变蓝。
    /// 画法：一只手一次实例化绘制（Graphics.RenderMeshInstanced，26 个小球一条绘制命令）。以前是 52 个独立的小球物体，
    /// 双眼就是一百多次绘制，头显的渲染线程本来就是瓶颈。DIVA_HANDDOTS=spheres 退回旧画法排查。
    /// </summary>
    public class DivaVRHandGhost : MonoBehaviour
    {
        XRHandSubsystem hands;
        static readonly List<XRHandSubsystem> found = new List<XRHandSubsystem>();
        readonly Material[] mats = new Material[2];
        readonly Matrix4x4[][] matrices = { new Matrix4x4[32], new Matrix4x4[32] };
        readonly RenderParams[] renderParams = new RenderParams[2];
        readonly Transform[][] dots = new Transform[2][];   // 旧画法
        float[] radii;
        Mesh sphere;
        bool instanced;
        Transform space;
        DivaVRInput input;
        static readonly Color Idle = new Color(1f, .75f, .9f), Pinch = new Color(1f, 1f, .6f), Drink = new Color(.45f, .7f, 1f);

        void Start()
        {
            input = GetComponent<DivaVRInput>();
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow");
            if (!glow) { enabled = false; return; }
            int joints = XRHandJointID.EndMarker.ToIndex();
            radii = new float[joints];
            for (int j = 0; j < joints; j++)
            {
                var id = XRHandJointIDUtility.FromIndex(j);
                radii[j] = id == XRHandJointID.Palm ? .016f : id.ToString().EndsWith("Tip") ? .011f : id == XRHandJointID.Wrist ? .013f : .007f;
            }
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            for (int h = 0; h < 2; h++)
            {
                mats[h] = new Material(glow) { enableInstancing = true }; Tint(mats[h], Idle * 1.4f);
                renderParams[h] = new RenderParams(mats[h])
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, layer = 0,
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 4),
                };
            }
            instanced = sphere && mats[0].enableInstancing && DivaVRDebug.Get("DIVA_HANDDOTS") != "spheres";
            if (!instanced) BuildSpheres(joints);
            Debug.Log("DIVA_VR hand ghost: " + (instanced ? "instanced" : "sphere objects"));
        }

        // 旧画法：每个关节一个小球物体
        void BuildSpheres(int joints)
        {
            for (int h = 0; h < 2; h++)
            {
                dots[h] = new Transform[joints];
                var root = new GameObject(h == 0 ? "Hand Ghost L" : "Hand Ghost R").transform;
                for (int j = 0; j < joints; j++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var c = go.GetComponent<Collider>(); if (c) Destroy(c);
                    go.name = XRHandJointIDUtility.FromIndex(j).ToString(); go.transform.SetParent(root, false);
                    go.transform.localScale = Vector3.one * radii[j] * 2;
                    var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mats[h]; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                    go.SetActive(false);
                    dots[h][j] = go.transform;
                }
            }
        }

        static void Tint(Material m, Color c) { m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); }

        void LateUpdate()
        {
            if (hands == null || !hands.running) { SubsystemManager.GetSubsystems(found); hands = found.Count > 0 ? found[0] : null; }
            if (!space) space = DivaVR.Head ? DivaVR.Head.parent : null;
            bool pinching = input && (input.Current.shoot || input.Current.pinch), drinking = input && input.Current.drink;
            var tint = (drinking ? Drink : pinching ? Pinch : Idle) * (drinking ? 2.2f : 1.4f);
            for (int h = 0; h < 2; h++)
            {
                if (mats[h].color != tint) Tint(mats[h], tint);
                bool on = hands != null && space;
                var hand = h == 0 ? hands?.leftHand : hands?.rightHand;
                on &= hand.HasValue && hand.Value.isTracked;
                if (instanced)
                {
                    if (!on) continue;
                    int count = 0; var centre = Vector3.zero;
                    var m = matrices[h];
                    for (int j = 0; j < radii.Length && count < m.Length; j++)
                        if (hand.Value.GetJoint(XRHandJointIDUtility.FromIndex(j)).TryGetPose(out var pose))
                        {
                            var p = space.TransformPoint(pose.position);
                            m[count++] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * radii[j] * 2);
                            centre += p;
                        }
                    if (count == 0) continue;
                    var rp = renderParams[h];
                    rp.worldBounds = new Bounds(centre / count, Vector3.one * 1.5f);
                    renderParams[h] = rp;
                    Graphics.RenderMeshInstanced(rp, sphere, 0, m, count);
                }
                else if (dots[h] != null)
                    for (int j = 0; j < dots[h].Length; j++)
                    {
                        var d = dots[h][j];
                        Pose pose = default;
                        bool show = on && hand.Value.GetJoint(XRHandJointIDUtility.FromIndex(j)).TryGetPose(out pose);
                        if (d.gameObject.activeSelf != show) d.gameObject.SetActive(show);
                        if (show) d.position = space.TransformPoint(pose.position);
                    }
            }
        }
    }
}
