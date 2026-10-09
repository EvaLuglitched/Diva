using UnityEngine;
using UnityEngine.Rendering;

namespace Diva.Show
{
    /// <summary>
    /// 过场特效：选角色地台光环、彩带、烟花、巨型泡泡、爆破。全部运行时生成，用机甲包里已有的两个粒子材质
    /// （Mech Flame 是叠加发光，Mech Bubble 是半透明泡泡）复制出实例。
    /// </summary>
    public class DivaShowFx
    {
        readonly Material glow, bubble, confetti, ring, mega;
        readonly Transform parent;

        public DivaShowFx(Transform parent, Material glowMaterial, Material bubbleMaterial)
        {
            this.parent = parent;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            glow = glowMaterial ? new Material(glowMaterial) : shader ? new Material(shader) : null;
            bubble = bubbleMaterial ? new Material(bubbleMaterial) : glow;
            if (bubble) confetti = new Material(bubble) { name = "Diva Confetti" };
            if (confetti) confetti.SetTexture("_BaseMap", Texture2D.whiteTexture);
            // 巨型泡泡：中间透明，边缘一圈彩虹反光，左上角一个高光点
            if (bubble)
            {
                mega = new Material(bubble) { name = "Diva Mega Bubble" };
                mega.SetColor("_BaseColor", Color.white);
                mega.SetTexture("_BaseMap", DivaUi.MakeTexture("Diva Mega Bubble", 256, (x, y) =>
                {
                    float r = Mathf.Sqrt(x * x + y * y);
                    if (r > 1) return Color.clear;
                    float hue = Mathf.Repeat(Mathf.Atan2(y, x) / (2 * Mathf.PI) + r * .3f, 1);
                    var c = Color.HSVToRGB(hue, .45f, 1);
                    float rim = Mathf.Pow(Mathf.Clamp01((r - .55f) / .45f), 2.5f) * (1 - Mathf.Clamp01((r - .97f) / .03f));
                    float spot = Mathf.Exp(-((x + .38f) * (x + .38f) + (y - .42f) * (y - .42f)) / .012f);
                    float a = Mathf.Clamp01(.05f + rim * .85f + spot);
                    return new Color(Mathf.Lerp(c.r, 1, spot), Mathf.Lerp(c.g, 1, spot), Mathf.Lerp(c.b, 1, spot), a);
                }));
            }
            if (glow)
            {
                ring = new Material(glow) { name = "Diva Select Ring" };
                ring.SetTexture("_BaseMap", DivaUi.MakeTexture("Diva Ring", 256, (x, y) =>
                {
                    float r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
                    float edge = Mathf.Exp(-Mathf.Pow((r - .9f) / .025f, 2)) + .5f * Mathf.Exp(-Mathf.Pow((r - .78f) / .012f, 2));
                    float ticks = r > .8f && r < .87f && Mathf.Repeat(a / (Mathf.PI * 2) * 48, 1) < .25f ? .6f : 0;
                    float fill = r < .9f ? .12f * r : 0;
                    float v = Mathf.Clamp01(edge + ticks + fill);
                    return new Color(1, 1, 1, v);
                }));
            }
        }

