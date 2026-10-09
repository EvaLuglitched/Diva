using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>界面和过场的音效，启动时用程序合成（和机甲音效一样不用录音素材）。</summary>
    public class DivaShowSound
    {
        const int Rate = 44100;
        readonly AudioSource source, music;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        public float volume = .7f;
        /// <summary>For recording tools (video capture): every clip played, with its gain.</summary>
        public static event Action<AudioClip, float> Played;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Played = null;

        public DivaShowSound(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = true;
            music = host.AddComponent<AudioSource>();
            music.playOnAwake = false; music.spatialBlend = 0; music.loop = false;
            var rng = new System.Random(7);
            float Noise() => (float)(rng.NextDouble() * 2 - 1);
            float Env(float t, float a, float d) => t < a ? t / a : Mathf.Exp(-(t - a) / d);

            Make("tick", .07f, t => Mathf.Sin(2 * Mathf.PI * 1500 * t) * Env(t, .003f, .02f) * .5f);
            Make("confirm", .32f, t => (Mathf.Sin(2 * Mathf.PI * (t < .1f ? 880 : 1320) * t) * .5f + Mathf.Sin(2 * Mathf.PI * 2640 * t) * .12f) * Env(t, .005f, .12f));
            Make("beep", .2f, t => Square(660 * t) * .22f * Env(t, .005f, .08f));
            Make("go", .7f, t => (Square(990 * t) * .16f + Mathf.Sin(2 * Mathf.PI * 1485 * t) * .25f + Mathf.Sin(2 * Mathf.PI * 1980 * t) * .12f) * Env(t, .005f, .25f));
            Make("powerup", 1.5f, t =>
            {
                float f = Mathf.Lerp(120, 1400, t / 1.5f * t / 1.5f);
                return (Saw(Phase("powerup", f)) * .14f + Noise() * .05f * (1 - t / 1.5f)) * Mathf.Clamp01(t * 6) * Mathf.Clamp01((1.5f - t) * 5);
            });
            Make("skill", .75f, t => (Mathf.Sin(2 * Mathf.PI * 1760 * t) * .22f + Mathf.Sin(2 * Mathf.PI * 2637 * t) * .16f +
                                      Mathf.Sin(2 * Mathf.PI * 3520 * t) * .08f) * Env(t, .004f, .22f) + Noise() * .08f * Env(t, .002f, .04f));
            Make("hit", .12f, t => (Noise() * .25f + Mathf.Sin(2 * Mathf.PI * 2200 * t) * .3f) * Env(t, .002f, .03f));
            Make("combo", .5f, t => Arp(t, new[] { 784f, 988f, 1175f, 1568f }, .07f) * .3f);
            Make("ready", .7f, t => Arp(t, new[] { 523f, 659f, 784f, 1047f, 1319f }, .09f) * .28f);
            Make("whoosh", 1.2f, t => Noise() * .35f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.2f)) * (.4f + .6f * t / 1.2f));
            Make("pop", .9f, t => (Mathf.Sin(2 * Mathf.PI * Mathf.Lerp(160, 50, t / .9f) * t) * .7f + Noise() * .45f * Env(t, .002f, .08f)) * Env(t, .003f, .25f));
            Make("fanfare", 2.2f, t =>
            {
                float[] notes = { 523, 659, 784, 1047 };
                float s = 0;
                for (int i = 0; i < notes.Length; i++)
                {
                    float start = i * .14f;
                    if (t < start) continue;
                    float u = t - start, hold = i == notes.Length - 1 ? .9f : .2f;
                    s += (Square(notes[i] * u) * .1f + Mathf.Sin(2 * Mathf.PI * notes[i] * u) * .18f) * Env(u, .01f, hold);
                }
                if (t > .56f) { float u = t - .56f; s += (Mathf.Sin(2 * Mathf.PI * 659 * u) + Mathf.Sin(2 * Mathf.PI * 784 * u)) * .1f * Env(u, .02f, .7f); }
                return s;
            });
            // VR 传送门开场：门的嗡鸣（4 秒无缝循环：每个频率在 4 秒里都是整数个周期）、大象长鸣、上机甲的上升音
            Make("portal", 4f, t =>
            {
                float pad = Mathf.Sin(2 * Mathf.PI * 110 * t) * .3f + Mathf.Sin(2 * Mathf.PI * 164.75f * t) * .2f + Mathf.Sin(2 * Mathf.PI * 220.5f * t) * .16f;
                float shimmer = Mathf.Sin(2 * Mathf.PI * 880 * t) * (.5f + .5f * Mathf.Sin(2 * Mathf.PI * .5f * t)) * .05f +
                                Mathf.Sin(2 * Mathf.PI * 1320.25f * t) * (.5f + .5f * Mathf.Sin(2 * Mathf.PI * .75f * t)) * .035f;
                return (pad * (.85f + .15f * Mathf.Sin(2 * Mathf.PI * .25f * t)) + shimmer) * .55f;
            });
            {
                // 锯齿波过两个共振峰（像铜管），音高先往上滑再带颤音，最后往下落；加一点气声
                var formantA = new Resonance(820, 5); var formantB = new Resonance(1900, 7); var breath = new Resonance(2600, 2);
                double phase = 0;
                Make("trumpet", 1.6f, t =>
                {
                    float pitch = 230 + 190 * Mathf.SmoothStep(0, 1, t / .22f) - 70 * Mathf.SmoothStep(0, 1, (t - 1.05f) / .45f)
                                  + Mathf.Sin(2 * Mathf.PI * 5.5f * t) * 10 * Mathf.Clamp01((t - .3f) * 3);
                    phase += pitch / Rate;
                    float saw = (float)(phase - Math.Floor(phase)) * 2 - 1;
                    float brass = formantA.Step(saw) * 1.1f + formantB.Step(saw) * .7f + saw * .08f;
                    float air = breath.Step(Noise()) * .35f;
                    float env = Mathf.Clamp01(t / .06f) * Mathf.Clamp01((1.6f - t) / .35f);
                    return (float)Math.Tanh((brass + air) * 1.8f) * .5f * env;
                });
            }
            Make("rise", 1.3f, t =>
            {
                float u = t / 1.3f;
                float f = 180 * Mathf.Pow(10, u);   // 180 → 1800 Hz
                return (Mathf.Sin(2 * Mathf.PI * Phase("rise", f)) * .22f + Mathf.Sin(2 * Mathf.PI * Phase("rise2", f * 1.5f)) * .1f + Noise() * .08f * u)
                       * Mathf.Clamp01(t * 8) * Mathf.Clamp01((1.3f - t) * 4);
            });
        }

        /// <summary>二阶带通（共振峰），合成音色用。</summary>
        class Resonance
        {
            readonly float b0, a1, a2;
            float y1, y2;
            public Resonance(float freq, float q)
            {
                float w = 2 * Mathf.PI * freq / Rate, alpha = Mathf.Sin(w) / (2 * q), a0 = 1 + alpha;
                b0 = alpha / a0; a1 = -2 * Mathf.Cos(w) / a0; a2 = (1 - alpha) / a0;
            }
            float x1, x2;
            public float Step(float x)
            {
                float y = b0 * x - b0 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        /// <summary>合成好的音效（VR 开场要放在 3D 声源上循环）。</summary>
        public AudioClip Clip(string name) => clips.TryGetValue(name, out var clip) ? clip : null;

        readonly Dictionary<string, double> phases = new Dictionary<string, double>();
        float Phase(string key, float freq)
        {
            phases.TryGetValue(key, out var p);
            p += freq / Rate;
            phases[key] = p;
            return (float)(p - Math.Floor(p));
        }
        static float Saw(float phase) => phase * 2 - 1;
        static float Square(float cycles) => (cycles - Mathf.Floor(cycles)) < .5f ? 1 : -1;
        static float Arp(float t, float[] notes, float step)
        {
            int i = Mathf.Min(notes.Length - 1, (int)(t / step));
            float u = t - i * step;
            return Mathf.Sin(2 * Mathf.PI * notes[i] * u) * (i == notes.Length - 1 ? Mathf.Exp(-u / .2f) : 1) * Mathf.Clamp01(u * 200);
        }

        void Make(string name, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1, 1);
            var clip = AudioClip.Create("Diva Show " + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }

        public void PlayMusic(AudioClip clip, float gain = 1)
        {
            if (!music || !clip) return;
            music.Stop(); music.clip = clip; music.volume = volume * gain; music.Play();
            Played?.Invoke(clip, volume * gain);
        }

        /// <summary>Fade the music out (call every frame while fading).</summary>
        public void FadeMusic(float seconds)
        {
            if (!music || !music.isPlaying) return;
            music.volume = Mathf.MoveTowards(music.volume, 0, DivaClock.DeltaTime / Mathf.Max(.01f, seconds));
            if (music.volume <= 0) music.Stop();
        }

        readonly System.Collections.Generic.List<AudioSource> spatial = new System.Collections.Generic.List<AudioSource>();
        int nextSpatial;

        /// <summary>
        /// 3D 音效：从世界里的某一点发出（VR 里命中声从靶子那里来，烟火在头顶）。用 8 个声源轮流播。
        /// </summary>
        public void PlayAt(string name, Vector3 world, float gain = 1)
        {
            if (!source || !clips.TryGetValue(name, out var clip)) return;
            if (spatial.Count == 0)
                for (int i = 0; i < 8; i++)
                {
                    var go = new GameObject("Diva Show Sound 3D " + i);
                    go.transform.SetParent(source.transform, false);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false; s.spatialBlend = 1; s.rolloffMode = AudioRolloffMode.Linear;
                    s.minDistance = 3; s.maxDistance = 60; s.dopplerLevel = 0; s.ignoreListenerPause = true;
                    spatial.Add(s);
                }
            var src = spatial[nextSpatial++ % spatial.Count];
            src.transform.position = world;
            src.PlayOneShot(clip, volume * gain * 1.4f);
            Played?.Invoke(clip, volume * gain);
        }

        public void Play(string name, float gain = 1)
        {
            if (source && clips.TryGetValue(name, out var clip))
            {
                source.PlayOneShot(clip, volume * gain);
                Played?.Invoke(clip, volume * gain);
            }
        }
    }
}
