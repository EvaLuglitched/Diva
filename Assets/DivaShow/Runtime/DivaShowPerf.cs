using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diva.Show
{
    /// <summary>
    /// 桌面版的帧率守护（目标机器：M1 / M2 Max 的 MacBook Pro，3456×2234 的 Retina 屏，ProMotion 120 Hz）。
    /// · 开垂直同步、不限帧：画面跟着显示器走（120 Hz 的屏就是 120 fps），没有撕裂，帧与帧之间的节奏均匀，这比"越高越好"更丝滑。
    /// · 每 2 秒看一次平均帧时间：跟不上显示器的刷新率就把 URP 的渲染分辩率往下调一档（最低 minScale），余量够了再慢慢升回去。
    ///   降了一档帧时间却没变好，说明慢在 CPU（摄像头识别在抢处理器），降分辩率只会让画面变软：马上退回去。
    ///   只差一点（比如 110 fps）就留在全速全分辩率；差很多（不到 96 fps）才锁到半速 60，之后每 30 秒试一次能不能回全速。
    ///   改的是内存里的管线资源，退出时还原（编辑器里不会把改动存进工程）。
    /// 头显（DivaVRRig 自己管画质）和后台测试不挂它。
    /// · Eva 2026-10-13：开着摄像头识别时，游戏最多画 cameraFrameCap（60）帧。识别和游戏抢同一块 CPU：游戏跑满 120 Hz 时
    ///   识别从 30 帧掉到约 15 帧，走路就一顿一顿的。60 帧的画面照样流畅，省下来的 CPU 让识别回到 30 帧。
    ///   日志和 F2 都显示识别的帧率（tracking … fps，摄像头每处理一帧发一个包）。
    /// </summary>
    public class DivaShowPerf : MonoBehaviour
    {
        [Tooltip("Lowest render scale the guard may fall back to (1 = native). Smoothness first: 0.6 on a 3456x2234 screen is still sharper than 1080p.")]
        [Range(.5f, 1f)] public float minScale = .6f;
        [Tooltip("Lower the render scale automatically when the frame time misses the display's refresh rate.")]
        public bool adaptive = true;
        [Tooltip("If even the lowest scale cannot hold the display's rate, lock to half of it (a steady 60 on a 120 Hz screen) instead of juddering between 120 and 60.")]
        public bool halfRateFallback = true;
        [Tooltip("While the camera tracking runs, draw at most this many fps (0 = no cap). The game and the tracking share the CPU: at 120 fps the tracking fell from 30 to about 15 fps.")]
        public int cameraFrameCap = 60;

        public float RefreshHz { get; private set; } = 60;
        /// <summary>Average frame time over the last window, in milliseconds.</summary>
        public float AverageMs { get; private set; }
        public float Scale => scale;
        /// <summary>vSync interval in use: 1 = every refresh, 2 = every second refresh (steady half rate).</summary>
        public int VSync { get; private set; } = 1;
        /// <summary>Camera tracking rate (packets from the bridge per second), measured over the last report window.</summary>
        public float TrackingFps { get; private set; }

        DigiPhant.DigiPhantController controller;
        int lastReceived; float lastReceivedAt, nextFind;
        bool cameraCapped;
        /// <summary>Smallest vSync interval allowed now: with the camera tracking on, enough to stay at or below cameraFrameCap.</summary>
        int FloorVSync => cameraFrameCap > 0 && controller && controller.inputMode == DigiPhant.InputMode.Camera
            ? Mathf.Max(1, Mathf.RoundToInt(RefreshHz / cameraFrameCap)) : 1;

        UniversalRenderPipelineAsset urp;
        float baseScale = 1, scale = 1, sum, worst, hold, nextReport;
        int frames, previousVSync, previousTarget, misses;
        // 刚降过一档：降之前的帧时间和分辩率（下一次测量用来判断降分辩率有没有用）
        bool checkingDrop, probing;
        float msBeforeDrop, scaleBeforeDrop, noDropUntil, nextProbe;
        /// <summary>Below 1/1.25 of the display rate (96 fps on 120 Hz) a steady half rate is smoother than judder.</summary>
        const float SevereMiss = 1.25f;

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

        // F2：右上角一行小字（帧率、平均帧时间、渲染分辩率、垂直同步档）——不用看日志就知道现在顺不顺
        bool showStats; GUIStyle statsStyle; float fpsNow;

        void OnGUI()
        {
            if (!showStats) return;
            statsStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1, 1, 1, .9f) } };
            string text = $"{fpsNow:0} fps  ·  {AverageMs:0.0} ms  ·  scale {scale:0.00}  ·  vsync {VSync} ({RefreshHz / VSync:0} Hz)  ·  {Screen.width}x{Screen.height}" +
                          (controller && controller.inputMode == DigiPhant.InputMode.Camera ? $"  ·  tracking {TrackingFps:0} fps" : "");
            var size = statsStyle.CalcSize(new GUIContent(text));
            var r = new Rect(Screen.width - size.x - 24, 8, size.x + 16, size.y + 6);
            GUI.color = new Color(0, 0, 0, .55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 8, r.y + 3, size.x, size.y), text, statsStyle);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame) showStats = !showStats;
            float dt = Time.unscaledDeltaTime;
            fpsNow = Mathf.Lerp(fpsNow, 1 / Mathf.Max(1e-4f, dt), 1 - Mathf.Exp(-dt * 2));
            // The elephant's controls (the show director's, on this same object): keep looking once a second until found.
            if (!controller && Time.unscaledTime >= nextFind)
            {
                nextFind = Time.unscaledTime + 1;
                var director = GetComponent<DivaShowDirector>();
                controller = director && director.controller ? director.controller
                           : FindAnyObjectByType<DigiPhant.DigiPhantController>(FindObjectsInactive.Include);
                if (controller) Debug.Log($"DIVA_SHOW perf: watching {controller.name} (input {controller.inputMode}) for the camera frame cap");
            }
            // Camera tracking on: cap the game so the tracking keeps its CPU (and its 30 fps). Off: the cap goes away.
            int floor = FloorVSync;
            if (VSync < floor)
            {
                VSync = floor; QualitySettings.vSyncCount = floor; cameraCapped = true; hold = 1; probing = false;
                Debug.Log($"DIVA_SHOW perf: camera tracking on -> game capped at {RefreshHz / floor:0} fps so the tracking keeps 30 fps");
            }
            else if (cameraCapped && floor == 1)
            {
                cameraCapped = false; VSync = 1; QualitySettings.vSyncCount = 1; hold = 1;
                Debug.Log($"DIVA_SHOW perf: camera tracking off -> back to {RefreshHz:0} fps");
            }
            if (hold > 0) { hold -= dt; return; }
            worst = Mathf.Max(worst, dt);
            // 单帧超过 100 毫秒的是卡顿（着色器首次编译、切阶段的加载），不是持续的负载：记进 worst，不算进平均，
            // 否则一次 700 毫秒的卡顿就把平均拉过预算，白白降一档分辩率（第一次真机跑就是这样）
            if (dt > .1f) return;
            sum += dt; frames++;
            if (sum < 2f) return;
            AverageMs = sum / frames * 1000;
            float budget = 1000f * VSync / RefreshHz;
            if (controller)
            {
                int received = controller.ReceivedFrames;
                float now = Time.unscaledTime;
                if (received >= lastReceived && now > lastReceivedAt) TrackingFps = (received - lastReceived) / (now - lastReceivedAt);
                lastReceived = received; lastReceivedAt = now;
            }
            if (Time.unscaledTime > nextReport)
            {
                nextReport = Time.unscaledTime + 10;
                Debug.Log($"DIVA_SHOW perf: {frames / sum:0} fps ({AverageMs:0.0} ms avg, worst {worst * 1000:0} ms) scale {scale:0.00} vsync {VSync} at {Screen.width}x{Screen.height}" +
                          (controller && controller.inputMode == DigiPhant.InputMode.Camera ? $", tracking {TrackingFps:0.0} fps" : ""));
            }
            if (adaptive && urp)
            {
                // 在允许的最高帧率上调分辩率：平时是显示器的全速，开着摄像头就是锁好的 60（Carl 2026-10-13：原来只在 vsync 1
                // 时调，锁到 60 以后显卡跟不上也不会降分辩率，120/144 Hz 屏配弱显卡的电脑会一直卡在 40 帧左右）
                if (VSync == FloorVSync) AdaptAtFullRate(budget);
                else if (Time.unscaledTime > nextProbe && FloorVSync == 1)
                {
                    // 半速时帧时间被垂直同步卡在两次刷新上，看不出余量：隔一阵试一次全速，跟得上就留下
                    VSync = 1; QualitySettings.vSyncCount = 1; probing = true; hold = 1;
                }
            }
            sum = worst = 0; frames = 0;
        }

        void AdaptAtFullRate(float budget)
        {
            if (probing)
            {
                probing = false;
                if (AverageMs > budget * SevereMiss) { HalfRate("still cannot hold"); return; }
                Debug.Log($"DIVA_SHOW perf: headroom ({AverageMs:0.0} ms/frame) -> back to {RefreshHz:0} Hz");
            }
            // 降了一档之后帧时间没有变好（不到 4%）：慢在 CPU，不在显卡。退回原来的分辩率，一阵子之内不再因为这个降
            if (checkingDrop)
            {
                checkingDrop = false;
                if (AverageMs > msBeforeDrop * .96f)
                {
                    noDropUntil = Time.unscaledTime + 45;
                    SetScale(scaleBeforeDrop, $"lower resolution did not help ({msBeforeDrop:0.0} -> {AverageMs:0.0} ms/frame, CPU-bound)");
                    return;
                }
            }
            // 流畅优先：差 8% 就降一档；要升回去得有 30% 的余量，而且是慢慢升
            bool miss = AverageMs > budget * 1.08f;
            if (miss && scale > minScale + .001f && Time.unscaledTime >= noDropUntil)
            {
                msBeforeDrop = AverageMs; scaleBeforeDrop = scale; checkingDrop = true;
                SetScale(Mathf.Max(minScale, scale - .1f), $"{AverageMs:0.0} ms/frame (worst {worst * 1000:0}) vs {budget:0.0} ms budget");
                return;
            }
            if (!miss && AverageMs < budget * .7f && scale < baseScale - .001f) { SetScale(Mathf.Min(baseScale, scale + .05f), "headroom"); return; }
            // 分辩率降到底了、或者降了也没用，还差很多：锁到显示器一半的刷新率（120 Hz 屏就是稳稳的 60）。只差一点就留在全速
            bool cannotScale = scale <= minScale + .001f || Time.unscaledTime < noDropUntil;
            misses = AverageMs > budget * SevereMiss && cannotScale ? misses + 1 : 0;
            // 摄像头锁帧时已经是半速，不再往下减半（120 Hz 屏会掉到 30）
            if (halfRateFallback && misses >= 2 && FloorVSync == 1) HalfRate("cannot hold");
        }

        void HalfRate(string why)
        {
            VSync = 2; QualitySettings.vSyncCount = 2; misses = 0; hold = 3; nextProbe = Time.unscaledTime + 30;
            Debug.Log($"DIVA_SHOW perf: {why} {RefreshHz:0} Hz ({AverageMs:0.0} ms/frame at scale {scale:0.00}) -> steady {RefreshHz / 2:0} fps");
        }

        void SetScale(float value, string why)
        {
            scale = value; urp.renderScale = value; hold = 3;   // 改分辩率要重建渲染目标，隔 3 秒再量
            Debug.Log($"DIVA_SHOW perf: {why} -> render scale {value:0.00}");
        }
    }
}
