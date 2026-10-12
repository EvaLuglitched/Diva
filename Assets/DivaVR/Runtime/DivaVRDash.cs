using DigiPhant;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 冲刺：大招放完（泡泡炸开回到比赛）或吃到火箭时，大象自动全速往前冲 2.5 秒，速度 1.6 倍，推进器喷火，
    /// 驾驶舱两侧有风的线条往后飞，一声"呼"。冲刺时把手只管转弯。
    /// </summary>
    public class DivaVRDash : MonoBehaviour
    {
        public float seconds = 2.5f, speedScale = 1.6f;
        public bool Active => Time.time < until;
        float until = -1, baseRun = -1;
        DigiPhantLocomotion loco;
        DivaShowDirector show;
        ParticleSystem streaks;
        ShowPhase lastPhase;
        Light tail;

        void Start()
        {
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            show = FindAnyObjectByType<DivaShowDirector>();
            if (!loco) { enabled = false; return; }
            baseRun = loco.runSpeed;
            DivaShowDirector.PhaseChanged += OnPhase;
            BuildStreaks();
            // 尾部的暖色点光：冲刺时亮，照亮大象的屁股和地面
            var lg = new GameObject("Dash Tail Light");
            lg.transform.SetParent(loco.travelRoot, false);
            lg.transform.localPosition = new Vector3(0, 1.6f, -2.2f);
            tail = lg.AddComponent<Light>(); tail.type = LightType.Point; tail.color = new Color(1f, .55f, .3f); tail.range = 9; tail.intensity = 0; tail.shadows = LightShadows.None;
        }

        void OnDestroy() { DivaShowDirector.PhaseChanged -= OnPhase; if (loco && baseRun > 0) loco.runSpeed = baseRun; }

        void OnPhase(ShowPhase p)
        {
            // 大招 → 比赛：泡泡炸完接着冲出去
            if (lastPhase == ShowPhase.Ult && p == ShowPhase.Play) Trigger("ult");
            if (p != ShowPhase.Play && p != ShowPhase.Ult) Stop();
            lastPhase = p;
        }

        public void Trigger(string why)
        {
            until = Time.time + seconds;
            if (loco) loco.runSpeed = baseRun * speedScale;
            if (show) { show.VrDash(true); if (show.Sound != null) { show.Sound.Play("whoosh", .8f); show.Sound.Play("rise", .5f); } }
            if (streaks) streaks.Play();
            var cockpit = GetComponent<DivaVRCockpit>(); if (cockpit) cockpit.Rumble(1);
            if (show && show.Sound != null) show.Sound.Play("toot", .5f, 1.15f);
            Debug.Log("DIVA_VR dash " + why);
        }

        void Stop()
        {
            if (until < 0) return;
            until = -1;
            if (loco) loco.runSpeed = baseRun;
            if (show) show.VrDash(false);
            if (streaks) streaks.Stop();
        }

        float lastUltProgress;
        void Update()
        {
            if (until > 0 && Time.time >= until) Stop();
            // 举双手蓄力（0.4 秒）：一举起就有上升音，座舱越震越厉害——火箭点火前的倒数
            var hands = GetComponent<DivaVRHands>();
            float p = hands && show && show.Phase == ShowPhase.Play && show.Charge >= 100 ? hands.UltProgress : 0;
            if (p > 0 && lastUltProgress <= 0 && show.Sound != null) show.Sound.Play("rise", .7f, 1.3f);
            if (p > 0) { var cockpit = GetComponent<DivaVRCockpit>(); if (cockpit) cockpit.Rumble(.2f + .6f * p); }
            lastUltProgress = p;
            // 尾灯慢慢亮起来（一下全亮整个画面会跳一下）
            if (tail) tail.intensity = Mathf.MoveTowards(tail.intensity, Active ? 2.2f + .5f * Mathf.Sin(Time.time * 30) : 0, Time.deltaTime * 4);
        }

        // 风的线条：驾驶舱两侧往后飞的细长亮条（驾驶舱坐标里模拟，跟着大象）
        void BuildStreaks()
        {
            if (!show || !show.glowMaterial) return;
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
