using System.Collections.Generic;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 本局录制（录数据，不录屏）：每秒 30 帧记录大象位置朝向、全部骨骼、水枪/泡泡/火焰状态，外加事件。
    /// 结束后按事件分数找最精彩的一段，再用这些数据把那段重演一遍。
    /// </summary>
    public class DivaReplay
    {
        public struct Frame
        {
            public float t;
            public Vector3 position;
            public Quaternion rotation;
            public float boost, speed;
            public bool spray, blow;
            public Quaternion gunRotation;
            public float jetSpeed;
        }

        public struct Event
        {
            public float t;
            public string kind;     // hit, combo3, combo5, ult, task, boost, bubble, finish
            public float weight;
            public DivaTarget target;
            public string label;    // 文字表的 key（显示时翻译）
        }

        public const float Hz = 30;
        public const float MaxSeconds = 600;

        readonly Transform root;
        readonly Transform[] bones;
        readonly Transform gun;
        public readonly List<Frame> frames = new List<Frame>();
        readonly List<Quaternion> boneRotations = new List<Quaternion>();
        readonly List<Vector3> bonePositions = new List<Vector3>();
        public readonly List<Event> events = new List<Event>();
        float nextCapture;
        public float Duration => frames.Count > 0 ? frames[frames.Count - 1].t : 0;

        public DivaReplay(Transform root, Transform[] bones, Transform gun)
        {
            this.root = root; this.bones = bones ?? new Transform[0]; this.gun = gun;
        }

        public void Clear() { frames.Clear(); boneRotations.Clear(); bonePositions.Clear(); events.Clear(); nextCapture = 0; }

        public void Capture(float t, float boost, float speed, bool spray, bool blow, float jetSpeed)
        {
            if (t < nextCapture || t > MaxSeconds || !root) return;
            nextCapture = t + 1 / Hz;
            frames.Add(new Frame
            {
                t = t, position = root.position, rotation = root.rotation, boost = boost, speed = speed, spray = spray, blow = blow,
                gunRotation = gun ? gun.rotation : Quaternion.identity, jetSpeed = jetSpeed,
            });
            foreach (var b in bones)
            {
                boneRotations.Add(b ? b.localRotation : Quaternion.identity);
                bonePositions.Add(b ? b.localPosition : Vector3.zero);
            }
        }

        public void Mark(float t, string kind, float weight, string label, DivaTarget target = null) =>
            events.Add(new Event { t = t, kind = kind, weight = weight, label = label, target = target });

        /// <summary>找分数最高的一段：事件分数之和，加一点平均速度分（没打中东西时也能选出跑得最快的一段）。</summary>
        public (float start, float end, float key, string label) Best(float window)
        {
            float duration = Duration;
            if (duration <= window) return (0, duration, KeyIn(0, duration, out var l0), l0);
            float bestStart = 0, bestScore = float.MinValue;
            for (float s = 0; s <= duration - window; s += .25f)
            {
                float score = 0;
                foreach (var e in events) if (e.t >= s + .3f && e.t <= s + window - 1.2f) score += e.weight;
                float speed = 0; int n = 0;
                for (int i = IndexAt(s); i < frames.Count && frames[i].t <= s + window; i += 3) { speed += Mathf.Abs(frames[i].speed); n++; }
                score += n > 0 ? speed / n * 4 : 0;
                if (score > bestScore) { bestScore = score; bestStart = s; }
            }
            float key = KeyIn(bestStart, bestStart + window, out var label);
            return (bestStart, bestStart + window, key, label);
        }

        float KeyIn(float s, float e, out string label)
        {
            label = null;
            float bestW = 0, key = (s + e) * .5f;
            foreach (var ev in events)
                if (ev.t >= s && ev.t <= e && ev.weight >= bestW) { bestW = ev.weight; key = ev.t; label = ev.label; }
            return key;
        }

        int IndexAt(float t)
        {
            int lo = 0, hi = frames.Count - 1;
            while (lo < hi) { int mid = (lo + hi + 1) / 2; if (frames[mid].t <= t) lo = mid; else hi = mid - 1; }
            return Mathf.Max(0, lo);
        }

        /// <summary>把录到的姿势放回场景（在骨骼动画之后调用）。返回这一刻的帧数据（特效状态）。</summary>
        public Frame Apply(float t)
        {
            if (frames.Count == 0) return default;
            int i = IndexAt(t), j = Mathf.Min(frames.Count - 1, i + 1);
            var a = frames[i]; var b = frames[j];
            float k = j == i || b.t <= a.t ? 0 : Mathf.Clamp01((t - a.t) / (b.t - a.t));
            root.SetPositionAndRotation(Vector3.Lerp(a.position, b.position, k), Quaternion.Slerp(a.rotation, b.rotation, k));
            int n = bones.Length;
            for (int q = 0; q < n; q++)
            {
                var bone = bones[q];
                if (!bone) continue;
                bone.localRotation = Quaternion.Slerp(boneRotations[i * n + q], boneRotations[j * n + q], k);
                bone.localPosition = Vector3.Lerp(bonePositions[i * n + q], bonePositions[j * n + q], k);
            }
            if (gun) gun.rotation = Quaternion.Slerp(a.gunRotation, b.gunRotation, k);
            var f = a;
            f.boost = Mathf.Lerp(a.boost, b.boost, k);
            f.speed = Mathf.Lerp(a.speed, b.speed, k);
            f.jetSpeed = Mathf.Lerp(a.jetSpeed, b.jetSpeed, k);
            f.position = root.position; f.rotation = root.rotation;
            return f;
        }
    }
}
