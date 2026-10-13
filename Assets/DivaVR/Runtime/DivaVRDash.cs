using DigiPhant;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 冲刺在驾驶舱里的呈现（冲刺本身——吃到火箭、大招放完自动全速前冲 2.5 秒、1.6 倍速、推进器喷火——在 PC / VR 共用的
    /// DivaShowMeta.Dash）：驾驶舱两侧有风的线条往后飞，座舱震动，一声"嘟"，尾部暖光；举双手蓄力时上升音 + 越震越厉害。
    /// </summary>
    public class DivaVRDash : MonoBehaviour
    {
        public bool Active => show && show.Dashing;
        DigiPhantLocomotion loco;
        DivaShowDirector show;
        ParticleSystem streaks;
        Light tail;

        void Start()
        {
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            show = FindAnyObjectByType<DivaShowDirector>();
            if (!loco || !show) { enabled = false; return; }
            show.Dashed += OnDash;
            BuildStreaks();
            // 尾部的暖色点光：冲刺时亮，照亮大象的屁股和地面
            var lg = new GameObject("Dash Tail Light");
            lg.transform.SetParent(loco.travelRoot, false);
            lg.transform.localPosition = new Vector3(0, 1.6f, -2.2f);
            tail = lg.AddComponent<Light>(); tail.type = LightType.Point; tail.color = new Color(1f, .55f, .3f); tail.range = 9; tail.intensity = 0; tail.shadows = LightShadows.None;
        }

        void OnDestroy() { if (show) show.Dashed -= OnDash; }

        DivaVRCockpit cockpit; DivaVRHands hands;

        void OnDash(string why)
        {
            if (streaks) streaks.Play();
            if (!cockpit) cockpit = GetComponent<DivaVRCockpit>();
            if (cockpit) cockpit.Rumble(1);
            if (show.Sound != null) show.Sound.Play("toot", .5f, 1.15f);
            Debug.Log("DIVA_VR dash " + why);
        }

        float lastUltProgress;
        void Update()
        {
            if (streaks && streaks.isPlaying && !Active) streaks.Stop();
            // 举双手蓄力（0.4 秒）：一举起就有上升音，座舱越震越厉害——火箭点火前的倒数
            if (!hands) hands = GetComponent<DivaVRHands>();
            if (!cockpit) cockpit = GetComponent<DivaVRCockpit>();
            float p = hands && show.Phase == ShowPhase.Play && show.Charge >= 100 ? hands.UltProgress : 0;
            if (p > 0 && lastUltProgress <= 0 && show.Sound != null) show.Sound.Play("rise", .7f, 1.3f);
            if (p > 0 && cockpit) cockpit.Rumble(.2f + .6f * p);
            lastUltProgress = p;
            // 尾灯慢慢亮起来（一下全亮整个画面会跳一下）
            if (tail) tail.intensity = Mathf.MoveTowards(tail.intensity, Active ? 2.2f + .5f * Mathf.Sin(Time.time * 30) : 0, Time.deltaTime * 4);
        }

        // 风的线条：驾驶舱两侧往后飞的细长亮条（驾驶舱坐标里模拟，跟着大象）
        void BuildStreaks()
        {
            if (!show.glowMaterial) return;
            var go = new GameObject("Dash Streaks");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, .9f, 2.5f);
            streaks = go.AddComponent<ParticleSystem>();
            var main = streaks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = .45f; main.startSpeed = 0; main.startSize = new ParticleSystem.MinMaxCurve(.03f, .06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .9f, 1f, .35f), new Color(.6f, 1f, .95f, .25f));
            main.maxParticles = 300; main.playOnAwake = false; main.loop = true;
            var em = streaks.emission; em.rateOverTime = 110;
            var sh = streaks.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(3.2f, 2.2f, .5f);
            var vel = streaks.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.z = new ParticleSystem.MinMaxCurve(-14, -20);
            var r = streaks.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = show.glowMaterial; r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .08f; r.lengthScale = 2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            streaks.Stop();
        }
    }
}
