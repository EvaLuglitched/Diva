using System;
using System.IO;
using System.Linq;
using Diva.Show;
using Unity.Profiling;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 头显上一直记帧率（很便宜）：每 5 秒一行写到 app 的 Documents/DivaPerf/perf-时间.log，掉帧（一帧超过 1.5 倍）单独记一行。
    /// 人玩完以后在电脑上取回来看哪里卡（头显合成器跟不上 90 fps 时画面会抖、会闪）：
    /// xcrun devicectl device copy from --device … --domain-type appDataContainer --domain-identifier com.jchen60.divavr --source Documents/DivaPerf --destination …
    /// </summary>
    public class DivaVRPerfLog : MonoBehaviour
    {
        StreamWriter log, console;
        DivaShowDirector show;
        DivaVRBubbleTour tour;
        System.Reflection.FieldInfo stageField;
        ProfilerRecorder batches, tris, casters;
        readonly FrameTiming[] timing = new FrameTiming[1];
        float next, sinceStart, sum, worst, gpuSum; int frames, slow, verySlow, gpuFrames;
        float target = 90;

        void Awake()
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "DivaPerf");
                Directory.CreateDirectory(dir);
                // 只留最近 6 个
                foreach (var old in Directory.GetFiles(dir, "perf-*.log").OrderByDescending(f => f).Skip(5)) File.Delete(old);
                log = new StreamWriter(Path.Combine(dir, $"perf-{DateTime.Now:MMdd-HHmmss}.log")) { AutoFlush = true };
                // 头显上看不到控制台：游戏自己的日志（DIVA 开头）和所有警告、错误也存一份，出问题时取回来看。
                // 从 Awake 就开始记：别的组件在 Start 里的日志才不会丢
                foreach (var old in Directory.GetFiles(dir, "console-*.log").OrderByDescending(f => f).Skip(5)) File.Delete(old);
                console = new StreamWriter(Path.Combine(dir, $"console-{DateTime.Now:MMdd-HHmmss}.log")) { AutoFlush = true };
                Application.logMessageReceived += OnLog;
                console.WriteLine($"# env DIVA_AUTOPLAY={Env("DIVA_AUTOPLAY")} DIVA_CAPTURE={Env("DIVA_CAPTURE")} DIVA_NOPORTAL={Env("DIVA_NOPORTAL")} DIVA_MUTE={Env("DIVA_MUTE")} args {string.Join(" ", Environment.GetCommandLineArgs())}");
            }
            catch (Exception e) { Debug.LogWarning("DIVA_VR perf log off: " + e.Message); enabled = false; }
        }

        static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? "-";

        void Start()
        {
            if (log == null) return;
            show = FindAnyObjectByType<DivaShowDirector>();
            tour = GetComponent<DivaVRBubbleTour>();
            stageField = typeof(DivaVRBubbleTour).GetField("stage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            casters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
            if (Application.targetFrameRate > 0) target = Application.targetFrameRate;
            log.WriteLine($"# Diva VR perf {DateTime.Now:yyyy-MM-dd HH:mm:ss} target {target} fps, {SystemInfo.deviceModel}, {SystemInfo.graphicsDeviceName}");
            log.WriteLine("# t  phase/stage  fps  avg_ms  worst_ms  slow(>1.5x)  very_slow(>3x)  gpu_ms  batches  tris_k  shadow_casters");
            next = Time.unscaledTime + 5;
        }

        string Where()
        {
            string s = show ? show.Phase.ToString() : "-";
            if (tour && tour.enabled && stageField != null) { var st = stageField.GetValue(tour)?.ToString(); if (st != null && st != "Done") s += "/" + st; }
            return s;
        }

        bool dumped;
        /// <summary>选涂装 3 秒后把眼前该有的东西的状态倒一遍：驾驶舱、指引牌、名字牌到底在不在、有没有被摄像机画到。</summary>
        void DumpScene()
        {
            if (console == null) return;
            var head = DivaVR.Head; var cam = head ? head.GetComponent<Camera>() : Camera.main;
            console.WriteLine($"# DUMP t={Time.unscaledTime:F1} head {(head ? head.position.ToString("F2") : "-")} cam {(cam ? cam.name : "-")} mask {(cam ? cam.cullingMask : 0)} enabled {(cam && cam.enabled)} target {(cam && cam.targetTexture ? cam.targetTexture.name : "screen")} stereo {(cam ? cam.stereoEnabled.ToString() : "-")} rig {(DivaVR.Rig ? DivaVR.Rig.position.ToString("F2") : "-")} arrival {DivaVR.ArrivalDone}");
            var displays = new System.Collections.Generic.List<UnityEngine.XR.XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays) console.WriteLine($"# DUMP xr display running {d.running} passes {d.GetRenderPassCount()} scale {UnityEngine.XR.XRSettings.eyeTextureResolutionScale} eye {UnityEngine.XR.XRSettings.eyeTextureWidth}x{UnityEngine.XR.XRSettings.eyeTextureHeight} fov {d.foveatedRenderingLevel}");
            if (DivaVR.Rig)
                foreach (Transform c in DivaVR.Rig)
                {
                    var rs = c.GetComponentsInChildren<Renderer>(true);
                    console.WriteLine($"# DUMP rig/{c.name} active {c.gameObject.activeSelf} layer {c.gameObject.layer} pos {c.localPosition:F2} scale {c.localScale.x:F3} renderers {rs.Length} on {rs.Count(r => r.enabled && r.gameObject.activeInHierarchy)} visible {rs.Count(r => r.isVisible)}");
                }
            foreach (var cv in FindObjectsByType<Canvas>(FindObjectsInactive.Include))
            {
                var g = cv.GetComponent<CanvasGroup>();
                console.WriteLine($"# DUMP canvas {cv.name} active {cv.gameObject.activeInHierarchy} enabled {cv.enabled} mode {cv.renderMode} cam {(cv.worldCamera ? cv.worldCamera.name : "-")} order {cv.sortingOrder} layer {cv.gameObject.layer} alpha {(g ? g.alpha : 1):F2} pos {cv.transform.position:F2} scale {cv.transform.lossyScale.x:F4} graphics {cv.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Length}");
            }
            foreach (var b in (DivaVR.Rig ? DivaVR.Rig.GetComponents<Behaviour>() : new Behaviour[0]))
                console.WriteLine($"# DUMP rig component {b.GetType().Name} enabled {b.enabled}");
        }

        void Update()
        {
            if (log == null) return;
            if (!dumped && show && show.Phase == ShowPhase.Select && show.PhaseTime > 3) { dumped = true; DumpScene(); }
            float dt = Time.unscaledDeltaTime;
            sinceStart += dt; sum += dt; frames++; worst = Mathf.Max(worst, dt);
            float budget = 1f / target;
            if (dt > budget * 1.5f) slow++;
            if (dt > budget * 3f) { verySlow++; log.WriteLine($"{sinceStart:F1}  HITCH {dt * 1000:F0} ms at {Where()}"); }
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0) { gpuSum += (float)timing[0].gpuFrameTime; gpuFrames++; }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 5;
            log.WriteLine($"{sinceStart:F1}  {Where()}  {frames / Mathf.Max(.001f, sum):F1}  {sum / Mathf.Max(1, frames) * 1000:F1}  {worst * 1000:F1}  {slow}  {verySlow}  {(gpuFrames > 0 ? gpuSum / gpuFrames : 0):F1}  {batches.LastValue}  {tris.LastValue / 1000}  {casters.LastValue}");
            sum = worst = gpuSum = 0; frames = slow = verySlow = gpuFrames = 0;
        }

        int consoleLines;
        void OnLog(string message, string stack, LogType type)
        {
            if (console == null || consoleLines > 4000) return;
            bool ours = message.StartsWith("DIVA");
            if (!ours && type == LogType.Log) return;
            consoleLines++;
            console.WriteLine($"{Time.unscaledTime:F1} [{type}] {message}");
            if (type == LogType.Exception || type == LogType.Error) console.WriteLine(stack);
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            batches.Dispose(); tris.Dispose(); casters.Dispose();
            log?.Dispose(); log = null;
            console?.Dispose(); console = null;
        }
    }
}
