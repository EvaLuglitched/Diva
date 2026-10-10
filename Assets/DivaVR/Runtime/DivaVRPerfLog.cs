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
        StreamWriter log;
        DivaShowDirector show;
        DivaVRBubbleTour tour;
        System.Reflection.FieldInfo stageField;
        ProfilerRecorder batches, tris, casters;
        readonly FrameTiming[] timing = new FrameTiming[1];
        float next, sinceStart, sum, worst, gpuSum; int frames, slow, verySlow, gpuFrames;
        float target = 90;

        void Start()
        {
            try
            {
                var dir = Path.Combine(Application.persistentDataPath, "DivaPerf");
                Directory.CreateDirectory(dir);
                // 只留最近 6 个
                foreach (var old in Directory.GetFiles(dir, "perf-*.log").OrderByDescending(f => f).Skip(5)) File.Delete(old);
                log = new StreamWriter(Path.Combine(dir, $"perf-{DateTime.Now:MMdd-HHmmss}.log")) { AutoFlush = true };
            }
            catch (Exception e) { Debug.LogWarning("DIVA_VR perf log off: " + e.Message); enabled = false; return; }
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

        void Update()
        {
            if (log == null) return;
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

        void OnDestroy()
        {
            batches.Dispose(); tris.Dispose(); casters.Dispose();
            log?.Dispose(); log = null;
        }
    }
}
