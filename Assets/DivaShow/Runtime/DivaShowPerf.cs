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
        [Tooltip("Lowest render scale the guard may fall back to (1 = native).")]
        [Range(.5f, 1f)] public float minScale = .7f;
        [Tooltip("Lower the render scale automatically when the frame time misses the display's refresh rate.")]
        public bool adaptive = true;

        public float RefreshHz { get; private set; } = 60;
        /// <summary>Average frame time over the last window, in milliseconds.</summary>
        public float AverageMs { get; private set; }
        public float Scale => scale;

        UniversalRenderPipelineAsset urp;
        float baseScale = 1, scale = 1, sum, worst, hold, nextReport;
        int frames, previousVSync, previousTarget;

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
            float budget = 1000f / RefreshHz;
            if (Time.unscaledTime > nextReport) { nextReport = Time.unscaledTime + 10; Debug.Log($"DIVA_SHOW perf: {frames / sum:0} fps ({AverageMs:0.0} ms avg, worst {worst * 1000:0} ms) scale {scale:0.00} at {Screen.width}x{Screen.height}"); }
            if (adaptive && urp)
            {
                float next = scale;
                if (AverageMs > budget * 1.12f) next = Mathf.Max(minScale, scale - .1f);
                else if (AverageMs < budget * .72f) next = Mathf.Min(baseScale, scale + .05f);
                if (Mathf.Abs(next - scale) > .001f)
                {
                    scale = next; urp.renderScale = scale; hold = 3;   // 改分辩率要重建渲染目标，隔 3 秒再量
                    Debug.Log($"DIVA_SHOW perf: {AverageMs:0.0} ms/frame (worst {worst * 1000:0}) vs {budget:0.0} ms budget -> render scale {scale:0.00}");
                }
            }
            sum = worst = 0; frames = 0;
        }
    }
}
