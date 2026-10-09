using System;
using System.IO;
using System.Linq;
using Diva.Show;
using Unity.Profiling;
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
        readonly string session = DateTime.Now.ToString("HHmmss");
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
            // 测试用：DIVA_LANG=zh 时用中文开始（检查 visionOS 上的中文字体）
            if (Environment.GetEnvironmentVariable("DIVA_LANG") == "zh") DivaText.Set(Lang.Zh);
            Debug.Log("DIVA_VR capture → " + dir);
        }

        // 渲染统计（每 5 秒一行）：调头显性能用
        ProfilerRecorder batches, setPass, tris, casters, drawCalls;
        float statsNext, statsTime; int statsFrames; float worst;

        void OnEnable()
        {
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        }

        void OnDisable() { batches.Dispose(); setPass.Dispose(); drawCalls.Dispose(); tris.Dispose(); casters.Dispose(); }

        void Update()
        {
            statsFrames++; statsTime += Time.unscaledDeltaTime; worst = Mathf.Max(worst, Time.unscaledDeltaTime);
            if (Time.unscaledTime < statsNext) return;
            statsNext = Time.unscaledTime + 5;
            Debug.Log($"DIVA_VR stats {(show ? show.Phase.ToString() : "")} fps {statsFrames / Mathf.Max(.001f, statsTime):F1} worst {worst * 1000:F1} ms  batches {batches.LastValue} setpass {setPass.LastValue} draws {drawCalls.LastValue} tris {tris.LastValue / 1000}k shadowCasters {casters.LastValue}");
            statsFrames = 0; statsTime = 0; worst = 0;
        }

        void OnDestroy() { DivaShowDirector.PhaseChanged -= OnPhase; if (rt) rt.Release(); }

        void OnPhase(ShowPhase p) => Queue(p.ToString(), 1.2f);
        /// <summary>过 delay 秒拍一张（传送门开场在关键时刻要拍）。</summary>
        public void Queue(string name, float delay) { shotName = name; shotAt = Time.unscaledTime + delay; }

        static readonly int RoomPreview = Shader.PropertyToID("_DivaRoomPreview");

        void LateUpdate()
        {
            if (shotAt > 0 && Time.unscaledTime >= shotAt) { shotAt = -1; Shot(shotName); }
            if (show && show.Phase == ShowPhase.Play && Time.unscaledTime >= next) { next = Time.unscaledTime + 6; Shot("play"); }
        }

        void Shot(string name)
        {
            if (!DivaVR.Head) return;
            cam.transform.SetPositionAndRotation(DivaVR.Head.position, DivaVR.Head.rotation);
            // 截图里没有透视：开场时真实房间的地方画成带网格的灰色
            var room = Shader.GetGlobalVector(RoomPreview);
            Shader.SetGlobalVector(RoomPreview, new Vector4(.15f, .16f, .2f, 0));
            cam.Render();
            Shader.SetGlobalVector(RoomPreview, room);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); read.Apply();
            RenderTexture.active = prev;
            // 每次启动用时间做前缀、名字小写：不和上次留下的文件撞名（macOS 文件名不分大小写）
            var file = Path.Combine(dir, $"{session}_{++index:000}_{name.ToLowerInvariant()}.jpg");
            try { File.WriteAllBytes(file, read.EncodeToJPG(85)); Debug.Log("DIVA_VR capture " + file); }
            catch (Exception e) { Debug.LogWarning("DIVA_VR capture failed: " + e.Message); }
        }
    }
}
