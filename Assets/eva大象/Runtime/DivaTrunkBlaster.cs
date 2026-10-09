using UnityEngine;

namespace Diva
{
    /// <summary>
    /// 象鼻水枪：水柱、水雾、落地水花和喷口闪光。触发方式留给项目（例如 MediaPipe 手势）：
    /// 调用 StartSpray / StopSpray / Burst / SetPressure，或在 Inspector 勾选 Spraying 测试。
    /// </summary>
    [DisallowMultipleComponent]
    public class DivaTrunkBlaster : MonoBehaviour
    {
        [Tooltip("Tick to spray (for testing). Scripts can call StartSpray, StopSpray, Burst or SetPressure instead.")]
        public bool spraying;
        [Tooltip("0..1: how hard the water comes out (rate and speed).")]
        [Range(0, 1)] public float pressure = 1;

        [Header("Parts (filled in by Diva > Add D.Va Mech)")]
        public ParticleSystem jet;
        public ParticleSystem mist;
        public Light muzzleLight;

        [Header("Sound")]
        public AudioSource loopSource;
        [Tooltip("Separate source for the start/stop sounds, so the loop volume does not mute them.")]
        public AudioSource fxSource;
        public AudioClip startClip, stopClip;
        [Range(0, 1)] public float volume = .8f;

        [Header("Tuning")]
        [Min(0)] public float jetRate = 220;
        [Min(0)] public float mistRate = 45;
        [Min(0)] public float jetSpeed = 9.5f;
        [Tooltip("Seconds for the water to build up or die down.")]
        [Min(0)] public float rampSeconds = .12f;

        float burstUntil = -1;
        float level, previousLevel;

        public bool IsSpraying => spraying || Time.time < burstUntil;
        public float Level => level;

        public void StartSpray() => spraying = true;

        public void StopSpray()
        {
            spraying = false;
            burstUntil = -1;
        }

        /// <summary>Spray for a fixed time, e.g. one shot per gesture.</summary>
        public void Burst(float seconds = .8f) => burstUntil = Mathf.Max(burstUntil, Time.time + seconds);

        public void SetPressure(float value) => pressure = Mathf.Clamp01(value);

        void Update()
        {
            float target = IsSpraying ? pressure : 0;
            level = Mathf.MoveTowards(level, target, Time.deltaTime / Mathf.Max(rampSeconds, .01f));
            Apply(level, Time.time);
            UpdateSound();
            previousLevel = level;
        }

        void UpdateSound()
        {
            if (!loopSource) return;
            var fx = fxSource ? fxSource : loopSource;
            if (level > 0 && previousLevel <= 0 && startClip) fx.PlayOneShot(startClip, volume);
            if (level <= 0 && previousLevel > 0 && stopClip) fx.PlayOneShot(stopClip, volume * .8f);
            loopSource.volume = volume * level;
            loopSource.pitch = Mathf.Lerp(.85f, 1.1f, pressure);
            if (level > 0 && !loopSource.isPlaying) loopSource.Play();
            else if (level <= 0 && loopSource.isPlaying && previousLevel <= 0) loopSource.Stop();
        }

        void OnDisable()
        {
            level = previousLevel = 0;
            Apply(0, 0);
            if (loopSource) loopSource.Stop();
        }

        /// <summary>Set the effect to a spray level (0..1). Also used by the editor preview.</summary>
        public void Apply(float amount, float time)
        {
            if (jet)
            {
                var emission = jet.emission;
                emission.rateOverTimeMultiplier = jetRate * amount;
                var main = jet.main;
                float speed = jetSpeed * Mathf.Lerp(.55f, 1f, amount);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speed * .92f, speed * 1.08f);
                if (amount > 0 && !jet.isPlaying) jet.Play(true);
            }
            if (mist)
            {
                var emission = mist.emission;
                emission.rateOverTimeMultiplier = mistRate * amount;
            }
            if (muzzleLight)
                muzzleLight.intensity = amount * (1.6f + .6f * Mathf.Sin(time * 37f));
        }
    }
}
