using System;
using System.IO;
using System.Linq;
using Diva.Show;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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

        float probeAt = -1;
        string probeName;
        /// <summary>过 delay 秒做一次透视检查（见 AlphaProbe）。</summary>
        public void QueueProbe(string name, float delay) { probeName = name; probeAt = Time.unscaledTime + delay; }

        void LateUpdate()
        {
            if (probeAt > 0 && Time.unscaledTime >= probeAt) { probeAt = -1; AlphaProbe(probeName); }
            if (shotAt > 0 && Time.unscaledTime >= shotAt) { shotAt = -1; Shot(shotName); }
            if (show && show.Phase == ShowPhase.Play && Time.unscaledTime >= next) { next = Time.unscaledTime + 6; Shot("play"); }
        }

        /// <summary>
        /// 透视检查（传送门开场用）：模拟器的截图拍不到 Metal 画的那一层，没法看房间有没有透出来。
        /// 这里用和头显一样的设置（主摄像机此刻的 HDR 和后期处理）画一帧，读每个像素的透明度：
        /// 房间应该是透明的（alpha≈0，真机上露出真实房间），门里的镇子不透明。存一张图：透明的地方画成棋盘格。
        /// </summary>
        void AlphaProbe(string name)
        {
            if (!DivaVR.Head) return;
            var main = DivaVR.Head.GetComponent<Camera>();
            var data = cam.GetUniversalAdditionalCameraData();
            var mainData = main ? main.GetUniversalAdditionalCameraData() : null;
            bool post = data.renderPostProcessing, hdr = cam.allowHDR;
            data.renderPostProcessing = mainData ? mainData.renderPostProcessing : true;
            cam.allowHDR = main ? main.allowHDR : false;
            const int w = 800, h = 450;
            var probe = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Diva VR Alpha Probe" };
            cam.targetTexture = probe;
            cam.transform.SetPositionAndRotation(DivaVR.Head.position, DivaVR.Head.rotation);
            cam.Render();
            cam.targetTexture = rt;
            data.renderPostProcessing = post; cam.allowHDR = hdr;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active; RenderTexture.active = probe;
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            probe.Release(); Destroy(probe);
            var px = tex.GetPixels32();
            int clear = 0, solid = 0; long glow = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.a < 26) { clear++; glow += c.r + c.g + c.b; }
                else if (c.a > 229) solid++;
                // 棋盘格上按透明度叠
                int x = i % w, y = i / w;
                byte board = (byte)(((x / 20 + y / 20) & 1) == 0 ? 70 : 150);
                float a = c.a / 255f;
                px[i] = new Color32((byte)Mathf.Lerp(board, c.r, a), (byte)Mathf.Lerp(board, c.g, a), (byte)Mathf.Lerp(board, c.b, a), 255);
            }
            tex.SetPixels32(px); tex.Apply();
            Debug.Log($"DIVA_VR alpha {name}: transparent {clear * 100f / px.Length:F1}% opaque {solid * 100f / px.Length:F1}% mean rgb in transparent {(clear > 0 ? glow / (3f * clear) : 0):F1} hdr {main && main.allowHDR} post {data.renderPostProcessing}");
            var file = Path.Combine(dir, $"{session}_{++index:000}_{name.ToLowerInvariant()}_alpha.jpg");
            try { File.WriteAllBytes(file, tex.EncodeToJPG(85)); } catch (Exception e) { Debug.LogWarning("DIVA_VR alpha probe write failed: " + e.Message); }
            Destroy(tex);
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
