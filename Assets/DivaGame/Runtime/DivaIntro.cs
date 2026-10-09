using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using DigiPhant;
using TMPro;
using UnityEngine.UI;

namespace Diva
{
    /// <summary>
    /// Race-start intro in the style of kart-racer openings: aerial view of the town, the rocket flying
    /// past, a swoop down to the start line, a full orbit of the mech elephant while it powers up part by
    /// part, a low hero close-up as its thrusters ignite, then a 3-2-1-GO countdown. All sounds are synthesised here.
    /// The elephant is held on the start line until GO. Esc skips to the countdown, I replays (not while Diva Show runs the match).
    /// On-screen text is uGUI + TextMeshPro, so it also draws on Vision Pro (IMGUI does not).
    /// Diva Show relies on Begin(), Skip(), Playing, Time, playOnStart, SwoopEnd, BootStart, OrbitEnd and Go: keep their names and meaning.
    /// </summary>
    [DefaultExecutionOrder(2000)]   // after DivaDemo's camera and DivaSkyCamera
    public class DivaIntro : MonoBehaviour
    {
        public Camera gameCamera;
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public DivaOrbit rocket;
        public DivaSkyCamera skyCamera;
        public DivaGameManager game;
        public bool playOnStart = true;
        public string title = "DIVA SAFARI";
        public string subtitle = "Candy Town Course";
        [Range(0, 1)] public float volume = .8f;
        [Tooltip("Font for the banner and countdown. Empty: Diva Show's font when it is in the scene, else the TextMesh Pro default.")]
        public TMP_FontAsset font;

        // Timeline, in seconds.
        public const float AerialEnd = 3.5f, RocketEnd = 6.5f, SwoopEnd = 9f, HeroStart = 13f, OrbitEnd = 15.5f;
        public const float BootStart = 9.4f, BootEnd = 11.9f, Ignite = 13.8f;
        public const float CountStart = OrbitEnd + .3f, Go = CountStart + 3, End = Go + 1.2f;
        /// <summary>When the rocket crosses straight ahead of the gameplay camera, in the middle of 3-2-1.</summary>
        public const float RocketShow = CountStart + 1.6f;
        /// <summary>Extra degrees the camera looks up during the countdown to frame the rocket.</summary>
        const float CountdownLookUp = 7;

        public bool Playing { get; private set; }
        public float Time { get; private set; } = -1;

        float rocketAngle0, fogStart, fogEnd;
        bool fogSaved, fogOn;
        Vector3 lockPos;
        Quaternion lockRot;
        bool savedControls = true, savedHud = true, savedPreview = true, prepared;
        DigiPhantCameraPreview preview;
        readonly List<(Renderer r, Color[] baseColour, Color[] emission)> parts = new List<(Renderer, Color[], Color[])>();
        MaterialPropertyBlock block;
        DivaBoosters boosters;
        AudioSource audioSource;
        AudioClip beep, beepGo, whoosh, chirp, ignite, fanfare, chime;
        readonly List<(float time, AudioClip clip, float pitch, float gain)> cues = new List<(float, AudioClip, float, float)>();
        int nextCue;
        // On-screen UI (built on first Begin).
        Canvas canvas;
        RectTransform barTop, barBottom, banner, bannerLine, titleRect, subtitleRect;
        Image bannerFill, bannerLineFill;
        TextMeshProUGUI titleText, subtitleText, skipText, countText;
        bool showDirectsMatch;
        // Ignition burst.
        bool ignited;
        Light flash;
        float baseFlameSpeed = -1, baseFlameSize;
        readonly List<ParticleSystem> ignition = new List<ParticleSystem>();
        static Material smokeMaterial, glowMaterial;
        static Texture2D softDot;

        // ---------- Lifecycle ----------

        void Start()
        {
            // Diva Show starts the intro after its skin select and owns replays, so the I key stays out of its way.
            showDirectsMatch = FindAnyObjectByType<Diva.Show.DivaShowDirector>();
            if (playOnStart) Begin();
        }

