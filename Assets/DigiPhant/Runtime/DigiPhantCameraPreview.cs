using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace DigiPhant
{
    // MediaPipe owns camera capture; Unity displays its local JPEG preview.
    public class DigiPhantCameraPreview : MonoBehaviour
    {
        public bool autoStartBridge = true;
        [Range(0, 8)] public int cameraIndex;
        public bool showPreview = true;
        public string Status { get; private set; } = "Starting camera...";
        public bool HasLiveFrame => texture != null && Time.realtimeSinceStartup - lastFrame < 2;
        public Texture2D PreviewTexture => HasLiveFrame ? texture : null;
        DigiPhantController controller;
        UdpClient receiver;
        Process ownedBridge;
        Texture2D texture;
        float lastFrame = -1000;
        float startAfter;
        float nextRead;
        bool startAttempted;
        string bridgeOutput = "";
        readonly object outputLock = new object();

        void OnEnable()
        {
            if (!Application.isPlaying || Application.isBatchMode) return;
            BeginPreview();
        }
        public void BeginPreview()
        {
            StopPreview();
            controller = GetComponent<DigiPhantController>();
            if (controller == null) { Status = "Missing elephant controls"; return; }
            try
            {
                receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, controller.port + 2));
                receiver.Client.Blocking = false;
                nextRead = 0;
                startAfter = Time.realtimeSinceStartup + 1;
                startAttempted = false;
                Status = "Connecting to camera...";
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { Status = "Preview could not start: " + e.Message; }
        }
        public bool AcceptJpeg(byte[] bytes, float now)
        {
            if (bytes == null || bytes.Length < 4 || bytes.Length > 60000 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
            if (texture == null) texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return false;
            lastFrame = now;
            Status = "Live camera · unmirrored";
            return true;
        }
        public void PollPreview(float now)
        {
            if (receiver == null) return;
            try
            {
                byte[] latest = null;
                var sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 32 && receiver.Available > 0; i++) latest = receiver.Receive(ref sender);
                if (latest != null) AcceptJpeg(latest, now);
            }
            catch (SocketException e) { Status = "Preview connection: " + e.Message; }
        }
        void Update()
        {
            if (receiver == null) return;
            float now = Time.realtimeSinceStartup;
            if (now >= nextRead) { PollPreview(now); nextRead = now + .08f; }
            if (!startAttempted && now >= startAfter)
            {
                startAttempted = true;
                if (autoStartBridge && !HasLiveFrame && !Application.isBatchMode) LaunchBridge();
            }
            if (ownedBridge != null && ownedBridge.HasExited)
            {
                string detail;
                lock (outputLock) detail = bridgeOutput;
                // Show the bridge's own error (missing model, unknown --diva option, camera not opening...) instead of a guess.
                string reason = BridgeReason(detail);
                Status = reason != null ? "Camera stopped: " + reason + " Fix it, then Retry."
                                        : "Camera stopped. Check camera permission or close an old tracking window, then Retry.";
                if (!string.IsNullOrWhiteSpace(detail)) UnityEngine.Debug.LogWarning("DigiPhant camera bridge: " + detail);
                ownedBridge.Dispose();
                ownedBridge = null;
            }
            else if (texture != null && !HasLiveFrame) Status = "Camera feed interrupted";
        }
        void LaunchBridge()
        {
#if UNITY_EDITOR
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../DigiPhantStarter/Tracking"));
            string executable = Path.Combine(folder, Application.platform == RuntimePlatform.WindowsEditor
                ? ".venv/Scripts/python.exe" : ".venv/bin/python");
            if (!File.Exists(executable) || !File.Exists(Path.Combine(folder, "pose_landmarker_full.task")))
            { Status = "Camera setup needed: follow DigiPhantStarter/README.md section 3."; return; }
            bool divaMode = GetComponent<DivaDemo>() != null && GetComponent<DivaDemo>().isActiveAndEnabled;
            // Diva gestures need the team's bridge files and the hand model next to the starter bridge.
            // Copy TrackingOverrides in when they are missing or out of date, so a forgotten or stale copy cannot
            // make the bridge reject --diva; let the bridge download the hand model on first use.
            if (divaMode && !SyncTrackingOverrides(folder)) return;
            bool needHandModel = divaMode && !File.Exists(Path.Combine(folder, "hand_landmarker.task"));
            try
            {
                lock (outputLock) bridgeOutput = "";
                // WorkingDirectory makes all arguments fixed filenames or validated integers.
                var info = new ProcessStartInfo(executable,
                    "-u bridge.py --no-window --people " + controller.performerCount +
                    " --camera " + Mathf.Clamp(cameraIndex, 0, 8) + " --port " + controller.port +
                    (controller.upperBodyOnly ? " --upper-body-only" : "") +
                    (divaMode ? " --diva" : "") + (needHandModel ? " --download-model" : ""))
                {
                    WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                ownedBridge = new Process { StartInfo = info };
                ownedBridge.OutputDataReceived += CaptureOutput;
                ownedBridge.ErrorDataReceived += CaptureOutput;
                ownedBridge.Start();
                ownedBridge.BeginOutputReadLine();
                ownedBridge.BeginErrorReadLine();
                Status = needHandModel ? "Downloading the hand model (first run, needs internet), then opening the webcam"
                                       : "Opening webcam — allow camera access if prompted";
            }
            catch (Exception e)
            {
                ownedBridge?.Dispose(); ownedBridge = null;
                Status = "Camera launch failed: " + e.Message;
            }
#else
            Status = "Start the camera bridge on this computer.";
#endif
        }
#if UNITY_EDITOR
        bool SyncTrackingOverrides(string folder)
        {
            string source = Path.GetFullPath(Path.Combine(Application.dataPath, "../TrackingOverrides"));
            if (!Directory.Exists(source)) { Status = "Diva gestures need the TrackingOverrides folder from the repo."; return false; }
            try
            {
                foreach (var name in new[] { "bridge.py", "diva_gestures.py", "diva_gestures.json" })
                {
                    string from = Path.Combine(source, name), to = Path.Combine(folder, name);
                    if (!File.Exists(from)) { Status = "TrackingOverrides is missing " + name + "."; return false; }
                    if (File.Exists(to) && File.ReadAllText(from) == File.ReadAllText(to)) continue;
                    File.Copy(from, to, true);
                    UnityEngine.Debug.Log("DigiPhant: updated DigiPhantStarter/Tracking/" + name + " from TrackingOverrides");
                }
                return true;
            }
            catch (Exception e) { Status = "Could not install Diva tracking files: " + e.Message; return false; }
        }
#endif

        // The most useful line of the bridge output: an error, a missing file, or the camera failing to open.
        static string BridgeReason(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return null;
            var lines = output.Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                string lower = line.ToLowerInvariant();
                if (lower.Contains("error") || lower.Contains("missing") || lower.Contains("could not") || lower.Contains("denied")
                    || lower.Contains("not authorized") || lower.Contains("unrecognized") || lower.Contains("no such file"))
                    return line.Length > 160 ? line.Substring(0, 160) + "…" : line;
            }
            return null;
        }

        void CaptureOutput(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            lock (outputLock)
            {
                bridgeOutput += args.Data + "\n";
                if (bridgeOutput.Length > 4000) bridgeOutput = bridgeOutput.Substring(bridgeOutput.Length - 4000);
            }
        }
        public void StopPreview()
        {
            receiver?.Close(); receiver = null;
            if (ownedBridge != null)
            {
                try { if (!ownedBridge.HasExited) ownedBridge.Kill(); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                ownedBridge.Dispose(); ownedBridge = null;
            }
            if (texture != null)
            {
                if (Application.isPlaying) Destroy(texture);
                else DestroyImmediate(texture);
            }
            texture = null;
            lastFrame = -1000;
        }
        void OnDisable() => StopPreview();
        public void DrawInline(float width)
        {
            if (!showPreview) return;
            GUILayout.Label("CAMERA PREVIEW");
            float height = Mathf.Min(width * .75f, Screen.height * .25f);
            Rect picture = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(picture, Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            if (HasLiveFrame) GUI.DrawTexture(picture, texture, ScaleMode.ScaleToFit);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Status, new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 });
            if (GUILayout.Button("Retry", GUILayout.Width(54))) BeginPreview();
            GUILayout.EndHorizontal();
        }
        void OnGUI()
        {
            if (!showPreview || (controller != null && controller.showControls)) return;
            float width = Mathf.Clamp(Screen.width * .27f, 180, 350);
            float height = width * .75f;
            var box = new Rect(Screen.width - width - 12, 12, width, height + 85);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 10, box.y + 5, width - 20, 22), "CAMERA PREVIEW");
            var picture = new Rect(box.x + 8, box.y + 30, width - 16, height - 12);
            GUI.DrawTexture(picture, Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            if (HasLiveFrame) GUI.DrawTexture(picture, texture, ScaleMode.ScaleToFit);
            var labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };
            GUI.Label(new Rect(box.x + 8, box.y + height + 22, width - 75, 55), Status, labelStyle);
            if (GUI.Button(new Rect(box.xMax - 62, box.y + height + 30, 54, 26), "Retry")) BeginPreview();
        }
    }
}
