using System;
using System.Globalization;
using System.IO;
using Diva.Show;
using UnityEditor;
using UnityEngine;

namespace Diva.Editor
{
    public static class DivaTrialExportValidation
    {
        [Serializable] class EventFile { public EventRow[] events; }
        [Serializable] class EventRow { public string kind, label, taskLabel, targetName, targetPath; public int taskIndex; }
        [Serializable] class TeardownSummary { public float durationScaledSeconds; public string endInputMode; public int endPlayerCount, score, hits, tasksCompleted; public bool endCalibrated; }

        [MenuItem("DigiPhant/Diva/Validate Trial Export")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before export validation.");
            var type = typeof(DivaReplay).Assembly.GetType("Diva.Show.DivaTrialSnapshot");
            Require(type != null, "Trial export is missing: replay data is not saved to disk.");
            string root = Path.Combine(Path.GetTempPath(), "DivaTrialValidation-" + Guid.NewGuid().ToString("N"));
            var host = new GameObject("Trial validation") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                host.transform.position = new Vector3(1.25f, 2.5f, -3.75f);
                var replay = new DivaReplay(host.transform, null, null);
                replay.Capture(.5f, .75f, 2.25f, true, false, 0);
                var targetObject = new GameObject("Target \"A\",\n雪");
                targetObject.transform.SetParent(host.transform);
                var target = targetObject.AddComponent<DivaTarget>();
                replay.Mark(.6f, "hit", 30, "skill.cannon", target);
                string originalName = targetObject.name;
                targetObject.name = "renamed after hit";
                // Task identity must survive translation and repeated generic skill.task labels.
                typeof(DivaReplay).GetMethod("MarkTask").Invoke(replay, new object[] { .7f, 2, "Bush \"eat\",\n树" });
                string summary = "{\"reason\":\"manual_end\",\"startInputMode\":\"TestSliders\",\"endInputMode\":\"Camera\"}";
                object snapshot = Activator.CreateInstance(type, replay, summary);
                replay.Clear(); // Export is frozen, independent of resets and highlight playback.
                UnityEngine.Object.DestroyImmediate(targetObject);
                Directory.CreateDirectory(root);
                string blockedRoot = Path.Combine(root, "file-not-directory");
                File.WriteAllText(blockedRoot, "preserve this file");
                bool failedAsExpected = false;
                try { type.GetMethod("Save").Invoke(snapshot, new object[] { blockedRoot }); }
                catch (System.Reflection.TargetInvocationException e) when (e.InnerException is IOException || e.InnerException is UnauthorizedAccessException)
                { failedAsExpected = true; }
                Require(failedAsExpected && File.ReadAllText(blockedRoot) == "preserve this file", "Invalid destination did not fail safely.");
                string path = (string)type.GetMethod("Save").Invoke(snapshot, new object[] { root });
                string[] csv = File.ReadAllLines(Path.Combine(path, "route.csv"));
                Require(csv.Length == 2, "Frozen route was lost after replay reset.");
                Require(csv[1].StartsWith("0.5,1.25,2.5,-3.75,"), "CSV must use invariant decimal points.");
                var events = JsonUtility.FromJson<EventFile>(File.ReadAllText(Path.Combine(path, "events.json"))).events;
                Require(events.Length == 2 && events[0].targetName == originalName && events[0].targetPath.Contains(originalName), "Hit identity was not frozen when marked, or JSON escaping failed.");
                Require(events[1].taskIndex == 2 && events[1].taskLabel == "Bush \"eat\",\n树", "Task identity was lost.");
                Require(File.ReadAllText(Path.Combine(path, "summary.json")) == summary, "Actual mode/reason metadata changed.");
                Require((string)type.GetMethod("Save").Invoke(snapshot, new object[] { root }) == path, "Saving the same trial duplicated it.");
                foreach (string reason in new[] { "interrupted", "reset", "tasks_completed" })
                {
                    object other = Activator.CreateInstance(type, replay, "{\"reason\":\"" + reason + "\"}");
                    string otherPath = (string)type.GetMethod("Save").Invoke(other, new object[] { root });
                    Require(otherPath != path && Directory.Exists(otherPath), "Separate trials overwrite one another.");
                }
                Require(Directory.GetDirectories(root).Length == 4, "Trial directory count incorrect.");
                CheckTeardown(root);
                Debug.Log("DIVA_TRIAL_EXPORT_VALIDATION_OK: immutable route/events, identities, culture, JSON, failed-write recovery and unique/idempotent saves.");
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                UnityEngine.Object.DestroyImmediate(host);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
        static void CheckTeardown(string root)
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var cache = typeof(DivaShowDirector).GetMethod("CacheTrialState", flags);
            var freeze = typeof(DivaShowDirector).GetMethod("FreezeTrial", flags);
            Require(cache != null && freeze != null, "Teardown regression: live trial state is not cached before Unity resets time and destroys the controller.");
            var host = new GameObject("Teardown validation") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            try
            {
                var director = host.AddComponent<DivaShowDirector>();
                var controller = host.AddComponent<DigiPhant.DigiPhantController>();
                director.controller = controller;
                controller.inputMode = DigiPhant.InputMode.Camera;
                controller.performerCount = 3;
                typeof(DigiPhant.DigiPhantController).GetProperty("IsCalibrated").SetValue(controller, true);
                var replay = new DivaReplay(host.transform, null, null);
                replay.Capture(174.6f, 0, 1, false, false, 0);
                typeof(DivaShowDirector).GetField("replay", flags).SetValue(director, replay);
                typeof(DivaShowDirector).GetField("runStart", flags).SetValue(director, Time.time - 175f);
                typeof(DivaShowDirector).GetMethod("BeginTrial", flags).Invoke(director, null);
                var game = host.AddComponent<DivaGameManager>();
                director.game = game;
                game.tasks = new[] { new CourseTask { label = "completed task", doneAt = 170f } };
                typeof(DivaGameManager).GetProperty("Score").SetValue(game, 60);
                var stats = typeof(DivaShowDirector).GetField("stats", flags).GetValue(director);
                stats.GetType().GetField("hits").SetValue(stats, 6);
                cache.Invoke(director, null);
                UnityEngine.Object.DestroyImmediate(controller);
                UnityEngine.Object.DestroyImmediate(game);
                typeof(DivaShowDirector).GetField("runStart", flags).SetValue(director, Time.time + 100f); // Time reset on editor stop.
                var snapshot = (DivaTrialSnapshot)freeze.Invoke(director, new object[] { "interrupted" });
                string path = snapshot.Save(root);
                var summary = JsonUtility.FromJson<TeardownSummary>(File.ReadAllText(Path.Combine(path, "summary.json")));
                Require(Mathf.Abs(summary.durationScaledSeconds - 175f) < .1f && summary.endInputMode == "Camera" && summary.endPlayerCount == 3 && summary.endCalibrated,
                    "Teardown lost cached duration/input/calibration/player count.");
                Require(summary.score == 60 && summary.hits == 6 && summary.tasksCompleted == 1, "Teardown lost cached score/hits/task completion.");
                Require(freeze.Invoke(director, new object[] { "interrupted" }) == null, "Teardown froze the same trial twice.");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
