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
        [Tooltip("Mac: always use the Mac's own camera, even when an iPhone (Continuity Camera) is nearby. Off = use Camera Index.")]
        public bool preferBuiltInCamera = true;
        [Tooltip("Start the camera tracking again by itself when it stops in the middle of a game (it used to stay off until Retry).")]
        public bool autoRestartBridge = true;
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
        float restartAt = -1, bridgeStartedAt;
        int restarts;
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
                try { receiver.Client.ReceiveBufferSize = 4 << 20; } catch (SocketException) { }   // HD preview frames arrive in pieces
                nextRead = 0;
                startAfter = Time.realtimeSinceStartup + 1;
                startAttempted = false;
                restarts = 0;
                Status = "Connecting to camera...";
            }
            catch (Exception e) when (e is SocketException || e is ArgumentOutOfRangeException)
            { Status = "Preview could not start: " + e.Message; }
        }
        public bool AcceptJpeg(byte[] bytes, float now)
        {
            if (bytes == null || bytes.Length < 4 || bytes.Length > 2_000_000 || bytes[0] != 0xff || bytes[1] != 0xd8) return false;
            if (texture == null) texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (!ImageConversion.LoadImage(texture, bytes, false)) return false;
            if (lastFrame < 0) UnityEngine.Debug.Log($"DIGIPHANT camera: first frame {texture.width}x{texture.height}");
            lastFrame = now;
            Status = "Live camera · mirrored";
            return true;
        }
        // A sharp preview arrives split into datagrams: "DVP1" + frame id (uint32) + index (uint16) + count (uint16) + bytes.
        uint chunkFrame;
        byte[][] chunks;
        int chunkHave;
        byte[] AddPreviewChunk(byte[] d)
        {
            if (d.Length <= 12 || d[0] != (byte)'D' || d[1] != (byte)'V' || d[2] != (byte)'P' || d[3] != (byte)'1') return null;
            uint id = BitConverter.ToUInt32(d, 4);
            int index = BitConverter.ToUInt16(d, 8), count = BitConverter.ToUInt16(d, 10);
            if (count == 0 || count > 512 || index >= count) return null;
            if (chunks == null || id != chunkFrame || chunks.Length != count) { chunkFrame = id; chunks = new byte[count][]; chunkHave = 0; }
            if (chunks[index] == null)
            {
                chunks[index] = new byte[d.Length - 12];
                Buffer.BlockCopy(d, 12, chunks[index], 0, d.Length - 12);
                chunkHave++;
            }
            if (chunkHave < count) return null;
            int total = 0;
            foreach (var c in chunks) total += c.Length;
            var whole = new byte[total];
            int at = 0;
            foreach (var c in chunks) { Buffer.BlockCopy(c, 0, whole, at, c.Length); at += c.Length; }
            chunks = null;
            return whole;
        }
        public void PollPreview(float now)
        {
            if (receiver == null) return;
            try
            {
                byte[] latest = null;
                var sender = new IPEndPoint(IPAddress.Loopback, 0);
                for (int i = 0; i < 512 && receiver.Available > 0; i++)
                {
                    var data = receiver.Receive(ref sender);
                    if (data.Length >= 2 && data[0] == 0xff && data[1] == 0xd8) latest = data;   // a whole small JPEG
                    else { var whole = AddPreviewChunk(data); if (whole != null) latest = whole; }
                }
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
            if (restartAt > 0 && now >= restartAt && ownedBridge == null)
            {
                restartAt = -1;
                if (!HasLiveFrame) LaunchBridge();
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
                // Start it again by itself: a tracking crash in the middle of a game used to leave the gestures off until
                // Retry. Wait 2, 4, 8 … 30 s when it keeps stopping straight away (camera denied, another bridge running).
                if (autoRestartBridge && autoStartBridge && !Application.isBatchMode)
                {
                    if (now - bridgeStartedAt > 60) restarts = 0;
                    float wait = Mathf.Min(30, 2 * Mathf.Pow(2, restarts));
                    restarts++;
                    restartAt = now + wait;
                    Status = (reason != null ? "Camera stopped: " + reason : "Camera stopped.") + $" Restarting it in {wait:0} s…";
                    UnityEngine.Debug.Log($"DIGIPHANT camera bridge stopped; restart {restarts} in {wait:0} s");
                }
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
            // The hand model is required; the Gesture Recognizer (pretrained thumbs-up) is optional and the bridge
            // falls back to the hand model if its download fails.
            bool needHandModel = divaMode && (!File.Exists(Path.Combine(folder, "hand_landmarker.task"))
                                              || !File.Exists(Path.Combine(folder, "gesture_recognizer.task")));
            // WorkingDirectory makes all arguments fixed filenames or validated integers.
            StartBridge(executable, "-u bridge.py " + BridgeArguments(divaMode) + (needHandModel ? " --download-model" : ""), folder,
                needHandModel ? "Downloading the hand / gesture models (first run, needs internet), then opening the webcam"
                              : "Opening webcam — allow camera access if prompted");
#else
            LaunchPackagedBridge();
#endif
        }

        /// <summary>The bridge options shared by the editor and release builds (same people / camera / port / Diva rules).</summary>
        string BridgeArguments(bool divaMode) =>
            "--no-window --people " + controller.performerCount + " --camera " + CameraForBridge() + " --port " + controller.port +
            (controller.upperBodyOnly ? " --upper-body-only" : "") + (divaMode ? " --diva" : "");

        /// <summary>
        /// The camera number for the bridge. On a Mac it is the built-in camera's place in the bridge's list
        /// (DigiPhantMacCameras): an iPhone nearby (Continuity Camera) can take number 0, and the game used to open the
        /// phone, or lose the camera when the phone went away. Elsewhere, or with preferBuiltInCamera off: cameraIndex.
        /// </summary>
        int CameraForBridge()
        {
            int configured = Mathf.Clamp(cameraIndex, 0, 8);
            if (!preferBuiltInCamera) return configured;
            int builtIn = DigiPhantMacCameras.BuiltInIndex(out string cameras);
            if (cameras != null)
                UnityEngine.Debug.Log("DIGIPHANT cameras (bridge order): " + cameras +
                                      (builtIn >= 0 ? " -> using camera " + builtIn : " -> no built-in camera, using camera " + configured));
            return builtIn >= 0 ? Mathf.Min(builtIn, 8) : configured;
        }

        void StartBridge(string executable, string arguments, string workingDirectory, string status)
        {
            try
            {
                lock (outputLock) bridgeOutput = "";
                var info = new ProcessStartInfo(executable, arguments)
                {
                    WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                ownedBridge = new Process { StartInfo = info };
                ownedBridge.OutputDataReceived += CaptureOutput;
                ownedBridge.ErrorDataReceived += CaptureOutput;
                ownedBridge.Start();
                ownedBridge.BeginOutputReadLine();
                ownedBridge.BeginErrorReadLine();
                bridgeStartedAt = Time.realtimeSinceStartup;
                Status = status;
            }
            catch (Exception e)
            {
                ownedBridge?.Dispose(); ownedBridge = null;
                Status = "Camera launch failed: " + e.Message;
            }
        }

#if !UNITY_EDITOR
        /// <summary>
        /// Release builds carry the same tracking bridge (folder DivaTracking), started with the same options as in the editor.
        /// macOS: a self-contained copy inside the app (Contents/Resources/DivaTracking/diva_bridge, no Python needed); the system
        /// asks for camera permission the first time (the app's Info.plist has NSCameraUsageDescription) and the bridge keeps
        /// reopening the camera until it is allowed. Windows: DivaTracking next to Diva.exe holds the bridge sources, the models and
        /// a Python environment that setup_camera.bat creates once. The only difference from the editor: the walking diagnostics
        /// CSV is off (--no-go-log), because the app folder can be read-only; detection is untouched.
        /// </summary>
        void LaunchPackagedBridge()
        {
            bool windows = Application.platform == RuntimePlatform.WindowsPlayer;
            string folder = Application.platform == RuntimePlatform.OSXPlayer
                ? Path.Combine(Application.dataPath, "Resources", "DivaTracking")
                : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "DivaTracking"));
            bool divaMode = GetComponent<DivaDemo>() != null && GetComponent<DivaDemo>().isActiveAndEnabled;
            string frozen = Path.Combine(folder, "diva_bridge", windows ? "diva_bridge.exe" : "diva_bridge");
            string python = Path.Combine(folder, windows ? ".venv/Scripts/python.exe" : ".venv/bin/python");
            string Quote(string p) => "\"" + p + "\"";
            // macOS: ask for the camera first (the system dialog appears as soon as the game opens; one click on Allow),
            // then start the bridge. Denied: the home page shows ALLOW CAMERA, which opens the camera setting.
            if (Application.platform == RuntimePlatform.OSXPlayer && CameraAccess != Access.Allowed)
            {
                if (askRoutine == null) askRoutine = StartCoroutine(AskForCameraThenLaunch());
                return;
            }
            if (File.Exists(frozen))
            {
                string data = Path.Combine(folder, "diva_bridge", "_internal");
                StartBridge(frozen, BridgeArguments(divaMode) + " --no-go-log" +
                    " --model " + Quote(Path.Combine(data, "pose_landmarker_full.task")) +
                    " --hand-model " + Quote(Path.Combine(data, "hand_landmarker.task")) +
                    " --gesture-model " + Quote(Path.Combine(data, "gesture_recognizer.task")) +
                    " --diva-config " + Quote(Path.Combine(data, "diva_gestures.json")),
                    Path.GetDirectoryName(frozen), "Opening webcam — allow camera access when the system asks");
            }
            else if (File.Exists(python) && File.Exists(Path.Combine(folder, "bridge.py")))
                StartBridge(python, "-u bridge.py " + BridgeArguments(divaMode) + " --no-go-log", folder, "Opening webcam");
            else
                Status = windows ? "Camera gestures: run DivaTracking\\setup_camera.bat once (needs Python 3.10+), then restart. Keyboard works now."
                                 : "This build has no camera bridge. Keyboard works now.";
        }

        Coroutine askRoutine;

        System.Collections.IEnumerator AskForCameraThenLaunch()
        {
            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                CameraAccess = Access.Asking;
                Status = "Click Allow in the camera dialog (Diva reads your movements on this Mac)";
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
            }
            // Denied (now or earlier): wait until it is switched on in System Settings, then carry on by itself
            while (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                if (CameraAccess != Access.Denied) UnityEngine.Debug.Log("DIGIPHANT camera access denied");
                CameraAccess = Access.Denied;
                Status = "Camera is off for Diva: click ALLOW CAMERA, switch Diva on, come back. The keyboard works meanwhile.";
                yield return new WaitForSecondsRealtime(1.5f);
            }
            CameraAccess = Access.Allowed;
            UnityEngine.Debug.Log("DIGIPHANT camera access allowed");
            askRoutine = null;
            LaunchPackagedBridge();
        }
#endif

        public enum Access { Unknown, Asking, Allowed, Denied }
        /// <summary>macOS release builds: where the camera permission stands (the editor and Windows stay Unknown).</summary>
        public Access CameraAccess { get; private set; } = Access.Unknown;

        /// <summary>The home page's ALLOW CAMERA button: opens System Settings > Privacy &amp; Security > Camera.</summary>
        public void OpenCameraSettings() => Application.OpenURL("x-apple.systempreferences:com.apple.preference.security?Privacy_Camera");
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
                // Release builds have no console: the bridge's first lines go to Player.log, so camera problems can be read afterwards
                if (!Application.isEditor && loggedBridgeLines < 60) { loggedBridgeLines++; UnityEngine.Debug.Log("DIGIPHANT bridge: " + args.Data); }
            }
        }
        int loggedBridgeLines;
        public void StopPreview()
        {
#if !UNITY_EDITOR
            if (askRoutine != null) { StopCoroutine(askRoutine); askRoutine = null; }
#endif
            receiver?.Close(); receiver = null;
            restartAt = -1;
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
