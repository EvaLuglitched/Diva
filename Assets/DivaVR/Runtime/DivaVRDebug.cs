using System;
using System.Globalization;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 头显上排查画面问题用的开关（环境变量；从电脑启动时传：xcrun devicectl device process launch --environment-variables '{"DIVA_CUBE":"1"}' …）。
    /// DIVA_CUBE=1 眼前放一个亮色方块；DIVA_POST=0 关后期处理；DIVA_HDR=1 开场不关 HDR；DIVA_NOPORTAL=1 跳过传送门开场；DIVA_RS=0.8 渲染缩放。
    /// </summary>
    public static class DivaVRDebug
    {
        public static bool On(string name) => Environment.GetEnvironmentVariable(name) == "1";

        public static float Float(string name, float fallback) =>
            float.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public static void Apply(Camera cam, Transform rig, float eye)
        {
            if (cam && Environment.GetEnvironmentVariable("DIVA_POST") == "0")
            {
                var data = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
                data.renderPostProcessing = false;
                Debug.Log("DIVA_VR debug: post-processing off");
            }
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
        }
    }
}
