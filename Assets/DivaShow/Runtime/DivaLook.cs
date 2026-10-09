using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Diva.Show
{
    /// <summary>
    /// 全局光影和后期的"高级感"一层，运行时叠在 Eva 的场景设置上面（不改她的灯光、天空和 Diva Post FX）：
    ///   - 一个优先级更高的全局 Volume：Bloom 只给发光的霓虹和特效（阈值提高，不再把亮色地面也晕开）、
    ///     调色（对比、冷暗部暖高光）、柔和暗角；过场时加景深（对焦大象）和一点胶片颗粒。
    ///   - 只照大象和机甲的冷色轮廓光（灯光图层），把主角从背景里分出来。
    ///   - 一个覆盖整条赛道的反射探针（开局渲染一次），金属和光面机甲反射的是糖果小镇，不是空天空。
    ///   - 主摄像机 SMAA 抗锯齿；主摄像机原来清成纯色深蓝，看不到 Eva 的程序天空，这里改成显示天空。
    /// 关掉这个组件就回到 Eva 原来的样子。
    /// </summary>
    [DisallowMultipleComponent]
    public class DivaLook : MonoBehaviour
    {
        public Camera targetCamera;
        [Tooltip("The elephant travel root: it and the mech get the rim light.")]
        public Transform hero;

        [Header("Post")]
        [Tooltip("Only HDR-bright pixels (neon, glows, effects) bloom.")]
        public float bloomThreshold = 1.15f;
        [Range(0, 3)] public float bloomIntensity = .9f;
        [Range(-100, 100)] public float contrast = 20;
        [Range(-100, 100)] public float saturation = 10;
        [Range(0, 1)] public float vignette = .24f;
        [Tooltip("Depth of field and film grain in cutscenes (skin select, ultimate, finish, highlight, results).")]
        public bool cinematicDepthOfField = true;

        [Header("Light")]
        public Color rimColour = new Color(.72f, .84f, 1f);
        [Range(0, 4)] public float rimIntensity = 1.8f;
        [Tooltip("Scales the scene's ambient light (lower = deeper shadows, more depth).")]
        [Range(.5f, 1.2f)] public float ambientScale = .85f;
        [Tooltip("Scales the sun.")]
        [Range(.8f, 1.5f)] public float keyScale = 1.08f;
        [Tooltip("Rendering layer used only by the elephant and the rim light.")]
        [Range(1, 31)] public int heroLayer = 7;
        public bool townReflections = true;
        [Tooltip("The main camera clears to a solid colour in the scene, which hides the procedural sky; show the sky instead.")]
        public bool showSky = true;

        /// <summary>0 = gameplay, 1 = cutscene look. Set every frame by DivaShowDirector.</summary>
        [System.NonSerialized] public float cinematic;
        /// <summary>Metres from the camera to keep sharp in cutscenes.</summary>
        [System.NonSerialized] public float focusDistance = 10;

        Volume volume;
        VolumeProfile profile;
        Bloom bloom;
        DepthOfField dof;
        FilmGrain grain;
        ChromaticAberration aberration;
        Vignette vig;
        Light rim, key;
        ReflectionProbe probe;
        float savedAmbient = -1, savedKey = -1;
        Color savedSky, savedEquator, savedGround;
        float pulse, blend;
        CameraClearFlags previousClear;
        AntialiasingMode previousAa;
        AntialiasingQuality previousAaQuality;
        UniversalAdditionalCameraData cameraData;
        readonly List<(Renderer r, uint mask)> heroRenderers = new List<(Renderer, uint)>();

        bool started;

        void OnEnable()
        {
            if (!targetCamera) targetCamera = Camera.main;
            BuildVolume();
            BuildRim();
            ScaleLighting();
            if (started && townReflections) BuildProbe();
            if (targetCamera)
            {
                previousClear = targetCamera.clearFlags;
                if (showSky && RenderSettings.skybox) targetCamera.clearFlags = CameraClearFlags.Skybox;
            }
            if (targetCamera && targetCamera.TryGetComponent(out cameraData))
            {
                previousAa = cameraData.antialiasing; previousAaQuality = cameraData.antialiasingQuality;
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;
            }
        }

        void Start()
        {
            started = true;
            if (townReflections && !probe) BuildProbe();
        }

        void OnDisable()
        {
            if (volume) Destroy(volume.gameObject);
            if (profile) Destroy(profile);
            if (rim) Destroy(rim.gameObject);
            if (probe) Destroy(probe.gameObject);
            foreach (var (r, mask) in heroRenderers) if (r) r.renderingLayerMask = mask;
            heroRenderers.Clear();
            if (savedAmbient >= 0)
            {
                RenderSettings.ambientIntensity = savedAmbient;
                RenderSettings.ambientSkyColor = savedSky; RenderSettings.ambientEquatorColor = savedEquator; RenderSettings.ambientGroundColor = savedGround;
                savedAmbient = -1;
            }
            if (key && savedKey >= 0) { key.intensity = savedKey; savedKey = -1; }
            if (cameraData) { cameraData.antialiasing = previousAa; cameraData.antialiasingQuality = previousAaQuality; }
            if (targetCamera) targetCamera.clearFlags = previousClear;
        }

        void BuildVolume()
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Diva Look";
            bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(bloomThreshold);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(.68f);
            bloom.tint.Override(new Color(1, .93f, .97f));
            bloom.highQualityFiltering.Override(true);
            var colour = profile.Add<ColorAdjustments>(true);
            colour.contrast.Override(contrast);
            colour.saturation.Override(saturation);
            // 暗部偏冷紫、亮部偏暖：画面更有层次，糖果色不会发灰
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(.96f, .95f, 1.06f, -.04f));
            smh.midtones.Override(new Vector4(1, 1, 1, 0));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, .97f, .02f));
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(4);
            vig = profile.Add<Vignette>(true);
            vig.color.Override(new Color(.13f, .05f, .2f));
            vig.intensity.Override(vignette);
            vig.smoothness.Override(.5f);
            dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focalLength.Override(85);
            dof.aperture.Override(4f);
            dof.focusDistance.Override(10);
            dof.active = false;
            grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Thin2);
            grain.intensity.Override(0);
            aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0);
            var go = new GameObject("Diva Look Volume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50;   // Eva's Diva Post FX stays underneath; we only override these settings
            volume.sharedProfile = profile;
        }

        // 轮廓光：从主光的反方向、偏低的角度打过来，只照灯光图层 heroLayer 上的大象和机甲
        void BuildRim()
        {
            FindKey();
            if (!hero) return;
            var go = new GameObject("Diva Rim Light");
            go.transform.SetParent(transform, false);
            rim = go.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.color = rimColour;
            rim.intensity = rimIntensity;
            rim.shadows = LightShadows.None;
            uint bit = 1u << heroLayer;
            var data = rim.GetUniversalAdditionalLightData();
            if (data) data.renderingLayers = bit;
            rim.renderingLayerMask = (int)bit;
            foreach (var r in hero.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                heroRenderers.Add((r, r.renderingLayerMask));
                r.renderingLayerMask |= bit;
            }
        }

        void FindKey()
        {
            if (key) return;
            key = RenderSettings.sun;
            if (!key)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.enabled && l.name != "Diva Rim Light") { key = l; break; }
        }

        // 环境光压低一点、主光亮一点：阴影有层次，不再整体发灰发平（关掉组件时还原）
        void ScaleLighting()
        {
            FindKey();
            savedAmbient = RenderSettings.ambientIntensity;
            savedSky = RenderSettings.ambientSkyColor; savedEquator = RenderSettings.ambientEquatorColor; savedGround = RenderSettings.ambientGroundColor;
            RenderSettings.ambientIntensity = savedAmbient * ambientScale;
            RenderSettings.ambientSkyColor = savedSky * ambientScale;
            RenderSettings.ambientEquatorColor = savedEquator * ambientScale;
            RenderSettings.ambientGroundColor = savedGround * ambientScale;
            if (key) { savedKey = key.intensity; key.intensity = savedKey * keyScale; }
        }

        // 覆盖整个赛道的反射探针，开局渲染一次
        void BuildProbe()
        {
            var go = new GameObject("Diva Town Reflections");
            go.transform.SetParent(transform, false);
            var centre = hero ? hero.position : Vector3.zero;
            go.transform.position = centre + Vector3.up * 4;
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.size = new Vector3(160, 60, 160);
            probe.hdr = true;
            probe.intensity = 1;
            probe.importance = 1;
            probe.cullingMask = ~(1 << 5);
            probe.farClipPlane = 300;
            probe.RenderProbe();
        }

        /// <summary>Re-renders the town reflections (e.g. after the skin changed the mech colours).</summary>
        public void RefreshReflections() { if (probe) probe.RenderProbe(); }

        /// <summary>A short punch for impacts: a little chromatic aberration and extra bloom.</summary>
        public void Pulse(float amount = 1) => pulse = Mathf.Max(pulse, Mathf.Clamp01(amount));

        void LateUpdate()
        {
            // 轮廓光跟着镜头：永远从镜头看过去的后上方、偏一侧打过来，只照亮大象的边缘
            if (rim && targetCamera)
            {
                var f = Vector3.ProjectOnPlane(targetCamera.transform.forward, Vector3.up).normalized;
                var side = Vector3.Cross(Vector3.up, f);
                rim.transform.rotation = Quaternion.LookRotation((-f * .7f + side * .45f + Vector3.down * .55f).normalized, Vector3.up);
            }
            if (!profile) return;
            float dt = DivaClock.DeltaTime;
            blend = Mathf.MoveTowards(blend, cinematicDepthOfField ? Mathf.Clamp01(cinematic) : 0, dt * 2.5f);
            pulse = Mathf.MoveTowards(pulse, 0, dt * 2.2f);
            dof.active = blend > .02f;
            if (dof.active)
            {
                // 景深从"几乎全清楚"渐变到 f/5.6，避免切换时跳一下
                dof.focusDistance.value = Mathf.Max(.5f, focusDistance);
                dof.aperture.value = Mathf.Lerp(22, 4f, blend);
            }
            grain.intensity.value = .22f * blend;
            vig.intensity.value = vignette + .08f * blend + .1f * pulse;
            aberration.intensity.value = .45f * pulse;
            bloom.intensity.value = bloomIntensity * (1 + .6f * pulse);
        }
    }
}
