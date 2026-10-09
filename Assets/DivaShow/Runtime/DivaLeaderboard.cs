using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Diva.Show
{
    [Serializable]
    public class DivaRunRecord
    {
        public int teamAdjective, teamNoun;
        public int score, hits, shots, tasks, bestCombo;
        public float seconds, distance, topSpeed;
        public string skin, date;
        public bool finished;
        public int players = 1;
    }

    /// <summary>本机排行榜：存成 JSON（persistentDataPath/diva_leaderboard.json），按分数排，同分用时短的在前。</summary>
    public static class DivaLeaderboard
    {
        [Serializable] class Store { public List<DivaRunRecord> runs = new List<DivaRunRecord>(); }
        public const int Keep = 50;

        /// <summary>Tests point this at a scratch file so they never touch the real leaderboard.</summary>
        public static string OverridePath;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OverridePath = null;
        public static string FilePath => !string.IsNullOrEmpty(OverridePath) ? OverridePath : Path.Combine(Application.persistentDataPath, "diva_leaderboard.json");

        public static List<DivaRunRecord> Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
                    if (store?.runs != null) return Sort(store.runs);
                }
            }
            catch (Exception e) { Debug.LogWarning("DIVA_SHOW leaderboard unreadable: " + e.Message); }
            return new List<DivaRunRecord>();
        }

        static List<DivaRunRecord> Sort(IEnumerable<DivaRunRecord> runs) =>
            runs.Where(r => r != null).OrderByDescending(r => r.score).ThenByDescending(r => r.finished).ThenBy(r => r.seconds).ToList();

        /// <summary>加入一局并保存，返回排序后的列表和这一局的名次（从 1 开始）。</summary>
        public static (List<DivaRunRecord> runs, int rank) Add(DivaRunRecord run)
        {
            var runs = Load();
            runs.Add(run);
            runs = Sort(runs);
            int rank = runs.IndexOf(run) + 1;
            if (runs.Count > Keep) runs.RemoveRange(Keep, runs.Count - Keep);
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(new Store { runs = runs }, true)); }
            catch (Exception e) { Debug.LogWarning("DIVA_SHOW leaderboard not saved: " + e.Message); }
            return (runs, rank);
        }
    }
}
