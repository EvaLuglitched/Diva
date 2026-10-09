using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 空气里的漂浮光点：粉、青、金三色的小光点在驾驶舱周围的小镇空气里慢慢飘。
    /// 粒子留在世界里不跟着人走，所以前进、转弯时近处光点和远处房子的视差会让 VR 的深度和速度感更强。
    /// 用 Diva Show 的发光材质（和烟火同一个），不另外加资源。
    /// </summary>
    public class DivaVRAmbience : MonoBehaviour
    {
        public Material glow;
        [Range(0, 120)] public float perSecond = 45;

        void Start()
        {
            if (!glow) { enabled = false; return; }
            var go = new GameObject("Ambient Motes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, 1.5f, 6);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7, 11);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.02f, .12f);
            main.startSize = new ParticleSystem.MinMaxCurve(.035f, .085f);
            main.maxParticles = 500;
            main.gravityModifier = -.004f;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(new Color(1f, .55f, .85f), 0), new GradientColorKey(new Color(.45f, 1f, .95f), .5f), new GradientColorKey(new Color(1f, .85f, .45f), 1) },
                         new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
            var emission = ps.emission; emission.rateOverTime = perSecond;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(34, 9, 34);
            var noise = ps.noise; noise.enabled = true; noise.strength = .25f; noise.frequency = .15f; noise.scrollSpeed = .1f;
            var fade = ps.colorOverLifetime; fade.enabled = true;
            var alpha = new Gradient();
            alpha.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                          new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.9f, .2f), new GradientAlphaKey(.9f, .75f), new GradientAlphaKey(0, 1) });
            fade.color = alpha;
            var twinkle = ps.sizeOverLifetime; twinkle.enabled = true;
            twinkle.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .6f), new Keyframe(.5f, 1.1f), new Keyframe(1, .7f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = glow;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            // 一开始就铺满，不要等它慢慢长出来
            ps.Simulate(8, true, false, true);
            ps.Play();
        }
    }
}
