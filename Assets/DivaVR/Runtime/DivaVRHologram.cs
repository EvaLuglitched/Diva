using System.Collections.Generic;
using System.Linq;
using DigiPhant;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 仪表台上的机甲全息小模型：坐在大象背上看不到自己的涂装，这里放一只缩小的、慢慢转的机甲大象。
    /// 选涂装时在正中间放大（换涂装马上看到），开跑后缩到左边，不挡前方。
    /// 开始时把大象（蒙皮按当时的姿势烘焙成静态网格）和机甲部件复制一份；换涂装时重新拿材质。
    /// </summary>
    public class DivaVRHologram : MonoBehaviour
    {
        [Tooltip("全息模型多高（米）：选涂装时 / 比赛中。")]
        public float selectHeight = .85f, playHeight = .2f;

        DivaShowDirector show;
        DivaMechSkins skins;
        Transform holo, spin, glowRing;
        readonly List<(Renderer source, Renderer copy)> parts = new List<(Renderer, Renderer)>();
        float height = 1, eye;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            skins = FindAnyObjectByType<DivaMechSkins>();
            var loco = FindAnyObjectByType<DigiPhantLocomotion>();
            var rig = GetComponent<DivaVRRig>();
            eye = rig ? rig.eyeHeight : .78f;
            if (!loco || !loco.travelRoot) { enabled = false; return; }
            var root = loco.travelRoot;

            holo = new GameObject("Mech Hologram").transform;
            holo.SetParent(transform, false);
            spin = new GameObject("Spin").transform;
            spin.SetParent(holo, false);
            var model = new GameObject("Model").transform;
            model.SetPositionAndRotation(root.position, root.rotation);
            var bounds = new Bounds(root.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                Mesh mesh;
                if (r is SkinnedMeshRenderer smr)
                {
                    mesh = new Mesh { name = smr.name + " (hologram)" };
                    smr.BakeMesh(mesh, true);
                }
                else if (r.TryGetComponent(out MeshFilter mf) && mf.sharedMesh) mesh = mf.sharedMesh;
                else continue;
                var go = new GameObject(r.name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
                go.transform.localScale = r is SkinnedMeshRenderer ? Vector3.one : r.transform.lossyScale;
                go.transform.SetParent(model, true);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var copy = go.GetComponent<MeshRenderer>();
                copy.sharedMaterials = r.sharedMaterials;
                copy.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                copy.receiveShadows = false;
                parts.Add((r, copy));
                bounds.Encapsulate(r.bounds);
            }
            // 模型放在 Spin 的中心（脚踩在 0 高度），按身高归一成 1 米，之后整体缩放
            model.SetParent(spin, true);
            var foot = root.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
            model.localRotation = Quaternion.Euler(0, 180, 0);   // 正面朝向驾驶员
            model.localPosition = model.localRotation * -foot;
            height = Mathf.Max(.5f, bounds.size.y);
            model.localScale = Vector3.one;
            spin.localScale = Vector3.one / height;

            // 底座的光圈
            if (show && show.glowMaterial)
            {
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(ring.GetComponent<Collider>());
                ring.name = "Pad";
                ring.transform.SetParent(holo, false);
                ring.transform.localScale = new Vector3(.9f, .004f, .9f);
                var mr = ring.GetComponent<MeshRenderer>();
                mr.sharedMaterial = show.glowMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                glowRing = ring.transform;
            }
            if (skins) skins.Changed += OnSkin;
            Place(true);
        }

        void OnDestroy() { if (skins) skins.Changed -= OnSkin; }

        // 涂装换的是真机甲的材质：再拿一遍
        void OnSkin(int index) { foreach (var (source, copy) in parts) if (source && copy) copy.sharedMaterials = source.sharedMaterials; }

        void LateUpdate() => Place(false);

        void Place(bool snap)
        {
            if (!holo) return;
            bool select = show && (show.Phase == ShowPhase.Select || show.Phase == ShowPhase.Results || show.Phase == ShowPhase.Intro);
            // 选涂装：右前方、大（0.62 米高，换涂装一眼看到；中间留给"下一步"指引牌和涂装名字）；比赛：仪表台最左边（左 52°，让开速度屏）、小一点
            // 选涂装：右前方、在教学牌后面一点（不挡牌子上的按钮），比以前大
            var target = select ? new Vector3(1.0f, eye - .4f, 1.7f) : new Vector3(Mathf.Sin(-52 * Mathf.Deg2Rad) * .6f, eye - .37f, Mathf.Cos(-52 * Mathf.Deg2Rad) * .6f);
            float size = select ? selectHeight : playHeight;
            float k = snap ? 1 : 1 - Mathf.Exp(-Time.unscaledDeltaTime * 3);
            holo.localPosition = Vector3.Lerp(holo.localPosition, target, k);
            holo.localScale = Vector3.one * Mathf.Lerp(holo.localScale.x, size, k);
            spin.localRotation = Quaternion.Euler(0, Time.unscaledTime * (select ? 25 : 12), 0);
            if (glowRing) glowRing.localRotation = Quaternion.identity;
        }
    }
}
