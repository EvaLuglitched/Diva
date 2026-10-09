using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DigiPhant;

namespace Diva
{
    public enum TaskCheck { Reach, CurlTrunk, LiftLegs }

    [Serializable] public class CourseTask
    {
        public string label;
        public Transform landmark;
        [Min(.5f)] public float radius = 5;
        public TaskCheck check = TaskCheck.Reach;
        [Tooltip("Optional: an action clip (e.g. eat, drink) that also completes the task when it plays nearby.")]
        public string action;
        [NonSerialized] public float doneAt = -1;
        public bool Done => doneAt >= 0;
    }

    /// <summary>Score, run timer and the course's four tasks, shown as a HUD over the Game view.</summary>
    public class DivaGameManager : MonoBehaviour
    {
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public DivaLaserBlaster blaster;
        public CourseTask[] tasks = Array.Empty<CourseTask>();
        [Min(0)] public float startDistance = 1.5f;
        [Tooltip("A leg or trunk control beyond this (of its -1..1 range) counts as lifting the leg / curling the trunk.")]
        [Range(.1f, 1)] public float controlThreshold = .35f;
        public bool showHud = true;
        public int Score { get; private set; }
        public int TargetsHit { get; private set; }
        public float RunSeconds => startTime < 0 ? 0 : (finishTime >= 0 ? finishTime : Time.time) - startTime;
        public bool Finished => finishTime >= 0;

        Transform elephant;
        Vector3 startPosition;
        float startTime = -1, finishTime = -1;
        GUIStyle title, line, big;

        void Awake() => RestoreEmission();

        /// <summary>
        /// The editor sometimes clears the _EMISSION keyword on our generated materials when it reloads them,
        /// which turns off every neon. Re-enable it for Diva/Town materials that have an emission colour.
        /// </summary>
        public static void RestoreEmission()
        {
            var fixedMaterials = new HashSet<Material>();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    if (m && fixedMaterials.Add(m) && (m.name.StartsWith("Diva ") || m.name.StartsWith("Town ")) &&
                        m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > .01f && !m.IsKeywordEnabled("_EMISSION"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
        }

        void OnEnable()
        {
            DivaTarget.Hit += OnTargetHit;
            elephant = locomotion ? locomotion.travelRoot : null;
            if (elephant) startPosition = elephant.position;
        }
        void OnDisable() => DivaTarget.Hit -= OnTargetHit;

        void OnTargetHit(DivaTarget target)
        {
            Score += target.points;
            TargetsHit++;
        }

        public void ResetRun()
        {
            Score = TargetsHit = 0;
            startTime = finishTime = -1;
            foreach (var task in tasks) task.doneAt = -1;
            foreach (var target in DivaTarget.All) target.ResetTarget();
            if (elephant) startPosition = elephant.position;
        }

        bool ControlActive(Func<string, bool> label) =>
            controller && controller.controls.Any(c => c.label != null && label(c.label) && Mathf.Abs(c.current) >= controlThreshold);

        // In the Diva three-player mode the body controls are not driven (DivaDemo owns the input), so the
        // tasks use its gestures instead: hands to mouth curls the trunk (eat / drink), pumping arms walks the legs.
        DivaDemo diva;
        bool DivaActive => (diva || controller && (diva = controller.GetComponent<DivaDemo>())) && diva.isActiveAndEnabled;
        bool TrunkCurled => DivaActive ? diva.State.Drink : ControlActive(l => l.StartsWith("Trunk"));
        bool LegsLifted => DivaActive ? diva.State.Forward > 0 : ControlActive(l => l.EndsWith(" leg"));

        void Update()
        {
            if (!elephant) return;
            Vector3 position = elephant.position;
            if (startTime < 0 && Flat(position - startPosition).magnitude > startDistance) startTime = Time.time;
            if (startTime < 0 || Finished) return;
            foreach (var task in tasks)
            {
                if (task.Done || !task.landmark || Flat(position - task.landmark.position).magnitude > task.radius) continue;
                bool ok = task.check == TaskCheck.Reach
                    || task.check == TaskCheck.LiftLegs && LegsLifted
                    || task.check == TaskCheck.CurlTrunk && TrunkCurled
                    || !string.IsNullOrEmpty(task.action) && locomotion.CurrentAction.StartsWith("Action: " + task.action);
                if (ok) task.doneAt = Time.time - startTime;
            }
            if (tasks.Length > 0 && tasks.All(t => t.Done)) finishTime = Time.time;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

        void OnGUI()
        {
            if (!showHud) return;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1, .78f, .1f) } };
                line = new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
                big = new GUIStyle(title) { fontSize = 34, alignment = TextAnchor.MiddleCenter };
            }
            const float width = 270;
            var area = new Rect(Screen.width - width - 16, 16, width, 92 + tasks.Length * 22 + 40);
            GUI.color = new Color(.05f, .08f, .16f, .78f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(area.x + 12, area.y + 8, width - 24, area.height - 12));
            GUILayout.Label("DIVA SAFARI", title);
            int total = DivaTarget.All.Count;
            GUILayout.Label($"Score {Score}   ·   Targets {TargetsHit}   ·   {RunSeconds:0.0}s", line);
            foreach (var task in tasks)
                GUILayout.Label((task.Done ? "<color=#7CFC9A>✔</color> " : "<color=#888>○</color> ") + task.label +
                                (task.Done ? $"  <color=#aaa>{task.doneAt:0.0}s</color>" : ""), new GUIStyle(line) { richText = true });
            GUILayout.BeginHorizontal();
            if (blaster && GUILayout.Button("Shoot (Space)")) blaster.Fire();
            if (GUILayout.Button("Reset run")) ResetRun();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            if (Finished)
                GUI.Label(new Rect(0, Screen.height * .18f, Screen.width, 60), $"COURSE COMPLETE · {RunSeconds:0.0}s · {Score} pts", big);
        }
    }
}
