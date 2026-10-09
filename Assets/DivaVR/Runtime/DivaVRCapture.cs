using System;
using System.IO;
using System.Linq;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 头显 / 模拟器里自动截图（调试用）：环境变量 DIVA_CAPTURE=1 时打开。每个阶段开始 1.2 秒、比赛中每 6 秒，
    /// 用一个和头同位置的普通摄像机（非 XR）拍一张 1600×900，存到 app 的 Documents/DivaCaptures。
    /// 模拟器：xcrun simctl get_app_container booted com.jchen60.divavr data；
    /// 真机：xcrun devicectl device copy from --domain-type appDataContainer --domain-identifier com.jchen60.divavr …
    /// </summary>
    public class DivaVRCapture : MonoBehaviour
    {
        public static bool Requested => Environment.GetEnvironmentVariable("DIVA_CAPTURE") == "1" || Environment.GetCommandLineArgs().Contains("-divaCapture");

        Camera cam;
        RenderTexture rt;
        Texture2D read;
        string dir;
        int index;
        float next, shotAt = -1;
        string shotName;
        DivaShowDirector show;

        void Start()
        {
            dir = Path.Combine(Application.persistentDataPath, "DivaCaptures");
            Directory.CreateDirectory(dir);
            show = FindAnyObjectByType<DivaShowDirector>();
            DivaShowDirector.PhaseChanged += OnPhase;
            var go = new GameObject("Diva VR Capture Camera");
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = 70; cam.nearClipPlane = .05f; cam.farClipPlane = 1000;
            var main = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            if (main) { cam.cullingMask = main.cullingMask; cam.clearFlags = main.clearFlags; cam.backgroundColor = main.backgroundColor; }
            rt = new RenderTexture(1600, 900, 24) { name = "Diva VR Capture", antiAliasing = 4 };
            cam.targetTexture = rt;
            read = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            Queue("start", 2.5f);
            Debug.Log("DIVA_VR capture → " + dir);
        }

        void OnDestroy() { DivaShowDirector.PhaseChanged -= OnPhase; if (rt) rt.Release(); }

        void OnPhase(ShowPhase p) => Queue(p.ToString(), 1.2f);
        void Queue(string name, float delay) { shotName = name; shotAt = Time.unscaledTime + delay; }

        void LateUpdate()
        {
            if (shotAt > 0 && Time.unscaledTime >= shotAt) { shotAt = -1; Shot(shotName); }
            if (show && show.Phase == ShowPhase.Play && Time.unscaledTime >= next) { next = Time.unscaledTime + 6; Shot("play"); }
        }

        void Shot(string name)
        {
            if (!DivaVR.Head) return;
            cam.transform.SetPositionAndRotation(DivaVR.Head.position, DivaVR.Head.rotation);
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); read.Apply();
            RenderTexture.active = prev;
            var file = Path.Combine(dir, $"{++index:000}_{name}.jpg");
            File.WriteAllBytes(file, read.EncodeToJPG(85));
            Debug.Log("DIVA_VR capture " + file);
        }
    }
}
