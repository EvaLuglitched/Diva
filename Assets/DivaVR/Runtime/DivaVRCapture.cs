using System;
using System.IO;
using System.Linq;
using Diva.Show;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

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
        // 头显上也可以不靠启动参数：往 app 的 Documents 里放一个叫 diva_capture 的文件（xcrun devicectl device copy to …），人自己打开 app 也会截图
        public static bool Requested => DivaVRDebug.On("DIVA_CAPTURE") || Environment.GetCommandLineArgs().Contains("-divaCapture")
                                        || File.Exists(Path.Combine(Application.persistentDataPath, "diva_capture"));

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
            RenderPipelineManager.endContextRendering += CopyXr;
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        }

        void OnDisable() { batches.Dispose(); setPass.Dispose(); drawCalls.Dispose(); tris.Dispose(); casters.Dispose(); RenderPipelineManager.endContextRendering -= CopyXr; }

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
            SaveXr();
            if (probeAt > 0 && Time.unscaledTime >= probeAt) { probeAt = -1; AlphaProbe(probeName); }
            if (shotAt > 0 && Time.unscaledTime >= shotAt) { shotAt = -1; Shot(shotName); }
            if (show && (show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Select) && Time.unscaledTime >= next) { next = Time.unscaledTime + 6; Shot(show.Phase.ToString()); }
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
            try { ShotInner(name); }
            catch (Exception e) { Debug.LogWarning($"DIVA_VR capture {name} failed: {e}"); }
        }

        void ShotInner(string name)
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
            // 同时拷一份 XR 真正画出来的左眼
            xrWant = $"{session}_{index:000}_{name.ToLowerInvariant()}_xr";
        }

        // ------------------------------------------------------------------ XR 画面
        // 上面的截图是另一台普通摄像机（单眼、不走 XR）拍的：头显里双眼渲染时有没有画出某样东西（比如 UI），它看不出来。
        // 这里在一帧渲染完的时候把 XR 渲染目标的第 0 层（左眼）原样拷出来存成图。头显上有注视点渲染时，图的边缘会被压缩。
        string xrWant, xrRead; int xrReadFrame;
        RenderTexture xrRt, xrRt2;
        readonly System.Collections.Generic.List<XRDisplaySubsystem> displays = new System.Collections.Generic.List<XRDisplaySubsystem>();

        void CopyXr(ScriptableRenderContext ctx, System.Collections.Generic.List<Camera> cams)
        {
            if (xrWant == null) return;
            string name = xrWant; xrWant = null;
            displays.Clear(); SubsystemManager.GetSubsystems(displays);
            var d = displays.FirstOrDefault(x => x.running);
            if (d == null || d.GetRenderPassCount() == 0) { Debug.Log("DIVA_VR xr capture: no XR render pass"); return; }
            d.GetRenderPass(0, out var pass);
            var desc = pass.renderTargetDesc;
            Debug.Log($"DIVA_VR xr pass {desc.width}x{desc.height} {desc.graphicsFormat} {desc.dimension} slices {desc.volumeDepth} msaa {desc.msaaSamples} params {pass.GetRenderParameterCount()} cams {string.Join(",", cams.Select(c => c.name))}");
            try
            {
                if (!xrRt || xrRt.width != desc.width || xrRt.height != desc.height)
                {
                    if (xrRt) { xrRt.Release(); Destroy(xrRt); }
                    // 固定用 8 位 sRGB：头显的目标可能是别的格式、带多重采样，Blit 会转换和解析；CopyTexture 不会
                    xrRt = new RenderTexture(desc.width, desc.height, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB) { name = "Diva XR Copy", dimension = TextureDimension.Tex2D };
                    xrRt.Create();
                }
                var cmd = new CommandBuffer { name = "Diva XR copy" };
                cmd.Blit(pass.renderTarget, xrRt, 0, 0);
                // 右眼（第二层）也拷一份：看一样东西是不是只在一只眼里有
                if (desc.volumeDepth > 1)
                {
                    if (!xrRt2) { xrRt2 = new RenderTexture(xrRt.descriptor) { name = "Diva XR Copy 2" }; xrRt2.Create(); }
                    cmd.Blit(pass.renderTarget, xrRt2, 1, 0);
                }
                ctx.ExecuteCommandBuffer(cmd);
                ctx.Submit();
                cmd.Release();
                xrRead = name; xrReadFrame = Time.frameCount;
            }
            catch (Exception e) { Debug.LogWarning("DIVA_VR xr capture failed: " + e.Message); }
        }

        void SaveXr()
        {
            if (xrRead == null || Time.frameCount <= xrReadFrame || !xrRt) return;
            string name = xrRead; xrRead = null;
            try
            {
                var tex = new Texture2D(xrRt.width, xrRt.height, TextureFormat.RGBA32, false);
                var prev = RenderTexture.active; RenderTexture.active = xrRt;
                tex.ReadPixels(new Rect(0, 0, xrRt.width, xrRt.height), 0, 0); tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(dir, name + ".jpg"), tex.EncodeToJPG(85));
                if (xrRt2)
                {
                    RenderTexture.active = xrRt2;
                    tex.ReadPixels(new Rect(0, 0, xrRt2.width, xrRt2.height), 0, 0); tex.Apply();
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, name + "2.jpg"), tex.EncodeToJPG(85));
                }
                Destroy(tex);
                Debug.Log("DIVA_VR xr capture " + name);
            }
            catch (Exception e) { Debug.LogWarning("DIVA_VR xr capture save failed: " + e.Message); }
        }
    }
}