        /// <summary>Starts (or restarts) the intro.</summary>
        public void Begin()
        {
            Prepare();
            if (!Playing)
            {
                savedControls = controller ? controller.showControls : true;
                savedHud = game ? game.showHud : true;
                preview = controller ? controller.GetComponent<DigiPhantCameraPreview>() : null;
                savedPreview = preview ? preview.showPreview : true;
            }
            if (rocket)
            {
                // Put the rocket on its orbit so that it flies across in front of the elephant during 3-2-1.
                rocketAngle0 = RocketStartAngle();
                rocket.SetAngle(rocketAngle0, UnityEngine.Time.time);
            }
            Playing = true;
            Time = 0;
            nextCue = 0;
            ignited = false;
            BuildCues();
            if (controller) controller.showControls = false;
            if (game) game.showHud = false;
            if (preview) preview.showPreview = false;   // its floating window would cover the cinematic
            if (Application.isPlaying) { BuildUi(); UpdateUi(0); }
        }

        /// <summary>Jumps to the countdown, with the mech already powered up.</summary>
        public void Skip()
        {
            if (!Playing || Time >= OrbitEnd) return;
            Time = OrbitEnd;
            if (rocket) rocket.SetAngle(rocketAngle0 + rocket.AngularSpeed * Time, UnityEngine.Time.time);   // keep its countdown pass
            ignited = true;   // no burst when jumping straight to the countdown
            while (nextCue < cues.Count && cues[nextCue].time < Time) nextCue++;
        }

