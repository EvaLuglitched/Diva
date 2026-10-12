using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Diva.Show
{
    // Freeze before resets/highlight playback. No Unity object references are serialized.
    public sealed class DivaTrialSnapshot
    {
        [Serializable] class Events { public EventRow[] events; }
        [Serializable] class EventRow
        {
            public float t, weight;
            public string kind, label, taskLabel, targetName, targetPath;
            public int taskIndex; public string targetEntityId;
        }
        readonly string route, events, summary;
        readonly string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        string savedPath;

        public DivaTrialSnapshot(DivaReplay replay, string summaryJson)
        {
            summary = summaryJson;
            var csv = new StringBuilder("t,x,y,z,qx,qy,qz,qw,speed,boost,spray,blow\n");
            if (replay != null)
                foreach (var f in replay.frames)
                {
                    var numbers = new[] { f.t, f.position.x, f.position.y, f.position.z, f.rotation.x, f.rotation.y, f.rotation.z, f.rotation.w, f.speed, f.boost };
                    foreach (float n in numbers) csv.Append(n.ToString("R", CultureInfo.InvariantCulture)).Append(',');
                    csv.Append(f.spray ? "1" : "0").Append(',').Append(f.blow ? "1" : "0").Append('\n');
                }
            route = csv.ToString();
            var rows = new EventRow[replay == null ? 0 : replay.events.Count];
            for (int i = 0; i < rows.Length; i++)
            {
                var e = replay.events[i];
                rows[i] = new EventRow { t = e.t, kind = e.kind, label = e.label, weight = e.weight,
                    taskIndex = e.taskIndex, taskLabel = e.taskLabel, targetName = e.targetName,
                    targetPath = e.targetPath, targetEntityId = e.targetEntityId };
            }
            events = JsonUtility.ToJson(new Events { events = rows }, true);
        }

        public string Save(string root)
        {
            if (savedPath != null) return savedPath;
            string destination = Path.Combine(Path.GetFullPath(root), id);
            string staging = destination + ".partial";
            Directory.CreateDirectory(staging);
            var utf8 = new UTF8Encoding(false);
            // A failed attempt only rewrites this snapshot's own staging files.
            File.WriteAllText(Path.Combine(staging, "route.csv"), route, utf8);
            File.WriteAllText(Path.Combine(staging, "events.json"), events, utf8);
            File.WriteAllText(Path.Combine(staging, "summary.json"), summary, utf8);
            Directory.Move(staging, destination); // Never replace another trial.
            savedPath = destination;
            return savedPath;
        }
    }
}
