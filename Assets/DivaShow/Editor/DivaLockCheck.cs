using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Diva.Editor
{
    /// <summary>
    /// 🔒 Diva v2.0 password lock (Tools/lock): the action detection, the controls and the camera / player lock must
    /// stay exactly as Eva signed them. Unity checks the locked files every time scripts load (a red Console error
    /// names any changed file) and refuses to start a player build while one is changed. The signature itself is
    /// checked by Tools/lock/diva_lock.py (tests, pre-commit hook, GitHub); only Eva's password can approve a change.
    /// </summary>
    [InitializeOnLoad]
    public sealed class DivaLockCheck : IPreprocessBuildWithReport
    {
        const string Manifest = "Tools/lock/lock.json";

        static DivaLockCheck() { EditorApplication.delayCall += () => Report(false); }

        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var changed = Changed(out string problem);
            if (problem != null || changed.Count > 0)
                throw new BuildFailedException(Message(changed, problem) + " The build is stopped.");
        }

        [MenuItem("DigiPhant/Diva/Verify Lock")]
        static void Menu() => Report(true);

        static void Report(bool always)
        {
            var changed = Changed(out string problem);
            if (problem != null || changed.Count > 0) Debug.LogError(Message(changed, problem));
            else if (always) Debug.Log("DIVA_LOCK_OK: the action detection, controls and camera / player lock are exactly as signed.");
        }

        static string Message(List<string> changed, string problem) =>
            "🔒 DIVA_LOCK: " + (problem ?? "changed locked file(s): " + string.Join(", ", changed)) +
            ". These parts are password-locked (GESTURES_LOCKED.md): undo the change, or ask Eva to approve it with her password.";

        /// <summary>Locked files whose content differs from Tools/lock/lock.json (line endings normalised).</summary>
        public static List<string> Changed(out string problem)
        {
            problem = null;
            var changed = new List<string>();
            string root = Path.GetDirectoryName(Application.dataPath);
            string manifestPath = Path.Combine(root, Manifest);
            if (!File.Exists(manifestPath)) { problem = Manifest + " is missing"; return changed; }
            string text = File.ReadAllText(manifestPath);
            var files = Regex.Match(text, "\"files\"\\s*:\\s*\\{(?<body>[^}]*)\\}");
            if (!files.Success) { problem = Manifest + " has no file list"; return changed; }
            using var sha = SHA256.Create();
            foreach (Match entry in Regex.Matches(files.Groups["body"].Value, "\"(?<path>[^\"]+)\"\\s*:\\s*\"(?<hash>[0-9a-f]{64})\""))
            {
                string path = entry.Groups["path"].Value, full = Path.Combine(root, path);
                if (!File.Exists(full)) { changed.Add(path); continue; }
                var bytes = Normalise(File.ReadAllBytes(full));
                var hash = new StringBuilder();
                foreach (byte b in sha.ComputeHash(bytes)) hash.Append(b.ToString("x2"));
                if (hash.ToString() != entry.Groups["hash"].Value) changed.Add(path);
            }
            return changed;
        }

        static byte[] Normalise(byte[] data)   // CRLF -> LF, as diva_lock.py hashes
        {
            var output = new List<byte>(data.Length);
            for (int i = 0; i < data.Length; i++)
                if (!(data[i] == '\r' && i + 1 < data.Length && data[i + 1] == '\n')) output.Add(data[i]);
            return output.ToArray();
        }
    }
}