        ParticleSystem System(string name, Vector3 position, Material material, float lifetime)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, true);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main; main.loop = false; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            Object.Destroy(go, lifetime);
            return ps;
        }

        static void FadeOut(ParticleSystem ps, float hold = .6f)
        {
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, hold), new GradientAlphaKey(0, 1) });
            col.color = g;
        }

        /// <summary>彩带：从大象上方往上喷，再慢慢飘落。</summary>
        public void Confetti(Vector3 position, Color[] colors, int count = 260)
        {
            if (!confetti) return;
            var ps = System("Diva Confetti", position, confetti, 7);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5, 10);
            main.startSize = new ParticleSystem.MinMaxCurve(.1f, .2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.gravityModifier = .35f; main.maxParticles = count;
            var grad = new Gradient();
            var keys = new GradientColorKey[Mathf.Min(8, Mathf.Max(2, colors.Length))];
            for (int i = 0; i < keys.Length; i++) keys[i] = new GradientColorKey(colors[i % colors.Length], i / (keys.Length - 1f));
            grad.SetKeys(keys, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            grad.mode = GradientMode.Fixed;
            main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = 1.2f;
            sh.rotation = new Vector3(-90, 0, 0);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-6, 6);
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.limit = 1.6f; drag.dampen = .06f;
            var noise = ps.noise; noise.enabled = true; noise.strength = .8f; noise.frequency = .6f;
            FadeOut(ps, .8f);
            ps.Emit(count);
        }

        /// <summary>一朵烟花：球面爆开的火花（拉长的发光粒子）加一下闪光。</summary>
        public void Firework(Vector3 position, Color color)
        {
            if (!glow) return;
            var ps = System("Diva Firework", position, glow, 4);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6, 9);
            main.startSize = new ParticleSystem.MinMaxCurve(.25f, .4f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, .5f));
            main.gravityModifier = .3f; main.maxParticles = 200;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = .2f;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.limit = 2; drag.dampen = .08f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .2f));
            FadeOut(ps, .5f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .06f; r.lengthScale = 1.5f;
            ps.Emit(150);
            Flash(position, color, 6, .35f);
        }

        /// <summary>一团短暂的发光（爆炸中心、命中）。</summary>
        public void Flash(Vector3 position, Color color, float size, float seconds)
        {
            if (!glow) return;
            var ps = System("Diva Flash", position, glow, seconds + .5f);
            var main = ps.main; main.startLifetime = seconds; main.startSpeed = 0; main.startSize = size; main.startColor = color; main.maxParticles = 4;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .4f), new Keyframe(.2f, 1), new Keyframe(1, 1.3f)));
            FadeOut(ps, .1f);
            ps.Emit(2);
        }

        /// <summary>泡泡爆开：一圈小泡泡往外飞，加闪光。</summary>
        public void Pop(Vector3 position, Color color, float radius)
        {
            if (!bubble) return;
            var ps = System("Diva Pop", position, bubble, 4);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(radius * 1.2f, radius * 2.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.2f, .55f);
            main.gravityModifier = -.05f; main.maxParticles = 160;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = radius * .4f;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.limit = 1; drag.dampen = .12f;
            FadeOut(ps, .7f);
            ps.Emit(140);
            Flash(position, color, radius * 3, .5f);
        }

        /// <summary>巨型泡泡：一个面向镜头的大泡泡片，由导演每帧设置位置和大小。</summary>
        public Transform GiantBubble()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Diva Mega Bubble";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mega ? mega : bubble; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>出场烟火：往上喷的火花喷泉，big 时更高更密并带闪光。</summary>
        public void Pyro(Vector3 position, Color color, bool big)
        {
            if (!glow) return;
            var ps = System("Diva Pyro", position, glow, 3);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, big ? 1.5f : 1.0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(big ? 9 : 6, big ? 14 : 9);
            main.startSize = new ParticleSystem.MinMaxCurve(big ? .3f : .22f, big ? .6f : .4f);
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white * 2, .4f));
            main.gravityModifier = 1.1f; main.maxParticles = 600;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = big ? 12 : 8; sh.radius = .15f;
            sh.rotation = new Vector3(-90, 0, 0);
            var em = ps.emission; em.rateOverTime = 0;
            em.SetBursts(new[] { new ParticleSystem.Burst(0, big ? 160 : 70), new ParticleSystem.Burst(.08f, big ? 120 : 40), new ParticleSystem.Burst(.16f, big ? 90 : 25),
                                 new ParticleSystem.Burst(.26f, big ? 60 : 0) });
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, .1f));
            FadeOut(ps, .4f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .05f; r.lengthScale = 1.2f;
            ps.Play();
            Flash(position + Vector3.up * .5f, color, big ? 5 : 2.5f, big ? .45f : .25f);
        }

        /// <summary>选角色时大象脚下的发光地台。</summary>
        public Transform SelectRing()
        {
            if (!ring) return null;
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Diva Select Ring";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, true);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = ring; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        public void TintRing(Color c) { if (ring) ring.SetColor("_BaseColor", c); }
    }
}
