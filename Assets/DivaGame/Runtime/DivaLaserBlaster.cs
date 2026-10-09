using UnityEngine;
using UnityEngine.InputSystem;

namespace Diva
{
    /// <summary>
    /// The elephant's trunk blaster. Fire() shoots a laser from the trunk tip at the best standing
    /// target inside a cone ahead of the elephant: body-driven steering is too coarse for precise aim.
    /// Call Fire() from a gesture rule's On Triggered event, the HUD button, or Space for testing.
    /// </summary>
    public class DivaLaserBlaster : MonoBehaviour
    {
        [Tooltip("The elephant travel root; its forward is the aim direction.")]
        public Transform elephant;
        [Tooltip("Where the beam starts, normally the trunk-tip bone.")]
        public Transform muzzle;
        [Min(1)] public float range = 22;
        [Range(5, 90)] public float coneDegrees = 40;
        [Min(0)] public float cooldownSeconds = .35f;
        public Material beamMaterial;
        [Min(.01f)] public float beamWidth = .12f;
        [Min(.01f)] public float beamSeconds = .15f;
        public Sprite burstSprite;
        public AudioClip shotSound, hitSound;
        public bool spaceToFire = true;
        public int Shots { get; private set; }
        public int Hits { get; private set; }

        LineRenderer beam;
        AudioSource audioSource;
        float lastShot = -1000, beamUntil;

        void Awake()
        {
            beam = gameObject.AddComponent<LineRenderer>();
            beam.positionCount = 2;
            beam.useWorldSpace = true;
            beam.material = beamMaterial;
            beam.startWidth = beamWidth;
            beam.endWidth = beamWidth * .4f;
            beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            beam.enabled = false;
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0;
        }

        Vector3 Origin => muzzle ? muzzle.position : elephant.position + Vector3.up * 2;

        /// <summary>Best standing target in the cone: small angle first, then distance.</summary>
        public DivaTarget FindTarget()
        {
            Vector3 origin = Origin, forward = Vector3.ProjectOnPlane(elephant.forward, Vector3.up).normalized;
            DivaTarget best = null;
            float bestScore = float.MaxValue;
            foreach (var target in DivaTarget.All)
            {
                if (!target.IsUp) continue;
                Vector3 offset = target.AimPoint - origin;
                float distance = offset.magnitude;
                if (distance > range) continue;
                float angle = Vector3.Angle(forward, Vector3.ProjectOnPlane(offset, Vector3.up));
                if (angle > coneDegrees) continue;
                float score = angle / coneDegrees + distance / range;
                if (score < bestScore) { bestScore = score; best = target; }
            }
            return best;
        }

        public void Fire()
        {
            if (!elephant || Time.time - lastShot < cooldownSeconds) return;
            lastShot = Time.time;
            Shots++;
            var target = FindTarget();
            Vector3 origin = Origin;
            Vector3 end = target ? target.AimPoint : origin + Vector3.ProjectOnPlane(elephant.forward, Vector3.up).normalized * range;
            beam.SetPosition(0, origin);
            beam.SetPosition(1, end);
            beam.enabled = true;
            beamUntil = Time.time + beamSeconds;
            if (shotSound) audioSource.PlayOneShot(shotSound, .7f);
            if (target && target.TryHit())
            {
                Hits++;
                if (hitSound) audioSource.PlayOneShot(hitSound);
                if (burstSprite) DivaPop.Spawn(burstSprite, end, 2.2f);
            }
        }

        void Update()
        {
            if (spaceToFire && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Fire();
            if (beam.enabled)
            {
                beam.SetPosition(0, Origin);
                if (Time.time >= beamUntil) beam.enabled = false;
            }
        }
    }
}
