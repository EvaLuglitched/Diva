using UnityEngine;

namespace Diva
{
    /// <summary>
    /// 两门炮当泡泡机用：喷出会飘、会慢慢上浮、落地就破的肥皂泡。触发方式留给项目（例如 MediaPipe 手势）：
    /// 调用 StartBubbles / StopBubbles / Burst，或在 Inspector 勾选 Blowing 测试。
    /// </summary>
    [DisallowMultipleComponent]
    public class DivaBubbleCannons : MonoBehaviour
    {
        [Tooltip("Tick to blow bubbles (for testing). Scripts can call StartBubbles, StopBubbles or Burst instead.")]
        public bool blowing;
        [Tooltip("Bubble systems, one per cannon. Filled in by Diva > Add D.Va Mech.")]
        public ParticleSystem[] bubbles = new ParticleSystem[0];
        [Min(0)] public float bubblesPerSecond = 22;

        [Header("Sound")]
        public AudioSource source;
        public AudioClip blowClip;
        [Range(0, 1)] public float volume = .6f;

        float burstUntil = -1;
        bool wasBlowing;

        public bool IsBlowing => blowing || Time.time < burstUntil;

        public void StartBubbles() => blowing = true;

        public void StopBubbles()
        {
            blowing = false;
            burstUntil = -1;
        }

        /// <summary>Blow bubbles for a fixed time, e.g. one puff per gesture.</summary>
        public void Burst(float seconds = 1.5f) => burstUntil = Mathf.Max(burstUntil, Time.time + seconds);

        void Update()
        {
            bool on = IsBlowing;
            Apply(on ? 1 : 0);
            if (source && blowClip)
            {
                if (on && !wasBlowing) source.PlayOneShot(blowClip, volume);
            }
            wasBlowing = on;
        }

        void OnDisable() => Apply(0);

        /// <summary>Set the bubble output (0..1). Also used by the editor preview.</summary>
        public void Apply(float amount)
        {
            foreach (var ps in bubbles)
            {
                if (!ps) continue;
                var e = ps.emission; e.rateOverTimeMultiplier = bubblesPerSecond * amount;
                if (amount > 0 && !ps.isPlaying) ps.Play(true);
            }
        }
    }
}
