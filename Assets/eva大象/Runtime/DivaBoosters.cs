using UnityEngine;

namespace Diva
{
    /// <summary>
    /// 背后两个喷射口的火焰：跟着大象的移动速度变化（站着不喷，走路小火，跑起来大火），
    /// 速度突然变快时再多一点"加速"爆发。也可以用 boostOverride 或 SetBoost 手动控制。
    /// </summary>
    [DisallowMultipleComponent]
    public class DivaBoosters : MonoBehaviour
    {
        [Tooltip("Movement script whose speed drives the flames. Filled in by Diva > Add D.Va Mech.")]
        public DigiPhant.DigiPhantLocomotion locomotion;
        [Tooltip("Core flame and outer glow systems, one pair per thruster.")]
        public ParticleSystem[] cores = new ParticleSystem[0];
        public ParticleSystem[] glows = new ParticleSystem[0];

        [Header("Look")]
        [Min(0)] public float coreRate = 230;
        [Min(0)] public float glowRate = 50;
        [Min(0)] public float flameSpeed = 6;
        [Min(0)] public float flameSize = .5f;
        [Tooltip("Flame level while walking slowly (0..1); running reaches 1.")]
        [Range(0, 1)] public float walkLevel = .35f;
        [Tooltip("Extra flame when the elephant speeds up.")]
        [Range(0, 1)] public float accelerationKick = .5f;

        [Header("Testing")]
        [Tooltip("-1 = follow the elephant's speed; 0..1 = fixed flame level.")]
        [Range(-1, 1)] public float boostOverride = -1;

        float level, previousSpeed, kick;

        /// <summary>Current flame level 0..1 (used by DivaMechAudio for the engine sound).</summary>
        public float Level => level;

        public void SetBoost(float value) => boostOverride = value;

        void Update()
        {
            float target = boostOverride >= 0 ? boostOverride : FromSpeed();
            level = Mathf.MoveTowards(level, Mathf.Clamp01(target + kick), Time.deltaTime * 4);
            kick = Mathf.MoveTowards(kick, 0, Time.deltaTime * 1.5f);
            Apply(level);
        }

        float FromSpeed()
        {
            if (!locomotion || !locomotion.enableLocomotion) return 0;
            float speed = Mathf.Abs(locomotion.CurrentSpeed);
            float dv = speed - previousSpeed;
            previousSpeed = speed;
            if (dv > .02f) kick = Mathf.Max(kick, accelerationKick * Mathf.Clamp01(dv * 20));
            if (speed < .05f) return 0;
            float walk = Mathf.Max(locomotion.walkSpeed, .01f), run = Mathf.Max(locomotion.runSpeed, walk + .01f);
            return speed <= walk ? walkLevel * speed / walk
                                 : Mathf.Lerp(walkLevel, 1, (speed - walk) / (run - walk));
        }

        void OnDisable() => Apply(0);

        /// <summary>Set the flames to a level (0..1). Also used by the editor preview.</summary>
        public void Apply(float amount)
        {
            foreach (var ps in cores)
            {
                if (!ps) continue;
                var e = ps.emission; e.rateOverTimeMultiplier = coreRate * amount;
                var m = ps.main;
                float s = flameSpeed * Mathf.Lerp(.45f, 1.2f, amount);
                m.startSpeed = new ParticleSystem.MinMaxCurve(s * .85f, s * 1.15f);
                float z = flameSize * Mathf.Lerp(.55f, 1.1f, amount);
                m.startSize = new ParticleSystem.MinMaxCurve(z * .75f, z * 1.15f);
                if (amount > 0 && !ps.isPlaying) ps.Play(true);
            }
            foreach (var ps in glows)
            {
                if (!ps) continue;
                var e = ps.emission; e.rateOverTimeMultiplier = glowRate * amount;
            }
        }
    }
}
