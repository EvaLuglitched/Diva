using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Diva.Show
{
    public partial class DivaShowDirector
    {
        [Serializable] class TrialSummary
        {
            public int schemaVersion = 1;
            public string startedUtc, endedUtc, reason, scene, unityVersion;
            public string startInputMode, endInputMode;
            public bool startCalibrated, endCalibrated, endCalibrationPending;
            public int startPlayerCount, endPlayerCount, frameCount, eventCount, score, hits, tasksCompleted, bestCombo, ultimates;
            public float durationScaledSeconds, sampledThroughSeconds, distance, topSpeed, spraySeconds, refillSeconds;
            public float nominalSampleHz = DivaReplay.Hz, sampleLimitSeconds = DivaReplay.MaxSeconds;
            public bool routeReachedLimit;
            public string sampling = "World-space route; actual scaled Time.time timestamps, nominal 30 Hz with no catch-up. Frame-rate gaps and slow-motion intervals are possible. Sampling stops at 600 scaled seconds; summary/events can extend beyond the route. Play and ultimate phases are sampled; highlights are excluded. End metadata is the last live observation (cached before teardown). Input labels describe start/end only; no claim of sustained tracking or calibration validity throughout.";
        }
        bool trialActive;
        TrialSummary trialSummary;
        string trialEndReason;
        // Retained across component disable/destruction, but not an editor domain reload.
        static readonly List<DivaTrialSnapshot> pendingTrials = new List<DivaTrialSnapshot>();

        void BeginTrial()
        {
            trialSummary = new TrialSummary { startedUtc = DateTime.UtcNow.ToString("O"),
                scene = gameObject.scene.path, unityVersion = Application.unityVersion,
                startInputMode = controller ? controller.inputMode.ToString() : "Unavailable",
                startCalibrated = controller && controller.IsCalibrated,
                startPlayerCount = controller ? controller.performerCount : 0 };
            trialEndReason = null;
            trialActive = true;
            CacheTrialState();
        }

        // Snapshot volatile Unity state while all run objects and the scaled clock are live.
        void CacheTrialState()
        {
            if (!trialActive) return;
            var s = trialSummary;
            s.durationScaledSeconds = Mathf.Max(s.durationScaledSeconds, Mathf.Max(0, Time.time - runStart));
            if (controller)
            {
                s.endInputMode = controller.inputMode.ToString();
                s.endCalibrated = controller.IsCalibrated;
                s.endCalibrationPending = controller.CalibrationPending;
                s.endPlayerCount = controller.performerCount;
            }
            if (game)
            {
                s.score = game.Score + bonus;
                s.tasksCompleted = 0;
                foreach (var task in game.tasks) if (task.Done) s.tasksCompleted++;
            }
            else s.score = bonus;
            s.hits = stats.hits; s.distance = stats.distance; s.topSpeed = stats.topSpeed;
            s.spraySeconds = stats.sprayTime; s.refillSeconds = stats.bubbleTime;
            s.bestCombo = stats.bestCombo; s.ultimates = stats.ults;
        }

        DivaTrialSnapshot FreezeTrial(string reason)
        {
            if (!trialActive) return null;
            // OnDisable can run after Time.time resets and the controller is destroyed.
            // Other lifecycle paths still have live state and may include this frame's final stats.
            if (reason != "interrupted") CacheTrialState();
            var s = trialSummary;
            float duration = Mathf.Max(s.durationScaledSeconds, replay?.Duration ?? 0);
            replay?.Mark(duration, "trial_end", 0, reason);
            s.endedUtc = DateTime.UtcNow.ToString("O"); s.reason = reason;
            s.durationScaledSeconds = duration; s.sampledThroughSeconds = replay?.Duration ?? 0;
            s.frameCount = replay?.frames.Count ?? 0; s.eventCount = replay?.events.Count ?? 0;
            s.routeReachedLimit = duration > DivaReplay.MaxSeconds;
            var frozen = new DivaTrialSnapshot(replay, JsonUtility.ToJson(s, true));
            trialActive = false;
            return frozen;
        }

        void FlushTrial(string reason)
        {
            var frozen = FreezeTrial(reason);
            if (frozen != null) pendingTrials.Add(frozen);
            RetryTrialExports();
        }

        [ContextMenu("Retry pending trial exports")]
        public void RetryTrialExports()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../TrialData"));
            for (int i = pendingTrials.Count - 1; i >= 0; i--)
                try
                {
                    string path = pendingTrials[i].Save(root);
                    pendingTrials.RemoveAt(i);
                    Debug.Log("DIVA_TRIAL_SAVED: " + path);
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                {
                    Debug.LogWarning("Diva trial export failed: " + error.Message +
                        ". Frozen data is retained until domain reload; use Retry pending trial exports after fixing access or disk space.");
                    break; // One failed I/O attempt per lifecycle/retry call; never spin.
                }
        }
    }
}
