using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 头显上排查画面问题用的开关（环境变量；从电脑启动时传：xcrun devicectl device process launch --environment-variables '{"DIVA_CUBE":"1"}' …）。
    /// DIVA_CUBE=1 眼前放一个亮色方块；DIVA_POST=0 关后期处理；DIVA_HDR=1 开场不关 HDR；DIVA_NOPORTAL=1 跳过传送门开场；DIVA_RS=0.8 渲染缩放；
    /// DIVA_SHADOWS=0 关阴影；DIVA_MSAA=2 两倍多重采样；
    /// DIVA_FOV=0 关注视点渲染；DIVA_NRP=0 关原生渲染通道；DIVA_MSAA=1 关多重采样；DIVA_CLEAR=1 背景纯洋红（看画面有没有送到头显）。
    /// </summary>
    public static class DivaVRDebug
    {
        static Dictionary<string, string> flags;

        /// <summary>
        /// 开关的值：环境变量，或者 app 的 Documents/diva_flags.txt 里的一行 NAME=VALUE（头显上人自己打开 app 时环境变量传不进去，
        /// 文件可以用 xcrun devicectl device copy to 推进去）。都没有就 null。
        /// </summary>
        public static string Get(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(v)) return v;
            if (flags == null)
            {
                flags = new Dictionary<string, string>();
                try
                {
                    var path = Path.Combine(Application.persistentDataPath, "diva_flags.txt");
                    if (File.Exists(path))
                        foreach (var line in File.ReadAllLines(path))
                        {
                            int i = line.IndexOf('=');
                            if (i > 0) flags[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                        }
                    if (flags.Count > 0) Debug.Log("DIVA_VR flags file: " + string.Join(" ", flags.Select(kv => kv.Key + "=" + kv.Value)));
                }
                catch (Exception e) { Debug.LogWarning("DIVA_VR flags file: " + e.Message); }
            }
            return flags.TryGetValue(name, out var f) ? f : null;
        }

        public static bool On(string name) => Get(name) == "1";

        public static float Float(string name, float fallback) =>
            float.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static void Apply(Camera cam, Transform rig, float eye)
        {
            if (cam && Get("DIVA_POST") == "0")
            {
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                data.renderPostProcessing = false;
                Debug.Log("DIVA_VR debug: post-processing off");
            }
            // 玻璃闪烁排查：DIVA_RIM=0 关轮廓光；DIVA_BLOOM=0 关泛光；DIVA_GLASS=0 玻璃不反射；DIVA_UIPROBE=1 眼前摆几种界面画法做对照
            if (Get("DIVA_RIM") == "0" || Get("DIVA_BLOOM") == "0" || On("DIVA_UIPROBE")) rig.gameObject.AddComponent<DivaVRDebugLate>();
            if (On("DIVA_CUBE"))
            {
                // 摄像机下面（跟着头）：前方 1 米一个洋红色方块，不管人在哪儿都能看到
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Debug Cube";
                cube.transform.SetParent(cam ? cam.transform : rig, false);
                cube.transform.localPosition = new Vector3(0, 0, 1);
                cube.transform.localScale = Vector3.one * .25f;
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                var r = cube.GetComponent<MeshRenderer>();
                if (shader) r.sharedMaterial = new Material(shader) { color = Color.magenta };
                Debug.Log("DIVA_VR debug: cube in front of the head, shader " + (shader ? shader.name : "missing"));
            }
            if (On("DIVA_NOPORTAL")) { DivaVR.ArrivalDone = true; Debug.Log("DIVA_VR debug: portal opening skipped"); }
            if (cam && On("DIVA_CLEAR"))
            {
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(1, 0, 1, 1);
                Debug.Log("DIVA_VR debug: solid magenta background");
            }
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                if (Get("DIVA_MSAA") == "1") { urp.msaaSampleCount = 1; Debug.Log("DIVA_VR debug: MSAA off"); }
                if (Get("DIVA_MSAA") == "2") { urp.msaaSampleCount = 2; Debug.Log("DIVA_VR debug: MSAA 2x"); }
                if (Get("DIVA_SHADOWS") == "0") { urp.shadowDistance = 0; Debug.Log("DIVA_VR debug: shadows off"); }
                if (Get("DIVA_NRP") == "0")
                {
                    var list = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(urp) as UnityEngine.Rendering.Universal.ScriptableRendererData[];
                    if (list != null)
                        foreach (var d in list)
                            if (d is UnityEngine.Rendering.Universal.UniversalRendererData ur) { ur.useNativeRenderPass = false; Debug.Log("DIVA_VR debug: native render pass off on " + ur.name); }
                }
            }
            if (Get("DIVA_FOV") == "0")
            {
                var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
                SubsystemManager.GetSubsystems(displays);
                foreach (var d in displays) { d.foveatedRenderingLevel = 0; d.foveatedRenderingFlags = UnityEngine.XR.XRDisplaySubsystem.FoveatedRenderingFlags.None; }
                Debug.Log("DIVA_VR debug: foveated rendering off on " + displays.Count + " displays");
            }
        }
    }
}
