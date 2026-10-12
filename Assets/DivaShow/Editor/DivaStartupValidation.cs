using System;
using System.Collections.Generic;
using System.Reflection;
using DigiPhant;
using Diva.Show;
using UnityEditor;
using UnityEngine;

namespace Diva.Editor
{
    public static class DivaStartupValidation
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [MenuItem("DigiPhant/Diva/Validate Startup")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before startup validation.");
            // Camera mode: START always opens the camera check, which recognises the players and saves the neutral
            // pose itself (5 s all green) before the race intro; it never jumps straight into the game.
            Check(InputMode.Camera, false, false, ShowPhase.CameraCheck, "uncalibrated camera");
            Check(InputMode.Camera, false, true, ShowPhase.CameraCheck, "pending calibration");
            Check(InputMode.Camera, true, true, ShowPhase.CameraCheck, "recalibration in progress");
            Check(InputMode.Camera, true, false, ShowPhase.CameraCheck, "calibrated camera (recalibrates on the check)");
            Check(InputMode.TestSliders, false, false, ShowPhase.RaceIntro, "manual controls");
            Debug.Log("DIVA_STARTUP_VALIDATION_OK: camera START opens the camera check; manual controls go straight to the intro.");
        }

        static void Check(InputMode mode, bool calibrated, bool pending, ShowPhase expected, string label)
        {
            var host = new GameObject("Startup validation fixture") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false); // Do not start receivers, camera or scene setup.
            float timeScale = Time.timeScale;
            try
            {
                var controller = host.AddComponent<DigiPhantController>();
                controller.inputMode = mode;
                typeof(DigiPhantController).GetProperty("IsCalibrated").SetValue(controller, calibrated);
                typeof(DigiPhantController).GetField("calibrationDeadline", Private).SetValue(controller, pending ? Time.realtimeSinceStartup + 10 : -1f);
                var director = host.AddComponent<DivaShowDirector>();
                director.controller = controller;
                director.raceIntro = host.AddComponent<DivaIntro>();
                typeof(DivaShowDirector).GetProperty("Phase").SetValue(director, ShowPhase.Select);
                typeof(DivaShowDirector).GetField("followingIntro", Private).SetValue(director, true);
                var sound = new DivaShowSound(host);
                // The transition is under test, not audio output. Free generated clips and keep the real sound object silent.
                var clips = (Dictionary<string, AudioClip>)typeof(DivaShowSound).GetField("clips", Private).GetValue(sound);
                foreach (var clip in clips.Values) UnityEngine.Object.DestroyImmediate(clip);
                clips.Clear();
                typeof(DivaShowDirector).GetField("sound", Private).SetValue(director, sound);
                typeof(DivaShowDirector).GetMethod("ConfirmSelect", Private).Invoke(director, null);
                if (director.Phase != expected)
                    throw new InvalidOperationException($"START regression ({label}): expected {expected}, got {director.Phase}.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                Time.timeScale = timeScale;
            }
        }
    }
}
