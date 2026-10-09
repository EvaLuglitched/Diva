using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 界面和过场用的时钟。平时等于 Time.unscaledTime（不受慢动作影响）；录屏时（Unity Recorder 或设置了
    /// Time.captureDeltaTime）按固定的每帧时间走，这样横幅、镜头和过场的速度和录出来的视频一致。
    /// </summary>
    public static class DivaClock
    {
        static int frame = -1;
        static float time, delta;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { frame = -1; time = 0; delta = 0; }

        /// <summary>Advances once per frame (DivaShowDirector calls it first thing every Update).</summary>
        public static void Tick()
        {
            if (UnityEngine.Time.frameCount == frame) return;
            frame = UnityEngine.Time.frameCount;
            delta = UnityEngine.Time.captureDeltaTime > 0 ? UnityEngine.Time.captureDeltaTime : UnityEngine.Time.unscaledDeltaTime;
            time += delta;
        }

        public static float Time { get { Tick(); return time; } }
        public static float DeltaTime { get { Tick(); return delta; } }
    }
}
