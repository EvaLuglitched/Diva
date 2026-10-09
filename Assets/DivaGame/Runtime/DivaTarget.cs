using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diva
{
    public enum TargetMotion { Static, Swing, Slide, Hover }

    /// <summary>A shooting target beside the route. Hit it to knock it down; it pops back up after a delay.</summary>
    public class DivaTarget : MonoBehaviour
    {
        public static readonly List<DivaTarget> All = new List<DivaTarget>();
        public static event Action<DivaTarget> Hit;

        [Tooltip("The part that moves and falls; the stand stays put.")]
        public Transform visual;
        public TargetMotion motion = TargetMotion.Static;
        [Tooltip("Swing: degrees. Slide: units sideways. Hover: units up and down.")]
        public float amplitude = 1;
        public float speed = 1;
        public int points = 10;
        [Min(.5f)] public float respawnSeconds = 6;
        [Tooltip("Optional neon glow; it fades out while the target is down.")]
        public Light glow;
        public bool IsUp { get; private set; } = true;
        public Vector3 AimPoint => visual ? visual.position : transform.position;

        Vector3 basePosition, baseScale;
        Quaternion baseRotation;
        float phase, downAt, fall, glowIntensity;

        void OnEnable()
        {
            if (!visual) visual = transform;
            basePosition = visual.localPosition;
            baseRotation = visual.localRotation;
            baseScale = visual.localScale;
            if (glow) glowIntensity = glow.intensity;
            // Desynchronise neighbouring targets without randomness, so runs are repeatable.
            phase = (transform.position.x * 1.7f + transform.position.z * .9f) % (2 * Mathf.PI);
            IsUp = true;
            fall = 0;
            All.Add(this);
        }
        void OnDisable() => All.Remove(this);

        public bool TryHit()
        {
            if (!IsUp) return false;
            IsUp = false;
            downAt = Time.time;
            Hit?.Invoke(this);
            return true;
        }

        public void ResetTarget()
        {
            IsUp = true;
            fall = 0;
        }

        void Update()
        {
            if (!IsUp && Time.time - downAt >= respawnSeconds) IsUp = true;
            fall = Mathf.MoveTowards(fall, IsUp ? 0 : 1, Time.deltaTime * (IsUp ? 2 : 5));
            float t = Time.time * speed + phase;
            Vector3 position = basePosition;
            Quaternion rotation = baseRotation;
            switch (motion)
            {
                case TargetMotion.Swing: rotation *= Quaternion.Euler(0, 0, Mathf.Sin(t) * amplitude); break;
                case TargetMotion.Slide: position += Vector3.right * Mathf.Sin(t) * amplitude; break;
                case TargetMotion.Hover:
                    position += Vector3.up * Mathf.Sin(t * 1.3f) * amplitude;
                    rotation *= Quaternion.Euler(0, Mathf.Sin(t * .5f) * 25, 0);
                    break;
            }
            float eased = fall * fall * (3 - 2 * fall);
            if (motion == TargetMotion.Hover)
            {
                // Drones drop and shrink; boards tip backwards on their hinge.
                position += Vector3.down * eased * 2.5f;
                visual.localScale = baseScale * Mathf.Lerp(1, .2f, eased);
            }
            else rotation *= Quaternion.Euler(-85 * eased, 0, 0);
            visual.localPosition = position;
            visual.localRotation = rotation;
            if (glow) glow.intensity = glowIntensity * (1 - eased);
        }
    }
}
