using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Diva.VR.EditorTools
{
    /// <summary>
    /// 驾驶舱用的三个材质存成资源（Assets/DivaVR/Resources），打包时着色器和变体才会一起进包；
    /// 运行时只是复制一份改颜色。
    /// </summary>
    static class DivaVRMaterials
    {
        const string Dir = "Assets/DivaVR/Resources";

        [MenuItem("Diva/VR/Rebuild Cockpit Materials", false, 220)]
        public static void Rebuild()
        {
            Directory.CreateDirectory(Dir);
            var body = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            body.SetColor("_BaseColor", new Color(.12f, .1f, .18f)); body.SetFloat("_Smoothness", .75f); body.SetFloat("_Metallic", .3f);
            Save(body, "DivaVR Cockpit Body");
            var glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            glow.SetColor("_BaseColor", new Color(2f, .8f, 1.6f));
            Save(glow, "DivaVR Cockpit Glow");
            var vignette = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            vignette.SetColor("_BaseColor", new Color(1, 1, 1, 0));
            vignette.SetFloat("_Surface", 1);
            vignette.SetOverrideTag("RenderType", "Transparent");
            vignette.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            vignette.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            vignette.SetFloat("_ZWrite", 0);
            vignette.SetFloat("_ZTest", (float)CompareFunction.Always);
            vignette.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            vignette.renderQueue = 4000;
            Save(vignette, "DivaVR Vignette");
            AssetDatabase.SaveAssets();
        }

        /// <summary>命令行：-executeMethod Diva.VR.EditorTools.DivaVRMaterials.RebuildFromCommandLine</summary>
        public static void RebuildFromCommandLine() { Rebuild(); EditorApplication.Exit(0); }

        static void Save(Material m, string name)
        {
            var path = $"{Dir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) { existing.CopyPropertiesFromMaterial(m); existing.shader = m.shader; existing.shaderKeywords = m.shaderKeywords; existing.renderQueue = m.renderQueue; EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(m, path);
        }
    }
}