        /// <summary>Reads the start pose, mech parts and rocket; safe to call in the editor for previews.</summary>
        public void Prepare()
        {
            if (!gameCamera) gameCamera = Camera.main;
            if (locomotion && locomotion.travelRoot)
            {
                lockPos = locomotion.travelRoot.position;
                lockRot = Quaternion.LookRotation(Flat(locomotion.travelRoot.forward));
                boosters = locomotion.travelRoot.GetComponent<DivaBoosters>();
            }
            rocketAngle0 = rocket ? (Application.isPlaying ? rocket.Angle : rocket.startAngle * Mathf.Deg2Rad) : 0;
            parts.Clear();
            if (locomotion && locomotion.travelRoot)
            {
                // Power-up order: legs, body, back, head, trunk gun.
                string[] order = { "l_Tibia", "r_Tibia", "l_Radius", "r_Radius", "Spine3", "Spine2", "Head", "Trunk7" };
                var mech = locomotion.travelRoot.GetComponentsInChildren<Renderer>(true)
                    .Where(r => r.name.StartsWith("DivaMech elephant_") && !(r is ParticleSystemRenderer)).ToList();
                foreach (var key in order)
                    foreach (var r in mech.Where(m => m.name.Contains(key)))
                        parts.Add((r, r.sharedMaterials.Select(m => m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white).ToArray(),
                                      r.sharedMaterials.Select(m => m && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black).ToArray()));
            }
            block ??= new MaterialPropertyBlock();
            if (!fogSaved) { fogOn = RenderSettings.fog; fogStart = RenderSettings.fogStartDistance; fogEnd = RenderSettings.fogEndDistance; fogSaved = true; }
            if (!audioSource && Application.isPlaying)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0;
                MakeSounds();
            }
            prepared = true;
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.escapeKey.wasPressedThisFrame) Skip();
            if (keys.iKey.wasPressedThisFrame && !showDirectsMatch) Begin();
        }

        void LateUpdate()
        {
            if (!Playing) return;
            Time += UnityEngine.Time.deltaTime;
            while (nextCue < cues.Count && cues[nextCue].time <= Time)
            {
                var cue = cues[nextCue++];
                if (cue.clip && audioSource) { audioSource.pitch = cue.pitch; audioSource.PlayOneShot(cue.clip, cue.gain * volume); }
            }
            if (Time < Go && locomotion && locomotion.travelRoot)
            {
                // Held on the start line until GO.
                locomotion.StopMotion();
                locomotion.travelRoot.SetPositionAndRotation(lockPos, lockRot);
            }
            ApplyBoot(Time);
            ApplyBoosters(Time);
            ApplyFog(Time);
            if (!ignited && Time >= Ignite) { ignited = true; Ignition(); }
            UpdateIgnition(Time);
            if (Time < Go && gameCamera)
            {
                PoseAt(Time, out var pos, out var rot, out float fov);
                // A short, decaying shake on ignition.
                float shake = Time >= Ignite ? .16f * Mathf.Max(0, 1 - (Time - Ignite) / .55f) : 0;
                if (shake > 0)
                    pos += rot * new Vector3(Mathf.PerlinNoise(Time * 30, 1) - .5f, Mathf.PerlinNoise(1, Time * 30) - .5f, 0) * 2 * shake;
                gameCamera.transform.SetPositionAndRotation(pos, rot);
                gameCamera.fieldOfView = fov;
            }
            UpdateUi(Time);
            if (Time >= End) Finish();
        }

        void Finish()
        {
            Playing = false;
            if (boosters && baseFlameSpeed >= 0) { boosters.flameSpeed = baseFlameSpeed; boosters.flameSize = baseFlameSize; baseFlameSpeed = -1; }
            if (flash) flash.intensity = 0;
            ApplyBoot(float.MaxValue);
            ApplyFog(float.MaxValue);
            if (boosters) boosters.SetBoost(-1);
            if (controller) controller.showControls = savedControls;
            if (game) game.showHud = savedHud;
            if (preview) preview.showPreview = savedPreview;
            if (canvas) canvas.gameObject.SetActive(false);
        }

        void OnDisable() { if (Playing) Finish(); }
        void OnDestroy() { if (canvas) Destroy(canvas.gameObject); }

        // ---------- Camera ----------

        static Vector3 Flat(Vector3 v) { v.y = 0; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
        static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float s)
        {
            float u = 1 - s;
            return u * u * u * a + 3 * u * u * s * b + 3 * u * s * s * c + s * s * s * d;
        }

        Vector3 RocketAt(float t) => rocket ? rocket.PositionAt(rocketAngle0 + rocket.AngularSpeed * t) : transform.position + Vector3.up * 60;

        Vector3 RocketCamera()
        {
            // A fixed spot beside the orbit, square to the rocket's path at mid-shot: it crosses the frame side-on.
            float mid = (AerialEnd + RocketEnd) / 2;
            float a = rocketAngle0 + (rocket ? rocket.AngularSpeed * mid : 0);
            Vector3 pm = RocketAt(mid), outward = Flat(pm - transform.position);
            return pm + outward * 78 - DivaOrbit.TangentAt(a) * 6 - Vector3.up * 10;
        }

        void GamePose(out Vector3 pos, out Quaternion rot, out float fov)
        {
            var demo = controller ? controller.GetComponent<DivaDemo>() : null;
            Vector3 offset = demo ? demo.cameraOffset : new Vector3(0, 4.5f, -8);
            pos = lockPos + lockRot * offset;
            rot = Quaternion.LookRotation(lockPos + Vector3.up * 1.5f - pos);
            float up = skyCamera ? skyCamera.lookUp : 0;
            rot = Quaternion.AngleAxis(-up, rot * Vector3.right) * rot;
            fov = skyCamera ? skyCamera.fieldOfView : 60;
        }

        /// <summary>
        /// The rocket's orbit angle at intro time 0 that puts it straight ahead of the gameplay camera (where the
        /// camera's flat forward ray meets the orbit circle) at RocketShow. Needs Prepare() first.
        /// </summary>
        public float RocketStartAngle()
        {
            if (!rocket) return 0;
            float now = Application.isPlaying ? rocket.Angle : rocket.startAngle * Mathf.Deg2Rad;
            if (!locomotion || !locomotion.travelRoot) return now;
            GamePose(out var pos, out var rot, out _);
            Vector3 f = Flat(rot * Vector3.forward), p = pos - rocket.centre;
            p.y = 0;
            float r = rocket.radius, pf = Vector3.Dot(p, f), q = pf * pf - (p.sqrMagnitude - r * r);
            if (q < 0) return now;   // camera outside the orbit and looking away: leave it
            Vector3 hit = p + f * (-pf + Mathf.Sqrt(q));
            return Mathf.Atan2(hit.z, hit.x) - rocket.AngularSpeed * RocketShow;
        }

        /// <summary>Camera pose at a time on the intro timeline.</summary>
        public void PoseAt(float t, out Vector3 pos, out Quaternion rot, out float fov)
        {
            if (!prepared) Prepare();
            Vector3 centre = transform.position, up = Vector3.up, forward = lockRot * Vector3.forward;
            Vector3 elephantLook = lockPos + up * 1.7f, look;
            Vector3 orbitStart = lockPos - forward * 9 + up * 3.2f;
            if (t < AerialEnd || !rocket && t < RocketEnd)
            {
                // Aerial establishing shot, slowly circling the town.
                float s = t / (rocket ? AerialEnd : RocketEnd);
                float a = Mathf.Lerp(215, 250, s) * Mathf.Deg2Rad;
                pos = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 92 + up * Mathf.Lerp(66, 56, s);
                look = centre + up * 4;
                fov = 50;
            }
            else if (t < RocketEnd)
            {
                // The rocket flies past a fixed camera.
                pos = RocketCamera();
                look = RocketAt(t);
                fov = 55;
            }
            else if (t < SwoopEnd)
            {
                // Swoop from the sky down to the start line.
                float s = Ease((t - RocketEnd) / (SwoopEnd - RocketEnd));
                Vector3 from = RocketCamera();
                pos = Bezier(from, centre + up * 55 + Flat(from - centre) * 30, lockPos - forward * 24 + up * 15, orbitStart, s);
                look = Vector3.Lerp(RocketAt(t), elephantLook, Ease(s * 1.6f));
                fov = Mathf.Lerp(55, 50, s);
            }
            else if (t < HeroStart)
            {
                // One full turn around the mech elephant.
                float s = Ease((t - SwoopEnd) / (HeroStart - SwoopEnd));
                Vector3 arm = Quaternion.AngleAxis(s * 360, up) * -forward;
                pos = lockPos + arm * Mathf.Lerp(9, 7, s) + up * Mathf.Lerp(3.2f, 2.4f, s);
                look = elephantLook;
                fov = 50;
            }
            else if (t < OrbitEnd)
            {
                // Hero close-up: low, front three-quarter, slowly pushing in as the thrusters ignite.
                float s = Ease((t - HeroStart) / (OrbitEnd - HeroStart));
                Vector3 arm = Quaternion.AngleAxis(145, up) * -forward;
                pos = lockPos + arm * Mathf.Lerp(7, 5, s) + up * Mathf.Lerp(1.1f, 1.5f, s);
                look = lockPos + forward * 1.2f + up * Mathf.Lerp(2.2f, 2f, s);
                fov = Mathf.Lerp(48, 42, s);
            }
            else
            {
                // Settle into the gameplay camera for the countdown.
                float s = Ease((t - OrbitEnd) / .9f);
                Vector3 from = lockPos - forward * 7 + up * 2.4f;
                GamePose(out var gp, out var gr, out float gf);
                pos = Vector3.Lerp(from, gp, s);
                rot = Quaternion.Slerp(Quaternion.LookRotation(elephantLook - from), gr, s);
                // Look up a little more during 3-2-1 so the rocket passing overhead is fully in view, back to the game view by GO.
                float lift = CountdownLookUp * Ease((t - OrbitEnd) / 1.2f) * (1 - Ease((t - (Go - .6f)) / .6f));
                rot = Quaternion.AngleAxis(-lift, rot * Vector3.right) * rot;
                fov = Mathf.Lerp(50, gf, s);
                return;
            }
            rot = Quaternion.LookRotation(look - pos);
        }

        // ---------- Mech power-up ----------

        /// <summary>Dark until each part's turn, then a bright flash that settles to its normal colours.</summary>
        public void ApplyBoot(float t)
        {
            if (!prepared) Prepare();
            int n = parts.Count;
            for (int i = 0; i < n; i++)
            {
                var (r, baseColour, emission) = parts[i];
                if (!r) continue;
                float at = BootStart + (n > 1 ? i * (BootEnd - BootStart) / (n - 1) : 0);
                float k = t - at;
                for (int m = 0; m < baseColour.Length; m++)
                {
                    if (k >= .45f) { r.SetPropertyBlock(null, m); continue; }
                    block.Clear();
                    Color dark = baseColour[m] * .12f; dark.a = baseColour[m].a;
                    if (k < 0)
                    {
                        block.SetColor("_BaseColor", dark);
                        block.SetColor("_EmissionColor", Color.black);
                    }
                    else
                    {
                        float p = k / .45f;
                        Color lit = Color.Lerp(dark, baseColour[m], Ease(p * 1.5f)); lit.a = baseColour[m].a;
                        block.SetColor("_BaseColor", lit);
                        block.SetColor("_EmissionColor", emission[m] + new Color(1, .7f, .95f) * 3f * (1 - p));
                    }
                    r.SetPropertyBlock(block, m);
                }
            }
        }

        /// <summary>No haze for the high aerial shots; after the swoop the game's fog eases back in from far away.</summary>
        public void ApplyFog(float t)
        {
            if (!fogSaved) return;
            RenderSettings.fog = fogOn && t >= SwoopEnd;
            float far = t < SwoopEnd ? 1 : 1 - Ease((t - SwoopEnd) / 1.5f);
            RenderSettings.fogStartDistance = Mathf.Lerp(fogStart, 400, far);
            RenderSettings.fogEndDistance = Mathf.Lerp(fogEnd, 900, far);
        }

        // ---------- Ignition burst ----------

        /// <summary>Smoke billowing out from both thrusters, a ground shock ring, sparks and a flash: visible from the front.</summary>
        void Ignition()
        {
            if (!locomotion || !locomotion.travelRoot) return;
            PrepareEffectMaterials();
            var root = locomotion.travelRoot;
            foreach (var ps in ignition) if (ps) Destroy(ps.gameObject);
            ignition.Clear();
            var nozzles = boosters ? boosters.cores.Where(c => c).Select(c => c.transform).ToList() : new List<Transform>();
            if (nozzles.Count == 0) nozzles.Add(root);   // no mech: burst from the elephant's back
            foreach (var n in nozzles)
            {
                Vector3 pos = n == root ? root.position + Vector3.up * 2.2f - root.forward * 1.5f : n.position;
                Vector3 dir = n == root ? -root.forward : n.forward;
                // Big soft smoke, thrown back and billowing up and out past the body.
                ignition.Add(Burst("Ignition smoke", pos, dir, smokeMaterial, 45, new Vector2(4, 10), new Vector2(1.4f, 2.4f),
                    new Vector2(1.2f, 2f), 3.2f, new Color(1, .9f, .97f, .9f), new Color(.92f, .82f, 1, 0), -.18f, 55, .35f, 2.2f));
                // Bright sparks.
                ignition.Add(Burst("Ignition sparks", pos, dir, glowMaterial, 70, new Vector2(7, 16), new Vector2(.35f, .8f),
                    new Vector2(.12f, .28f), .3f, new Color(1, .75f, .45f, 1), new Color(1, .3f, .6f, 0), 1.2f, 40, .1f, .6f));
            }
            // Dust ring rolling out along the ground in every direction.
            ignition.Add(Burst("Ignition shock ring", root.position + Vector3.up * .3f, Vector3.up, smokeMaterial, 80, new Vector2(9, 13),
                new Vector2(.9f, 1.5f), new Vector2(1.3f, 2.2f), 2.4f, new Color(.95f, .88f, 1, .8f), new Color(.85f, .8f, 1, 0), -.05f, 89, .6f, 2.6f, ring: true));
            if (!flash)
            {
                flash = new GameObject("Ignition flash").AddComponent<Light>();
                flash.type = LightType.Point;
                flash.color = new Color(1, .55f, .6f);
                flash.range = 16;
                flash.shadows = LightShadows.None;
            }
            flash.transform.position = root.position + Vector3.up * 2.5f - root.forward * 2;
            if (boosters && baseFlameSpeed < 0) { baseFlameSpeed = boosters.flameSpeed; baseFlameSize = boosters.flameSize; }
        }

        void UpdateIgnition(float t)
        {
            float k = t - Ignite;
            if (flash) flash.intensity = k < 0 ? 0 : k < .08f ? 14 * k / .08f : 14 * Mathf.Exp(-(k - .08f) * 5);
            if (boosters && baseFlameSpeed >= 0)
            {
                // Flames twice as long and fast for the ignition, easing back by 1.5 s.
                float boost = k < 0 ? 1 : k < .8f ? 2.2f : Mathf.Lerp(2.2f, 1, (k - .8f) / .7f);
                boosters.flameSpeed = baseFlameSpeed * boost;
                boosters.flameSize = baseFlameSize * Mathf.Lerp(1, boost, .8f);
                if (k > 1.5f) { boosters.flameSpeed = baseFlameSpeed; boosters.flameSize = baseFlameSize; baseFlameSpeed = -1; }
            }
        }

        ParticleSystem Burst(string name, Vector3 position, Vector3 direction, Material material, int count, Vector2 speed, Vector2 life,
            Vector2 size, float grow, Color start, Color end, float gravity, float coneAngle, float radius, float drag, bool ring = false)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1; main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = start;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 10;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var shape = ps.shape;
            if (ring)
            {
                // A flat ring on the ground, emitting outwards (the object faces up, so the circle lies flat).
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = radius;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = coneAngle;
                shape.radius = radius;
            }
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1, grow >= 1 ? AnimationCurve.EaseInOut(0, 1 / grow, 1, 1) : AnimationCurve.Linear(0, 1, 1, grow));
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(start, 0), new GradientColorKey(end, 1) },
                new[] { new GradientAlphaKey(start.a, 0), new GradientAlphaKey(start.a * .8f, .4f), new GradientAlphaKey(0, 1) });
            colour.color = gradient;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.drag = drag;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            Destroy(go, life.y + 1.5f);
            return ps;
        }

        static void PrepareEffectMaterials()
        {
            if (smokeMaterial && glowMaterial) return;
            if (!softDot)
            {
                const int n = 64;
                softDot = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "Diva soft dot", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                        float a = Mathf.Clamp01(1 - d);
                        px[y * n + x] = new Color(1, 1, 1, a * a * (3 - 2 * a));
                    }
                softDot.SetPixels(px);
                softDot.Apply();
            }
            smokeMaterial = EffectMaterial("Diva ignition smoke", false);
            glowMaterial = EffectMaterial("Diva ignition glow", true);
        }

        // Same transparent URP particle setup as the mech's own effects.
        static Material EffectMaterial(string name, bool additive)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = name };
            mat.SetTexture("_BaseMap", softDot);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", additive ? 2 : 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            return mat;
        }

        void ApplyBoosters(float t)
        {
            if (!boosters) return;
            if (t < Ignite) boosters.SetBoost(0);
            else if (t < Ignite + .7f) boosters.SetBoost(1);
            else if (t < Go) boosters.SetBoost(.35f);
            else if (t < Go + .8f) boosters.SetBoost(1);
            else boosters.SetBoost(-1);
        }

        // ---------- Sound ----------

        void BuildCues()
        {
            cues.Clear();
            cues.Add((.3f, chime, 1, .7f));
            cues.Add(((AerialEnd + RocketEnd) / 2 - .9f, whoosh, 1, 1));
            cues.Add((RocketEnd + .2f, whoosh, .6f, .7f));
            int n = Math.Max(1, parts.Count);
            for (int i = 0; i < parts.Count; i++)
                cues.Add((BootStart + (n > 1 ? i * (BootEnd - BootStart) / (n - 1) : 0), chirp, 1 + i * .08f, .55f));
            cues.Add((Ignite, ignite, 1, 1));
            for (int i = 0; i < 3; i++) cues.Add((CountStart + i, beep, 1, .8f));
            cues.Add((Go, beepGo, 1, .9f));
            cues.Add((Go + .12f, fanfare, 1, .6f));
            cues.Sort((a, b) => a.time.CompareTo(b.time));
        }

        static AudioClip Synth(string name, float seconds, Func<float, float> wave)
        {
            const int rate = 44100;
            int n = (int)(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(wave(i / (float)rate), -1, 1);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Tone(float f, float t) => Mathf.Sin(2 * Mathf.PI * f * t) + .25f * Mathf.Sin(6 * Mathf.PI * f * t);

        void MakeSounds()
        {
            beep = Synth("Countdown beep", .32f, t => Tone(523, t) * .45f * Mathf.Min(1, t * 200) * Mathf.Exp(-t * 7));
            beepGo = Synth("Countdown go", .9f, t => (Tone(1047, t) + .5f * Tone(1568, t)) * .35f * Mathf.Min(1, t * 200) * Mathf.Exp(-t * 2.5f));
            var noise = new System.Random(7);
            float lp = 0;
            whoosh = Synth("Whoosh", 1.8f, t =>
            {
                float cutoff = .02f + .25f * Mathf.Exp(-Mathf.Pow((t - .9f) / .35f, 2));
                lp += cutoff * ((float)noise.NextDouble() * 2 - 1 - lp);
                return lp * 2.2f * Mathf.Exp(-Mathf.Pow((t - .9f) / .45f, 2));
            });
            chirp = Synth("Power up", .28f, t => Mathf.Sin(2 * Mathf.PI * (380 * t + 1800 * t * t)) * .35f * Mathf.Min(1, t * 120) * Mathf.Exp(-t * 6));
            float lp2 = 0;
            ignite = Synth("Ignite", 1.4f, t =>
            {
                lp2 += .08f * ((float)noise.NextDouble() * 2 - 1 - lp2);
                float env = Mathf.Min(1, t * 30) * Mathf.Exp(-t * 2.2f);
                return (lp2 * 3 + Mathf.Sin(2 * Mathf.PI * (55 + 30 * t) * t) * .6f) * env * .7f;
            });
            float[] notes = { 523, 659, 784, 1047 };
            fanfare = Synth("Fanfare", 1.1f, t =>
            {
                int i = Mathf.Min(3, (int)(t / .1f));
                float local = t - i * .1f, decay = i == 3 ? 2.5f : 14;
                return Tone(notes[i], t) * .3f * Mathf.Min(1, local * 300) * Mathf.Exp(-local * decay);
            });
            chime = Synth("Title chime", 1.6f, t =>
                (Mathf.Sin(2 * Mathf.PI * 1319 * t) * Mathf.Exp(-t * 3) + Mathf.Sin(2 * Mathf.PI * 1760 * Mathf.Max(0, t - .18f)) * Mathf.Exp(-Mathf.Max(0, t - .18f) * 3) * (t > .18f ? 1 : 0)) * .3f);
        }

        // ---------- Overlay ----------

        // ---------- On-screen UI (uGUI + TextMeshPro) ----------

        static readonly Color Outline = new Color(.35f, .1f, .35f, .9f);
        static readonly Color[] CountColours = { new Color(1, .4f, .75f), new Color(.35f, .9f, 1), new Color(1, .86f, .3f), new Color(1, .3f, .7f) };

        /// <summary>A screen-space canvas laid out in screen fractions; the scaler keeps 1080 units of height, so font sizes are fractions of 1080.</summary>
        void BuildUi()
        {
            if (canvas) { canvas.gameObject.SetActive(true); return; }
            var go = new GameObject("Diva Intro UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;   // above Diva Show's UI (50)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;
            var root = go.transform;
            var face = font ? font : Diva.Show.DivaUi.Font ? Diva.Show.DivaUi.Font : TMP_Settings.defaultFontAsset;

            barTop = Box(root, "Bar top", Color.black, out _);
            barBottom = Box(root, "Bar bottom", Color.black, out _);
            banner = Box(root, "Banner", new Color(1, .35f, .72f, .92f), out bannerFill);
            bannerLine = Box(root, "Banner line", new Color(.3f, .95f, 1), out bannerLineFill);
            titleText = Label(root, "Title", face, title, 1080 * .075f, TextAlignmentOptions.Left, out titleRect);
            subtitleText = Label(root, "Subtitle", face, subtitle, 1080 * .035f, TextAlignmentOptions.Left, out subtitleRect);
            skipText = Label(root, "Skip hint", face, "Esc  skip", 22, TextAlignmentOptions.Right, out var skipRect);
            Place(skipRect, .7f, .09f, .99f, .09f + 40 / 1080f);
            skipText.color = new Color(1, 1, 1, .8f);
            countText = Label(root, "Countdown", face, "", 280, TextAlignmentOptions.Center, out var countRect);
            Place(countRect, 0, .3f, 1, .8f);
        }

        static void Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax)
        {
            rt.anchorMin = new Vector2(xMin, yMin); rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static RectTransform Box(Transform parent, string name, Color colour, out Image image)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        static TextMeshProUGUI Label(Transform parent, string name, TMP_FontAsset face, string text, float size, TextAlignmentOptions align, out RectTransform rect)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            rect = (RectTransform)go.transform;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (face) t.font = face;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            // Dark plum outline plus a soft shadow, so candy-coloured text reads on the bright town.
            var m = t.fontMaterial;
            m.EnableKeyword("OUTLINE_ON");
            m.EnableKeyword("UNDERLAY_ON");
            m.SetFloat("_OutlineWidth", .25f);
            m.SetColor("_OutlineColor", Outline);
            m.SetColor("_UnderlayColor", new Color(.2f, .05f, .2f, .55f));
            m.SetFloat("_UnderlaySoftness", .6f);
            m.SetFloat("_UnderlayDilate", .4f);
            t.UpdateMeshPadding();
            return t;
        }

        /// <summary>Letterbox bars, the sliding course banner, the skip hint and 3-2-1-GO, all driven by the intro time.</summary>
        void UpdateUi(float t)
        {
            if (!canvas) return;
            canvas.gameObject.SetActive(Playing);
            if (Playing) DrawUi(t);
        }

        /// <summary>Editor keyframes: draws the UI at time t onto a camera (screen-space overlays are not in camera renders).</summary>
        public void PreviewUi(float t, Camera camera)
        {
            BuildUi();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + .05f;
            DrawUi(t);
            Canvas.ForceUpdateCanvases();
        }

        public void EndPreviewUi()
        {
            if (canvas) DestroyImmediate(canvas.gameObject);
            canvas = null;
        }

        void DrawUi(float t)
        {

            // Cinematic bars until the countdown.
            float bars = t < OrbitEnd ? 1 : 1 - Ease((t - OrbitEnd) / .6f);
            Place(barTop, 0, 1 - .09f * bars, 1, 1);
            Place(barBottom, 0, 0, 1, .09f * bars);

            // Course title banner, sliding in from the left.
            bool showBanner = t > .3f && t < AerialEnd + .4f;
            banner.gameObject.SetActive(showBanner); bannerLine.gameObject.SetActive(showBanner);
            titleText.gameObject.SetActive(showBanner); subtitleText.gameObject.SetActive(showBanner);
            if (showBanner)
            {
                float a = Mathf.Min(Ease((t - .3f) / .5f), 1 - Ease((t - AerialEnd + .2f) / .6f));
                float x = Mathf.Lerp(-.5f, 0, Ease((t - .3f) / .5f));
                Place(banner, x, .19f, x + .52f, .36f);
                Place(bannerLine, x, .178f, x + .52f, .19f);
                Place(titleRect, x + .03f, .25f, x + .5f, .35f);
                Place(subtitleRect, x + .03f, .2f, x + .5f, .26f);
                bannerFill.color = new Color(1, .35f, .72f, .92f * a);
                bannerLineFill.color = new Color(.3f, .95f, 1, a);
                titleText.text = title; subtitleText.text = subtitle;
                titleText.color = new Color(1, 1, 1, a);
                subtitleText.color = new Color(1, .95f, .98f, a);
            }

            skipText.gameObject.SetActive(t < OrbitEnd);

            // 3, 2, 1, GO!
            countText.gameObject.SetActive(false);
            for (int i = 0; i < 4; i++)
            {
                float start = CountStart + i, length = i < 3 ? 1 : 1.2f, p = t - start;
                if (p < 0 || p >= length) continue;
                float pop = 1.7f - .7f * Ease(p / .18f);
                var c = CountColours[i];
                c.a = p > length - .3f ? 1 - (p - (length - .3f)) / .3f : 1;
                countText.gameObject.SetActive(true);
                countText.text = i < 3 ? (3 - i).ToString() : "GO!";
                countText.fontSize = 1080 * (i < 3 ? .26f : .22f) * pop;
                countText.color = c;
            }
        }
    }
}
