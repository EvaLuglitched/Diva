using System.Linq;
using DigiPhant;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 远处的小东西不画：镇子里几百个小道具（糖、路灯头、栏杆柱、小旗子）在 45 米外只剩几个像素，却占了一大半三角形和阴影。
    /// 把它们放到一个单独的层，摄像机对这个层按距离剔除，并且不投影。
    /// </summary>
    public class DivaVRCulling : MonoBehaviour
    {
        public const int SmallLayer = 30;
        public float smallSize = 2.2f, cullDistance = 45f;

        void Start()
        {
            var cam = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            var loco = FindAnyObjectByType<DigiPhantLocomotion>();
            if (!cam) return;
            var dist = new float[32];
            dist[SmallLayer] = DivaVRDebug.Float("DIVA_SMALLCULL", cullDistance);
            cam.layerCullDistances = dist;
            cam.layerCullSpherical = true;
            int n = 0;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.layer != 0 || r.bounds.size.magnitude > smallSize) continue;
                if (loco && loco.travelRoot && r.transform.IsChildOf(loco.travelRoot)) continue;
                if (DivaVR.Rig && r.transform.IsChildOf(DivaVR.Rig)) continue;
                r.gameObject.layer = SmallLayer;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                n++;
            }
            Debug.Log($"DIVA_VR culling: {n} small renderers culled beyond {dist[SmallLayer]} m, no shadows");
        }
    }
}
