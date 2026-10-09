using UnityEngine;

namespace Diva
{
    /// <summary>Flies this object in a circle around a centre point, facing along the path, with a gentle bank and bob.</summary>
    public class DivaOrbit : MonoBehaviour
    {
        public Vector3 centre;
        [Min(1)] public float radius = 40;
        public float height = 75;
        [Tooltip("Metres per second along the circle.")] public float speed = 8;
        public float bobHeight = 2.5f;
        [Range(0, 45)] public float bankDegrees = 10;
        [Range(0, 360)] public float startAngle = 200;
        float angle;

        /// <summary>Current angle around the centre, in radians.</summary>
        public float Angle => angle;
        /// <summary>Radians per second.</summary>
        public float AngularSpeed => speed / radius;

        void OnEnable() { angle = startAngle * Mathf.Deg2Rad; Apply(0); }
        void Update() { angle += AngularSpeed * Time.deltaTime; Apply(Time.time); }

        /// <summary>Back to the start angle (used by the editor after building).</summary>
        public void Place(float time) { angle = startAngle * Mathf.Deg2Rad; Apply(time); }
        public void SetAngle(float radians, float time) { angle = radians; Apply(time); }

        public Vector3 PositionAt(float radians) =>
            centre + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * radius + Vector3.up * height;
        /// <summary>Direction of travel (counter-clockwise tangent) at an angle.</summary>
        public static Vector3 TangentAt(float radians) => new Vector3(-Mathf.Sin(radians), 0, Mathf.Cos(radians));

        void Apply(float time)
        {
            transform.position = PositionAt(angle) + Vector3.up * Mathf.Sin(time * .4f) * bobHeight;
            // Bank into the turn (towards the centre, which is on the left).
            transform.rotation = Quaternion.LookRotation(TangentAt(angle)) * Quaternion.Euler(0, 0, bankDegrees);
        }
    }
}
