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
        }

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
