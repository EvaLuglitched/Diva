using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diva.Show
{
    /// <summary>
    /// 桌面版的帧率守护（目标机器：M1 / M2 Max 的 MacBook Pro，3456×2234 的 Retina 屏，ProMotion 120 Hz）。
    /// · 开垂直同步、不限帧：画面跟着显示器走（120 Hz 的屏就是 120 fps），没有撕裂，帧与帧之间的节奏均匀，这比"越高越好"更丝滑。
    /// · 每 2 秒看一次平均帧时间：跟不上显示器的刷新率就把 URP 的渲染分辩率往下调一档（最低 0.7，画面只是略软一点），
    ///   余量够了再慢慢升回去。改的是内存里的管线资源，退出时还原（编辑器里不会把改动存进工程）。
    /// 头显（DivaVRRig 自己管画质）和后台测试不挂它。
    /// </summary>
    public class DivaShowPerf : MonoBehaviour
    {
        [Tooltip("Lowest render scale the guard may fall back to (1 = native). Smoothness first: 0.6 on a 3456x2234 screen is still sharper than 1080p.")]
        [Range(.5f, 1f)] public float minScale = .6f;
        [Tooltip("Lower the render scale automatically when the frame time misses the display's refresh rate.")]
        public bool adaptive = true;
        [Tooltip("If even the lowest scale cannot hold the display's rate, lock to half of it (a steady 60 on a 120 Hz screen) instead of juddering between 120 and 60.")]
        public bool halfRateFallback = true;

        public float RefreshHz { get; private set; } = 60;
        /// <summary>Average frame time over the last window, in milliseconds.</summary>
        public float AverageMs { get; private set; }
        public float Scale => scale;
        /// <summary>vSync interval in use: 1 = every refresh, 2 = every second refresh (steady half rate).</summary>
        public int VSync { get; private set; } = 1;

        UniversalRenderPipelineAsset urp;
        float baseScale = 1, scale = 1, sum, worst, hold, nextReport;
        int frames, previousVSync, previousTarget, misses, hits;

        void Start()
        {
            previousVSync = QualitySettings.vSyncCount; previousTarget = Application.targetFrameRate;
            QualitySettings.vSyncCount = 1; Application.targetFrameRate = -1;
            var ratio = Screen.currentResolution.refreshRateRatio;
            RefreshHz = ratio.denominator > 0 ? (float)ratio.value : 60;
            if (RefreshHz < 30) RefreshHz = 60;
            urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp) baseScale = scale = urp.renderScale;
            hold = 8;   // 开头着色器还在编译、帧率不准：先不量
            Debug.Log($"DIVA_SHOW perf: vsync on, display {RefreshHz:0} Hz, render scale {scale:0.00}, adaptive {adaptive}");
        }

        void OnDestroy()
        {
            if (urp && Mathf.Abs(urp.renderScale - baseScale) > .001f) urp.renderScale = baseScale;
            QualitySettings.vSyncCount = previousVSync; Application.targetFrameRate = previousTarget;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (hold > 0) { hold -= dt; return; }
            sum += dt; frames++; worst = Mathf.Max(worst, dt);
            if (sum < 2f) return;
            AverageMs = sum / frames * 1000;
            float budget = 1000f * VSync / RefreshHz;
            if (Time.unscaledTime > nextReport) { nextReport = Time.unscaledTime + 10; Debug.Log($"DIVA_SHOW perf: {frames / sum:0} fps ({AverageMs:0.0} ms avg, worst {worst * 1000:0} ms) scale {scale:0.00} vsync {VSync} at {Screen.width}x{Screen.height}"); }
            if (adaptive && urp)
            {
                // 流畅优先：差 8% 就降一档；要升回去得有 30% 的余量，而且是慢慢升
                float next = scale;
                bool miss = AverageMs > budget * 1.08f;
                if (miss) next = Mathf.Max(minScale, scale - .1f);
                else if (AverageMs < budget * .7f) next = Mathf.Min(baseScale, scale + .05f);
                if (Mathf.Abs(next - scale) > .001f)
                {
                    scale = next; urp.renderScale = scale; hold = 3;   // 改分辩率要重建渲染目标，隔 3 秒再量
                    Debug.Log($"DIVA_SHOW perf: {AverageMs:0.0} ms/frame (worst {worst * 1000:0}) vs {budget:0.0} ms budget -> render scale {scale:0.00}");
                }
                // 最低分辩率还是跟不上：锁到显示器的一半刷新率（120 Hz 屏就是稳稳的 60），比在 120 / 60 之间来回抖舒服得多；
                // 之后在满分辩率下余量很大（平均帧时间不到一半预算）再回到全速
                misses = miss && scale <= minScale + .001f ? misses + 1 : 0;
                hits = !miss && scale >= baseScale - .001f && AverageMs < budget * .45f ? hits + 1 : 0;
                if (halfRateFallback && VSync == 1 && misses >= 2) { VSync = 2; QualitySettings.vSyncCount = 2; misses = 0; hold = 3; Debug.Log($"DIVA_SHOW perf: cannot hold {RefreshHz:0} Hz at scale {scale:0.00} -> steady {RefreshHz / 2:0} fps"); }
                else if (VSync == 2 && hits >= 5) { VSync = 1; QualitySettings.vSyncCount = 1; hits = 0; hold = 3; Debug.Log($"DIVA_SHOW perf: headroom -> back to {RefreshHz:0} Hz"); }
            }
            sum = worst = 0; frames = 0;
        }
    }
}
