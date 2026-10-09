using System.Collections.Generic;
using UnityEngine;

namespace DigiPhant
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(DigiPhantController), typeof(DigiPhantLocomotion))]
    public class DivaDemo : MonoBehaviour
    {
        public Transform[] trunkBones;
        public Transform trunkTip;
        public Camera thirdPersonCamera;
        public bool solo;
        [Range(1, 3)] public int soloRole = 1;
        [Range(0, 1)] public float gestureConfidence = .3f;
        [Range(.1f, 2)] public float trackingTimeout = .5f;
        [Range(.04f, .5f)] public float sprayInterval = .12f;
        [Range(.1f, 1)] public float heldSprayDelay = .4f;
        [Range(.05f, 1)] public float refillRate = .3f;
        [Range(10, 60)] public float trunkAimDegrees = 35;
        public Vector3 cameraOffset = new Vector3(0, 4.5f, -8);
        public DivaControlState State { get; } = new DivaControlState();
        public int TargetsHit { get; private set; }
        DigiPhantController controller;
        DigiPhantLocomotion locomotion;
        Diva.DivaLaserBlaster laser;
        Diva.DivaGameManager game;
        readonly List<Transform> targets = new List<Transform>();
        readonly List<Renderer> targetRenderers = new List<Renderer>();
        readonly List<float> lastHit = new List<float>();
        readonly List<GameObject> droplets = new List<GameObject>();
        readonly List<Vector3> velocities = new List<Vector3>();
        readonly List<float> births = new List<float>();
        Transform rangeRoot;
        Material waterMaterial, targetMaterial, hitMaterial;
        Vector2 scroll;
        float manualGo, manualTurn, manualAim;
        bool manualShoot, manualDrink;
        bool previousFollow;
        float previousRadius;
        int previousCount;

        // Editor validation can invoke public methods before Unity calls OnEnable.
        void EnsureDependencies()
        {
            if (!controller) controller = GetComponent<DigiPhantController>();
            if (!locomotion) locomotion = GetComponent<DigiPhantLocomotion>();
        }

        void OnEnable()
        {
            EnsureDependencies();
            previousFollow = locomotion.followElephant;
            previousRadius = locomotion.stageRadius;
            previousCount = controller.performerCount;
            locomotion.followElephant = false;
            locomotion.stageRadius = 40;
            if (!thirdPersonCamera) thirdPersonCamera = Camera.main;
            if (Application.isPlaying)
            {
                controller.SetPerformerCount(solo ? 1 : 3);
                if (!Application.isBatchMode) CreateRange();
            }
        }
        public void AcceptFrame(PoseFrame frame, float now)
        {
            if (!isActiveAndEnabled) return;
            State.ClearPeople();
            if (!frame.divaGestures) return;
            foreach (var person in frame.people)
            {
                var g = person.gestures;
                if (g != null) State.Accept(person.slot, g.go, g.steer, g.aim, g.shoot, g.drink, g.confidence, now);
            }
        }
        public void ResetTracking() { State.Clear(); manualGo = manualTurn = manualAim = 0; manualShoot = manualDrink = false; }
        /// <summary>Calibration hint: who is missing, and the solo option for one-person testing.</summary>
        public string WaitingMessage(float now)
        {
            int count = solo ? 1 : 3;
            var missing = new List<string>();
            for (int i = 1; i <= count; i++) if (!State.Visible(i, now)) missing.Add(solo ? "you" : "P" + i);
            if (missing.Count == 0) return "Keep every player visible, standing naturally";
            return "Waiting for " + string.Join(", ", missing) + " to be visible, standing naturally" +
                   (solo ? "" : ". Testing alone? Choose Try alone.");
        }
        public bool AllVisible(float now)
        {
            State.MinimumConfidence = gestureConfidence;
            State.Timeout = trackingTimeout;
            for (int i = 1; i <= (solo ? 1 : 3); i++) if (!State.Visible(i, now)) return false;
            return true;
        }
        public void SetSolo(bool enabled, int role)
        {
            EnsureDependencies();
            solo = enabled; soloRole = Mathf.Clamp(role, 1, 3);
            ResetTracking();
            controller.SetPerformerCount(solo ? 1 : 3);
        }
        public void UpdateInputs(float now, float dt)
        {
            EnsureDependencies();
            State.MinimumConfidence = gestureConfidence;
            State.Timeout = trackingTimeout;
            State.BurstInterval = sprayInterval;
            State.HoldDelay = heldSprayDelay;
            State.RefillPerSecond = refillRate;
            bool manual = controller.inputMode == InputMode.TestSliders;
            if (manual)
            {
                State.ClearPeople();
                State.Accept(1, manualGo, manualTurn, manualAim, manualShoot, manualDrink, 1, now);
                if (!solo)
                {
                    State.Accept(2, 0, manualTurn, 0, false, false, 1, now);
                    State.Accept(3, 0, 0, manualAim, manualShoot, manualDrink, 1, now);
                }
            }
            State.Step(now, dt, solo ? 1 : 3, soloRole, manual || controller.IsCalibrated);
        }
        void LateUpdate()
        {
            if (!Application.isPlaying || !locomotion.travelRoot) return;
            var root = locomotion.travelRoot;
            if (trunkBones != null)
                foreach (var bone in trunkBones)
                    if (bone)
                    {
                        Vector3 pitchAxis = bone.InverseTransformDirection(root.right).normalized;
                        Vector3 yawAxis = bone.InverseTransformDirection(Vector3.up).normalized;
                        bone.localRotation *= Quaternion.AngleAxis(State.Shoot ? 12 : State.Drink ? 18 : 0, pitchAxis) *
                            Quaternion.AngleAxis(State.Aim * trunkAimDegrees / Mathf.Max(1, trunkBones.Length), yawAxis);
                    }
            if (thirdPersonCamera)
            {
                Vector3 desired = root.position + root.rotation * cameraOffset;
                thirdPersonCamera.transform.position = Vector3.Lerp(thirdPersonCamera.transform.position, desired, 1 - Mathf.Exp(-Time.deltaTime * 5));
                thirdPersonCamera.transform.LookAt(root.position + Vector3.up * 1.5f);
            }
            if (State.Bursts > 0)
            {
                EmitBurst(Time.realtimeSinceStartup);
                if (!laser) laser = FindAnyObjectByType<Diva.DivaLaserBlaster>();
                if (laser) laser.Fire(State.Aim * trunkAimDegrees);
            }
            UpdateDroplets(Time.realtimeSinceStartup, Time.deltaTime);
            for (int i = 0; i < targetRenderers.Count; i++)
                if (targetRenderers[i]) targetRenderers[i].sharedMaterial = Time.realtimeSinceStartup - lastHit[i] < .4f ? hitMaterial : targetMaterial;
        }
        Material MakeMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            var material = new Material(shader);
            material.SetColor("_BaseColor", color); material.color = color;
            return material;
        }
        void CreateRange()
        {
            if (!locomotion.travelRoot || rangeRoot) return;
            // The Diva game layer has its own targets, scored by its laser; don't add a second set of spheres.
            if (FindAnyObjectByType<Diva.DivaTarget>(FindObjectsInactive.Exclude) != null) return;
            waterMaterial = MakeMaterial(new Color(.05f, .65f, 1));
            targetMaterial = MakeMaterial(new Color(1, .35f, .1f));
            hitMaterial = MakeMaterial(new Color(.1f, 1, .25f));
            rangeRoot = new GameObject("Diva water targets").transform;
            var root = locomotion.travelRoot;
            for (int i = -2; i <= 2; i++)
            {
                var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                target.name = "Water target " + (i + 3);
                target.transform.SetParent(rangeRoot);
                target.transform.position = root.position + root.rotation * new Vector3(i * 2.5f, 1.4f, 10 + Mathf.Abs(i));
                target.transform.localScale = Vector3.one * 1.2f;
                targets.Add(target.transform); targetRenderers.Add(target.GetComponent<Renderer>()); lastHit.Add(-1000);
                target.GetComponent<Renderer>().sharedMaterial = targetMaterial;
            }
        }
        void EmitBurst(float now)
        {
            if (!rangeRoot) return;
            var root = locomotion.travelRoot;
            Vector3 origin = trunkTip ? trunkTip.position : root.position + Vector3.up * 1.5f + root.forward * 2;
            Vector3 direction = Quaternion.AngleAxis(State.Aim * trunkAimDegrees, Vector3.up) * root.forward;
            // Several visible droplets form each short water burst; no reticle is drawn.
            for (int i = 0; i < 3; i++)
            {
                var drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                drop.name = "Water"; drop.transform.SetParent(rangeRoot);
                drop.transform.position = origin + direction * i * .15f;
                drop.transform.localScale = Vector3.one * .12f;
                drop.GetComponent<Renderer>().sharedMaterial = waterMaterial;
                Destroy(drop.GetComponent<Collider>());
                float verticalSpeed = (locomotion.travelRoot.position.y + 1.4f - origin.y) * 1.7f + .74f;
                droplets.Add(drop); velocities.Add(direction * 17 + Vector3.up * verticalSpeed); births.Add(now);
            }
        }
        void UpdateDroplets(float now, float dt)
        {
            for (int i = droplets.Count - 1; i >= 0; i--)
            {
                var drop = droplets[i];
                Vector3 before = drop.transform.position;
                velocities[i] += Vector3.down * 2.5f * dt;
                Vector3 after = before + velocities[i] * dt;
                drop.transform.position = after;
                bool hit = false;
                for (int j = 0; j < targets.Count; j++)
                {
                    Vector3 segment = after - before;
                    float t = segment.sqrMagnitude < .00001f ? 0 : Mathf.Clamp01(Vector3.Dot(targets[j].position - before, segment) / segment.sqrMagnitude);
                    if ((before + segment * t - targets[j].position).sqrMagnitude <= .49f)
                    {
                        if (now - lastHit[j] > .4f) TargetsHit++;
                        lastHit[j] = now; hit = true; break;
                    }
                }
                if (hit || now - births[i] > 2 || after.y < -.1f)
                {
                    Destroy(drop); droplets.RemoveAt(i); velocities.RemoveAt(i); births.RemoveAt(i);
                }
            }
        }
        public void DrawControls()
        {
            EnsureDependencies();
            float width = Mathf.Min(350, Screen.width * .32f);
            GUI.DrawTexture(new Rect(0, 0, width + 20, Screen.height), Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            GUILayout.BeginArea(new Rect(10, 10, width, Screen.height - 20), GUI.skin.box);
            GetComponent<DigiPhantCameraPreview>()?.DrawInline(width - 20);
            scroll = GUILayout.BeginScrollView(scroll);
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true };
            void Label(string text) => GUILayout.Label(text, style, GUILayout.Width(width - 30));
            Label("DIVA | Three players, one elephant");
            bool selectedSolo = GUILayout.Toolbar(solo ? 1 : 0, new[] { "3 players", "Try alone" }) == 1;
            int role = soloRole;
            if (selectedSolo) role = GUILayout.Toolbar(soloRole - 1, new[] { "P1 Move", "P2 Turn", "P3 Water" }) + 1;
            if (selectedSolo != solo || role != soloRole) SetSolo(selectedSolo, role);
            int mode = GUILayout.Toolbar((int)controller.inputMode, new[] { "Test controls", "Camera" });
            if (mode != (int)controller.inputMode) controller.SetInputMode((InputMode)mode);
            bool upper = GUILayout.Toggle(controller.upperBodyOnly, "Upper body only");
            if (upper != controller.upperBodyOnly) controller.SetUpperBodyOnly(upper);
            Label("P1: pump raised arms to go; stop pumping to stop.");
            Label("P2: lean hands and body left or right; stand upright to go straight.");
            Label("P3: raise left/right arm to aim. Push both open hands out to spray; hands near mouth to refill.");
            float now = Time.realtimeSinceStartup;
            if (!solo) Label("Start left to right in the unmirrored preview: P1, P2, P3. Avoid crossing.");
            Label("P1 " + (State.Forward > 0 ? "GO" : "STOP") + " | P2 " + (State.Turn < -.05f ? "LEFT" : State.Turn > .05f ? "RIGHT" : "STRAIGHT"));
            Label("P3 " + (State.Drink ? "REFILLING" : State.Shoot ? "SPRAYING" : "READY") + " | Water " + Mathf.RoundToInt(State.Water * 100) + "%");
            if (!game) game = FindAnyObjectByType<Diva.DivaGameManager>();
            Label("Target hits: " + (game ? game.TargetsHit : TargetsHit));
            if (controller.inputMode == InputMode.Camera)
            {
                for (int i = 1; i <= (solo ? 1 : 3); i++) Label((solo ? "Your camera" : "P" + i) + (State.Visible(i, now) ? " · visible" : " · waiting / lost"));
                Label(controller.Status);
                if (controller.CalibrationPending)
                {
                    Label("Stand naturally — ready in " + Mathf.CeilToInt(controller.CalibrationSecondsRemaining(now)) + " seconds");
                    if (GUILayout.Button("Cancel ready countdown")) controller.CancelCalibrationCountdown();
                }
                else if (GUILayout.Button("Get ready (10 seconds)")) controller.BeginCalibrationCountdown(now);
                if (GUILayout.Button("Reassign players")) controller.ReassignPeople();
            }
            else
            {
                Label("P1 movement"); manualGo = GUILayout.HorizontalSlider(manualGo, 0, 1);
                Label("P2 left / right"); manualTurn = GUILayout.HorizontalSlider(manualTurn, -1, 1);
                Label("P3 aim left / right"); manualAim = GUILayout.HorizontalSlider(manualAim, -1, 1);
                manualShoot = GUILayout.Toggle(manualShoot, "Spray water"); manualDrink = GUILayout.Toggle(manualDrink, "Drink / refill");
                if (GUILayout.Button("Stop all controls")) ResetTracking();
            }
            if (GUILayout.Button("Return elephant to start")) { locomotion.ResetPosition(); ResetTracking(); }
            if (GUILayout.Button("Fill tank for testing")) State.FillTank();
            Label("Refill is available anywhere in this prototype.");
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        void OnDisable()
        {
            ResetTracking();
            if (locomotion) { locomotion.StopMotion(); locomotion.followElephant = previousFollow; locomotion.stageRadius = previousRadius; }
            if (controller && Application.isPlaying) controller.SetPerformerCount(previousCount);
            if (rangeRoot) Destroy(rangeRoot.gameObject);
            if (waterMaterial) Destroy(waterMaterial); if (targetMaterial) Destroy(targetMaterial); if (hitMaterial) Destroy(hitMaterial);
            targets.Clear(); targetRenderers.Clear(); lastHit.Clear(); droplets.Clear(); velocities.Clear(); births.Clear();
        }
    }
}
