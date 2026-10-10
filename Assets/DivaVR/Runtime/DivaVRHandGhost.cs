using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Diva.VR
{
    /// <summary>
    /// 手的影子：完全沉浸模式下看不到自己的手，捏合、握把、手放嘴边都是在"盲做"。把每只手的 26 个关节画成小光点
    /// （指尖大一点、手掌更大），捏合时拇指和食指尖变亮，握住把手时整只手变青色。
    /// </summary>
    public class DivaVRHandGhost : MonoBehaviour
    {
        XRHandSubsystem hands;
        static readonly List<XRHandSubsystem> found = new List<XRHandSubsystem>();
        readonly Transform[][] dots = new Transform[2][];
        readonly Material[] mats = new Material[2];
        Transform space;
        DivaVRHandlebars bars;
        DivaVRInput input;
        static readonly Color Idle = new Color(1f, .75f, .9f), Grip = new Color(.5f, 1f, .95f), Pinch = new Color(1f, 1f, .6f);

        void Start()
        {
            bars = GetComponent<DivaVRHandlebars>();
            input = GetComponent<DivaVRInput>();
            var glow = Resources.Load<Material>("DivaVR Cockpit Glow");
            if (!glow) { enabled = false; return; }
            for (int h = 0; h < 2; h++)
            {
                mats[h] = new Material(glow); Tint(mats[h], Idle * 1.4f);
                dots[h] = new Transform[XRHandJointID.EndMarker.ToIndex()];
                var root = new GameObject(h == 0 ? "Hand Ghost L" : "Hand Ghost R").transform;
                for (int j = 0; j < dots[h].Length; j++)
                {
                    var id = XRHandJointIDUtility.FromIndex(j);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var c = go.GetComponent<Collider>(); if (c) Destroy(c);
                    go.name = id.ToString(); go.transform.SetParent(root, false);
                    float r = id == XRHandJointID.Palm ? .016f : id.ToString().EndsWith("Tip") ? .011f : id == XRHandJointID.Wrist ? .013f : .007f;
                    go.transform.localScale = Vector3.one * r * 2;
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
            bool pinching = input && input.Current.shoot;
            for (int h = 0; h < 2; h++)
            {
                var hand = h == 0 ? hands?.leftHand : hands?.rightHand;
                bool on = hand.HasValue && hand.Value.isTracked && space;
                var held = bars && (h == 0 ? bars.HeldLeft : bars.HeldRight);
                Tint(mats[h], (held ? Grip : pinching ? Pinch : Idle) * 1.4f);
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
