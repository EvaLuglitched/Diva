using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 头显里镇子的玻璃（Town Glass、Mech Glass…）一直在闪：光滑度 0.9 的玻璃上太阳的高光很小很亮，头稍微一动就在像素之间跳
    /// （高光走样），每扇窗都像在频闪。VR 里把玻璃做"磨砂"一点：光滑度降下来、关掉点状高光，只留环境反射。
    /// 不改资源文件：同一个玻璃材质换成一个运行时副本，所有用它的物体一起换。
    /// </summary>
    public class DivaVRGlass : MonoBehaviour
    {
        public float smoothness = .55f;

        void Start()
        {
            var copies = new Dictionary<Material, Material>();
            int renderers = 0;
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!m || !m.name.Contains("Glass") || !m.HasProperty("_Smoothness")) continue;
                    if (!copies.TryGetValue(m, out var copy))
                    {
                        copy = new Material(m) { name = m.name + " (VR)" };
                        copy.SetFloat("_Smoothness", Mathf.Min(copy.GetFloat("_Smoothness"), smoothness));
                        if (copy.HasProperty("_SpecularHighlights")) { copy.SetFloat("_SpecularHighlights", 0); copy.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
                        copies[m] = copy;
                    }
                    mats[i] = copy; changed = true;
                }
                if (changed) { r.sharedMaterials = mats; renderers++; }
            }
            Debug.Log($"DIVA_VR glass: {copies.Count} materials softened on {renderers} renderers ({string.Join(", ", copies.Keys.Select(m => m.name))})");
        }
    }
}
