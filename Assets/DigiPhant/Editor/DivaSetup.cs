using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigiPhant.Editor
{
    public static class DivaSetup
    {
        public const string ScenePath = "Assets/DigiPhant/Scenes/Diva.unity";
        [MenuItem("DigiPhant/Diva/Prepare Playable Demo")]
        public static void Prepare()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before preparing Diva.");
            var scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath))
                scene = EditorSceneManager.OpenScene(ScenePath);
            var c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!c) throw new InvalidOperationException("Open your DigiPhant scene first.");
            if (scene.path != ScenePath && !EditorSceneManager.SaveScene(scene, ScenePath, true))
                throw new InvalidOperationException("Could not preserve the scene copy.");
            if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
            c = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
            if (!c.GetComponent<DigiPhantLocomotion>()) DigiPhantLocomotionSetup.AddToOpenScene();
            Undo.RegisterFullObjectHierarchyUndo(c.gameObject, "Prepare Diva demo");
            var locomotion = c.GetComponent<DigiPhantLocomotion>();
            var diva = c.GetComponent<DivaDemo>();
            if (!diva) diva = Undo.AddComponent<DivaDemo>(c.gameObject);
            diva.enabled = true; diva.solo = false; diva.soloRole = 1;
            diva.thirdPersonCamera = Camera.main;
            diva.trunkBones = locomotion.elephantAnimator.GetComponentsInChildren<Transform>()
                .Where(t => Enumerable.Range(1, 7).Any(i => t.name == "elephant_Trunk" + i + "_bone")).OrderBy(t => t.name).ToArray();
            if (diva.trunkBones.Length == 0) throw new InvalidOperationException("Elephant trunk bones were not found.");
            diva.trunkTip = diva.trunkBones.Last();
            c.SetPerformerCount(3); c.SetUpperBodyOnly(false);
            c.inputMode = InputMode.Camera;
            c.showCameraPreview = c.showControls = true;
            locomotion.enableLocomotion = true; locomotion.stageRadius = 40; locomotion.followElephant = false;
            var preview = c.GetComponent<DigiPhantCameraPreview>();
            if (!preview) preview = Undo.AddComponent<DigiPhantCameraPreview>(c.gameObject);
            preview.autoStartBridge = preview.showPreview = true;
            if (diva.thirdPersonCamera)
            {
                Undo.RecordObject(diva.thirdPersonCamera.transform, "Place Diva camera");
                diva.thirdPersonCamera.transform.position = locomotion.travelRoot.position + locomotion.travelRoot.rotation * diva.cameraOffset;
                diva.thirdPersonCamera.transform.LookAt(locomotion.travelRoot.position + Vector3.up * 1.5f);
                diva.thirdPersonCamera.fieldOfView = 55;
            }
            EditorUtility.SetDirty(c); EditorUtility.SetDirty(locomotion); EditorUtility.SetDirty(diva); EditorUtility.SetDirty(preview);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            Selection.activeGameObject = c.gameObject;
            Debug.Log("DIVA_SCENE_READY: " + ScenePath + " | P1 move, P2 lean, P3 water; original DigiPhant scene preserved");
        }
        [MenuItem("DigiPhant/Diva/Validate Gestures")]
        public static void Validate()
        {
            var obj = new GameObject("Diva validation");
            try
            {
                var c = obj.AddComponent<DigiPhantController>();
                obj.AddComponent<DigiPhantLocomotion>();
                var diva = obj.AddComponent<DivaDemo>();
                c.performerCount = 3; c.upperBodyOnly = false; c.inputMode = InputMode.Camera;
                void Check(bool ok, string msg) { if (!ok) throw new InvalidOperationException(msg); }
                PerformerFrame Person(int slot, float go = 0, float steer = 0, float aim = 0, bool shoot = false, bool drink = false) => new PerformerFrame
                {
                    slot = slot, values = new float[6], confidence = Enumerable.Repeat(1f, 6).ToArray(),
                    gestures = new DivaGestureFrame { go = go, steer = steer, aim = aim, shoot = shoot, drink = drink, confidence = 1 }
                };
                string Packet(params PerformerFrame[] people) => JsonUtility.ToJson(new PoseFrame { version = 1, performerCount = c.performerCount, divaGestures = true, people = people });
                Check(c.AcceptPacket(Packet(Person(1, 1), Person(2, steer: -.7f), Person(3, aim: .8f, shoot: true)), 10), "Custom packet rejected");
                Check(diva.AllVisible(10), "Custom confidence did not see all roles");
                diva.State.Step(10, .02f, 3, 1, true);
                Check(diva.State.Forward == 1 && diva.State.Turn == -.7f && diva.State.Aim == .8f && diva.State.Bursts == 1, "Role channels mapped incorrectly");
                diva.State.Step(10.2f, .02f, 3, 1, true); Check(diva.State.Bursts == 0, "Quick opening repeated too soon");
                diva.State.Step(10.42f, .02f, 3, 1, true); Check(diva.State.Bursts == 1, "Hold failed to spray");
                Check(c.AcceptPacket(Packet(Person(1), Person(2)), 10.45f), "Missing-player packet rejected");
                diva.State.Step(10.45f, .02f, 3, 1, true); Check(!diva.State.Shoot && diva.State.Aim == 0, "Missing P3 kept spraying");
                Check(c.AcceptPacket(Packet(Person(1, 1), Person(2, steer: 1), Person(3)), 11), "Packet rejected");
                diva.State.Step(11.6f, .02f, 3, 1, true); Check(diva.State.Forward == 0 && diva.State.Turn == 0, "Stale people kept moving");
                Check(c.AcceptPacket(Packet(Person(1), Person(2), Person(3)), 12), "Neutral packet rejected");
                Check(c.CalibrateAt(12), "Neutral calibration still required legacy foot mappings");
                Check(c.IsCalibrated, "Calibration did not become ready");
                Check(c.AcceptPacket(Packet(Person(1), Person(2), Person(3, drink: true)), 12.1f), "Drink packet rejected");
                float before = diva.State.Water;
                diva.UpdateInputs(12.1f, .1f); Check(diva.State.Drink && diva.State.Water > before, "Drink failed to refill");
                Check(!c.AcceptPacket(Packet(Person(1, float.NaN)), 12.2f), "Nonfinite gesture was accepted");
                diva.UpdateInputs(12.2f, .02f); Check(!diva.State.Drink && diva.State.Forward == 0, "Invalid packet kept controls active");
                diva.SetSolo(true, 2);
                Check(c.performerCount == 1, "Solo mode did not change camera group");
                Check(c.AcceptPacket(Packet(Person(1, 1, -.8f, 1, true)), 13), "Solo packet rejected");
                diva.State.Step(13, .02f, 1, 2, true); Check(diva.State.Forward == 0 && diva.State.Turn == -.8f && !diva.State.Shoot, "Solo P2 controlled other roles");
                diva.State.Step(13, .02f, 1, 3, true); Check(diva.State.Shoot && diva.State.Aim == 1 && diva.State.Turn == 0, "Solo P3 failed");
                c.ReassignPeople(); diva.State.Step(13.1f, .02f, 3, 1, true);
                Check(!diva.State.Shoot && diva.State.Forward == 0, "Reassignment retained actions");
                Debug.Log("DIVA_GESTURES_VALIDATION_OK: roles, packets, solo, stop, bursts, held spray, refill, freshness, reset and neutral");
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }
    }
}
