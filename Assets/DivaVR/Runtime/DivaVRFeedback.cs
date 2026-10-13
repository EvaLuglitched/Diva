using System.Linq;
using DigiPhant;
using Diva;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 比赛里的手感：打中靶子飘"+分"、驾驶舱轻震；任务完成飘"DONE!"加"当当"；跑起来脚下冒闪粉；拱门和终点有光柱；
    /// 冲刺时机甲尾部一盏暖色点光。
    /// </summary>
    public class DivaVRFeedback : MonoBehaviour
    {
        DivaShowDirector show; DivaGameManager game; DigiPhantLocomotion loco; DivaVRCockpit cockpit;
        ParticleSystem dust; readonly bool[] done = new bool[16];

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            game = FindAnyObjectByType<DivaGameManager>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            cockpit = GetComponent<DivaVRCockpit>();
            DivaTarget.Hit += OnHit;
            if (show && show.glowMaterial) { BuildDust(); BuildShafts(); }
        }

        void OnDestroy() => DivaTarget.Hit -= OnHit;

        void OnHit(DivaTarget t)
        {
            if (!t) return;
            DivaVRFloatText.Show("+" + t.points, t.AimPoint + Vector3.up * .6f, new Color(.45f, .9f, 1f), .9f);
            if (cockpit) cockpit.Rumble(.25f);
        }

        void Update()
        {
            if (game == null || !show) return;
            // 任务完成：飘字 + 当当
            for (int i = 0; i < game.tasks.Length && i < done.Length; i++)
            {
                var task = game.tasks[i];
                if (task == null) continue;
                if (task.Done && !done[i])
                {
                    done[i] = true;
                    var at = task.landmark ? task.landmark.position + Vector3.up * 4 : (loco ? loco.travelRoot.position + Vector3.up * 5 : transform.position);
                    DivaVRFloatText.Show(DivaText.T("vr.feed.task"), at, new Color(.5f, 1f, .7f), 1.6f);
                    if (show.Sound != null) show.Sound.PlayAt("tada", at, .9f);
                    if (cockpit) cockpit.Rumble(.4f);
                }
                if (!task.Done) done[i] = false;
            }
            // 完赛判定和电脑版同一套：Eva 的终点线（过线就算完赛，任务没做完只是少拿分）。以前 VR 另判"离终点标记半径内"，
            // 两边圈速差一两秒，奖牌却是同一套门槛
            // 脚下闪粉：越快越多
            if (dust && loco)
            {
                var em = dust.emission;
                float speed = Mathf.Abs(loco.CurrentSpeed);
                em.rateOverTime = show.Phase == ShowPhase.Play || show.Phase == ShowPhase.Ult ? speed * 28 : 0;
                dust.transform.position = loco.travelRoot.position + Vector3.up * .15f;
            }
        }

        void BuildDust()
        {
            var go = new GameObject("Diva Dust");
            dust = go.AddComponent<ParticleSystem>();
            var main = dust.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(.06f, .16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .85f, .95f, .8f), new Color(.7f, 1f, .95f, .6f));
            main.gravityModifier = -.05f; main.maxParticles = 400; main.playOnAwake = true;
            var em = dust.emission; em.rateOverTime = 0;
            var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1.6f;
            var sol = dust.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0));
            var r = dust.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = show.glowMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // 光柱：每个霓虹拱门和终点上方三根慢慢转的发光长条
        void BuildShafts()
        {
            var gates = FindObjectsByType<Transform>().Where(t => t.name.StartsWith("Neon_Gate")).Select(t => t.position).ToList();
            var finish = game != null ? game.tasks.FirstOrDefault(t => t != null && t.check == TaskCheck.Reach && t.landmark) : null;
            if (finish != null) gates.Add(finish.landmark.position);
            var root = new GameObject("Diva Light Shafts").transform;
            int n = 0;
            foreach (var g in gates.Distinct())
            {
                for (int i = 0; i < 3; i++)
                {
                    var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    var c = q.GetComponent<Collider>(); if (c) Destroy(c);
                    q.name = "Shaft"; q.transform.SetParent(root, false);
                    q.transform.position = g + Vector3.up * 7 + Quaternion.Euler(0, i * 120 + n * 40, 0) * Vector3.forward * .9f;
                    q.transform.rotation = Quaternion.Euler(0, i * 120 + n * 40, 0);
                    q.transform.localScale = new Vector3(.5f, 12f, 1);
                    var r = q.GetComponent<MeshRenderer>();
                    var m = new Material(show.glowMaterial);
                    var col = (i % 2 == 0 ? new Color(1f, .5f, .85f) : new Color(.5f, .95f, 1f)) * .35f; col.a = .22f;
                    m.color = col; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col); if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", col);
                    r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                    shafts.Add(q.transform);
                }
                n++;
            }
        }
        readonly System.Collections.Generic.List<Transform> shafts = new System.Collections.Generic.List<Transform>();

        void LateUpdate()
        {
            float t = Time.time;
            for (int i = 0; i < shafts.Count; i++)
            {
                var s = shafts[i];
                s.rotation = Quaternion.Euler(0, s.eulerAngles.y + Time.deltaTime * (6 + i % 3 * 2), 0);
                s.localScale = new Vector3(.5f + .15f * Mathf.Sin(t * 1.3f + i), 12f, 1);
            }
        }
    }
}
