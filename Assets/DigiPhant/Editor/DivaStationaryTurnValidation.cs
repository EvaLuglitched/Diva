using System;
using UnityEditor;
using UnityEngine;

namespace DigiPhant.Editor
{
    public static class DivaStationaryTurnValidation
    {
        [MenuItem("DigiPhant/Diva/Validate Stationary Turn")]
        public static void Validate()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before validation.");
            var host = new GameObject("Stationary turn validation") { hideFlags = HideFlags.HideAndDontSave };
            host.SetActive(false);
            var clip = new AnimationClip();
            DigiPhantLocomotion loco = null;
            try
            {
                host.AddComponent<DigiPhantController>().inputMode = InputMode.TestSliders;
                var rig = new GameObject("Temporary rig"); rig.transform.SetParent(host.transform, false);
                loco = host.AddComponent<DigiPhantLocomotion>();
                loco.travelRoot = host.transform;
                loco.elephantAnimator = rig.AddComponent<Animator>();
                loco.animationMotionRoot = rig.transform;
                loco.idle = loco.walk = loco.run = loco.backward = loco.turnLeft = loco.turnRight = clip;
                loco.inputSmoothing = 0;
                var state = new DivaControlState();
                var origin = host.transform.position;
                void Step(float now)
                {
                    state.Step(now, .1f, 1, 0, true);
                    // Feed the same independent Forward/Turn channels consumed by DivaDemo locomotion.
                    loco.testForward = state.Forward; loco.testSteering = state.Turn;
                    Check(loco.Evaluate(now, .1f), "Locomotion fixture failed to initialise");
                    Check(loco.CurrentSpeed == 0 && Vector3.Distance(origin, host.transform.position) < .00001f,
                        "Zero-GO turning translated the elephant");
                }
                foreach (float turn in new[] { -.75f, .75f })
                {
                    host.transform.rotation = Quaternion.identity;
                    state.Accept(1, 0, turn, 0, false, false, 1, 10);
                    Step(10);
                    Check(Mathf.DeltaAngle(0, host.transform.eulerAngles.y) * turn > 0, "Stationary turn direction is incorrect");
                    var rotation = host.transform.rotation;
                    state.Accept(1, 0, 0, 0, false, false, 1, 10.1f);
                    Step(10.1f);
                    Check(Quaternion.Angle(rotation, host.transform.rotation) < .001f, "Upright input kept turning");
                    state.Accept(1, 0, turn, 0, false, false, 1, 11);
                    Step(11); rotation = host.transform.rotation;
                    Step(12);
                    Check(Quaternion.Angle(rotation, host.transform.rotation) < .001f, "Stale tracking kept turning");
                }
                state.Accept(1, 0, 1, 0, false, true, 1, 20); Step(20);
                Check(state.Turn == 0, "Solo refill did not suppress steering");
                state.Accept(1, 0, 1, 0, false, false, 1, 21, true); Step(21);
                Check(state.Turn == 0, "Solo ultimate did not suppress steering");
                Debug.Log("DIVA_STATIONARY_TURN_VALIDATION_OK: both yaw directions at zero speed/translation, upright/stale stop, solo refill/ultimate locks.");
            }
            finally
            {
                if (loco) loco.StopAnimation();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(clip);
            }
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
