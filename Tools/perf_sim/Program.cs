// Drives DivaShowPerf (the real source file, compiled outside Unity against UnityStubs.cs) with simulated frame times
// and checks what it decides: CPU-bound (camera tracking busy), GPU-bound, very heavy, light.
// Run (Unity ships a .NET SDK):
//   "/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" run --project Tools/perf_sim
// Exit code = number of failed checks.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

static class Program
{
    static int failures;

    // work(scale, t) = milliseconds of CPU/GPU work a frame needs; vsync turns it into the frame period
    static (float scale, int vsync, float avgFps, float fullScaleShare, List<string> log) Run(Func<float, float, float> work, float seconds, bool camera = false)
    {
        // camera = the camera tracking is on (v1.3: the game is capped at 60 fps on a 120 Hz screen while it runs)
        UnityEngine.Object.Found = camera ? new DigiPhant.DigiPhantController { name = "Elephant", inputMode = DigiPhant.InputMode.Camera } : null;
        var urp = new UniversalRenderPipelineAsset { renderScale = 1 };
        GraphicsSettings.currentRenderPipeline = urp;
        QualitySettings.vSyncCount = 1; Time.unscaledTime = 0;
        var log = new List<string>();
        Debug.Sink = m => log.Add($"{Time.unscaledTime,6:0.0} s  {m}");
        var perf = new Diva.Show.DivaShowPerf();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        typeof(Diva.Show.DivaShowPerf).GetMethod("Start", flags).Invoke(perf, null);
        var update = typeof(Diva.Show.DivaShowPerf).GetMethod("Update", flags);
        var rng = new Random(7);
        double frames = 0, fullScaleTime = 0;
        float interval = 1000f / 120;
        while (Time.unscaledTime < seconds)
        {
            float need = work(urp.renderScale, Time.unscaledTime) * (float)(0.97 + rng.NextDouble() * 0.06);
            // vsync: a frame lasts at least vsync refreshes; past that, the pipelined average follows the work
            float ms = Math.Max(need, interval * QualitySettings.vSyncCount);
            Time.unscaledDeltaTime = ms / 1000f;
            Time.unscaledTime += Time.unscaledDeltaTime;
            update.Invoke(perf, null);
            if (Time.unscaledTime > 15) { frames++; if (urp.renderScale > .999f) fullScaleTime += Time.unscaledDeltaTime; }
        }
        return (urp.renderScale, QualitySettings.vSyncCount, (float)(frames / (seconds - 15)), (float)(fullScaleTime / (seconds - 15)), log);
    }

    static void Check(string what, bool ok) { Console.WriteLine($"   {(ok ? "PASS" : "FAIL")}  {what}"); if (!ok) failures++; }

    static void Show(string title, (float scale, int vsync, float avgFps, float fullScaleShare, List<string> log) r)
    {
        Console.WriteLine($"\n== {title}");
        foreach (var l in r.log) if (!l.Contains("fps (")) Console.WriteLine("   " + l);
        Console.WriteLine($"   end: render scale {r.scale:0.00}, vsync {r.vsync}, {r.avgFps:0} fps after the first 15 s, full resolution {r.fullScaleShare:P0} of the time");
    }

    static void Main(string[] args)
    {
        bool expectNew = args.Length == 0 || args[0] != "old";
        // A. Carl's run: the camera bridge keeps the CPU busy, ~9.2 ms per frame whatever the resolution
        var a = Run((s, t) => 9.2f, 150);
        Show("A. CPU-bound like Carl's round (9.2 ms/frame at any resolution)", a);
        if (expectNew)
        {
            Check("stays at full resolution at the end", a.scale > .999f);
            Check("stays at 120 Hz (no lock to 60)", a.vsync == 1);
            Check("full resolution at least 85% of the time", a.fullScaleShare >= .85f);
            Check("about 108 fps (not 60)", a.avgFps > 100);
        }
        // B. GPU-bound: lower resolution really helps (6 ms CPU + 4.5 ms x scale^2 GPU)
        var b = Run((s, t) => Math.Max(6f, 4.5f + 6f * s * s), 120);
        Show("B. GPU-bound (10.5 ms at full resolution, 8.4 ms at 0.8)", b);
        if (expectNew)
        {
            Check("lowers the resolution to hold 120 Hz", b.scale < .95f && b.scale >= .6f);
            Check("stays at 120 Hz", b.vsync == 1);
            Check("about 120 fps", b.avgFps > 113);
        }
        // C. Very heavy CPU load (12 ms) for 70 s, then light (7 ms)
        var c = Run((s, t) => t < 70 ? 12f : 7f, 160);
        Show("C. Very heavy CPU load (12 ms) for 70 s, then light (7 ms)", c);
        if (expectNew)
        {
            Check("locked to a steady 60 while it could not hold 96+ fps", c.log.Exists(l => l.Contains("steady 60")));
            Check("went back to 120 Hz after the load dropped", c.vsync == 1 && c.log.Exists(l => l.Contains("back to 120")));
            Check("full resolution at the end", c.scale > .999f);
        }
        // D. Light load: nothing should change
        var d = Run((s, t) => 6f, 90);
        Show("D. Light load (6 ms)", d);
        if (expectNew) Check("never touches resolution or vsync", d.scale > .999f && d.vsync == 1 && !d.log.Exists(l => l.Contains("->")));
        // E–G (Carl 2026-10-13): camera tracking on, so the game is capped at 60 fps on the 120 Hz screen (vsync 2)
        var e = Run((s, t) => 6f, 90, camera: true);
        Show("E. Camera on, light load (6 ms)", e);
        if (expectNew)
        {
            Check("capped at 60 fps (vsync 2)", e.vsync == 2 && e.log.Exists(l => l.Contains("capped at 60")));
            Check("full resolution, about 60 fps", e.scale > .999f && e.avgFps > 57 && e.avgFps < 63);
        }
        // F. Camera on, GPU-bound: 18.5 ms at full resolution misses the 16.7 ms of 60 fps; lower resolution helps
        var f = Run((s, t) => Math.Max(6f, 4.5f + 14f * s * s), 120, camera: true);
        Show("F. Camera on, GPU-bound (18.5 ms at full resolution, 13.5 ms at 0.8)", f);
        if (expectNew)
        {
            Check("lowers the resolution to hold 60 fps under the camera cap", f.scale < .95f && f.scale >= .6f);
            Check("stays capped at 60 (vsync 2, never 30)", f.vsync == 2 && !f.log.Exists(l => l.Contains("steady")));
            Check("about 60 fps", f.avgFps > 57);
        }
        // G. Camera on, CPU-bound: 20 ms whatever the resolution
        var g = Run((s, t) => 20f, 120, camera: true);
        Show("G. Camera on, CPU-bound (20 ms at any resolution)", g);
        if (expectNew)
        {
            Check("keeps full resolution (lower resolution does not help)", g.scale > .999f);
            Check("stays at vsync 2 (no drop to 30 fps)", g.vsync == 2);
        }
        Console.WriteLine(expectNew ? $"\n{(failures == 0 ? "ALL PASS" : failures + " FAILED")}" : "\n(old version: behaviour only, no checks)");
        Environment.Exit(failures);
    }
}
