using UnityEngine;

namespace Diva
{
    /// <summary>
    /// 机甲音效：脚落地时的脚步声（走路动画和手势抬腿都会触发），以及喷射口的低声嗡鸣。
    /// 脚步按脚骨骼的高度判断：先抬高到 liftHeight 以上，再落回 landHeight 以下，算一步。
    /// </summary>
    [DisallowMultipleComponent]
    public class DivaMechAudio : MonoBehaviour
    {
        [Header("Footsteps")]
        public bool footsteps = true;
        [Tooltip("Foot bones (front paws and rear toes).")]
        public Transform[] feet = new Transform[0];
        public AudioClip[] stepClips = new AudioClip[0];
        [Range(0, 1)] public float stepVolume = .8f;
        [Tooltip("World units a foot must rise above its standing height before the next landing counts as a step.")]
        [Min(0)] public float liftHeight = .08f;
        [Tooltip("World units above standing height at which a lifted foot counts as landed.")]
        [Min(0)] public float landHeight = .03f;

        [Header("Thruster hum")]
        public bool thrusterHum = true;
        public AudioSource humSource;
        [Range(0, 1)] public float humVolume = .12f;
        [Tooltip("When set, the hum gets louder and higher as the thruster flames grow.")]
        public DivaBoosters boosters;

        AudioSource[] footSources;
        float[] standing;
        bool[] lifted;
        float[] lastStep;

        void Start()
        {
            int n = feet.Length;
            footSources = new AudioSource[n];
            standing = new float[n];
            lifted = new bool[n];
            lastStep = new float[n];
            for (int i = 0; i < n; i++)
            {
                if (!feet[i]) continue;
                standing[i] = Height(i);
                var go = new GameObject("Step Audio") { hideFlags = HideFlags.DontSave };
                go.transform.SetParent(feet[i], false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = .8f; s.minDistance = 2; s.maxDistance = 40; s.dopplerLevel = 0;
                footSources[i] = s;
            }
        }

        float Height(int i) => feet[i].position.y - transform.position.y;

        void LateUpdate()
        {
            if (footSources != null)
                for (int i = 0; i < feet.Length; i++)
                {
                    if (!feet[i] || !footSources[i]) continue;
                    float h = Height(i) - standing[i];
                    if (!lifted[i] && h > liftHeight) lifted[i] = true;
                    else if (lifted[i] && h < landHeight)
                    {
                        lifted[i] = false;
                        if (footsteps && Time.time - lastStep[i] > .15f) PlayStep(i);
                    }
                }
            if (humSource)
            {
                float boost = boosters ? boosters.Level : 0;
                humSource.volume = thrusterHum ? Mathf.Lerp(humVolume, Mathf.Min(1, humVolume * 4), boost) : 0;
                humSource.pitch = Mathf.Lerp(1, 1.6f, boost);
                if (thrusterHum && !humSource.isPlaying && humSource.isActiveAndEnabled) humSource.Play();
            }
        }

        void PlayStep(int i)
        {
            if (stepClips == null || stepClips.Length == 0) return;
            var clip = stepClips[Random.Range(0, stepClips.Length)];
            if (!clip) return;
            lastStep[i] = Time.time;
            var s = footSources[i];
            s.pitch = Random.Range(.92f, 1.08f);
            s.PlayOneShot(clip, stepVolume * Random.Range(.85f, 1f));
        }
    }
}
