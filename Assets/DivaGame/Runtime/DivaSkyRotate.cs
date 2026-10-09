using UnityEngine;

namespace Diva
{
    /// <summary>Slowly turns the starry skybox in Play mode (on a copy, so the material asset is never changed).</summary>
    public class DivaSkyRotate : MonoBehaviour
    {
        [Tooltip("Degrees per second.")] public float speed = .6f;
        [Tooltip("World azimuth (degrees, atan2(z, x)) of the painted planet at rotation 0; set by the builder.")]
        public float planetAzimuth;
        [Tooltip("Planet elevation in degrees; set by the builder.")] public float planetElevation;
        Material original, copy;

        void OnEnable()
        {
            original = RenderSettings.skybox;
            if (!original || !original.HasProperty("_Rotation")) return;
            copy = new Material(original) { name = original.name + " (turning)" };
            RenderSettings.skybox = copy;
        }

        void Update()
        {
            if (copy) copy.SetFloat("_Rotation", Mathf.Repeat(copy.GetFloat("_Rotation") + speed * Time.deltaTime, 360));
        }

        /// <summary>Turns the sky to an angle (degrees); the sky keeps turning from there.</summary>
        public void SetRotation(float degrees)
        {
            if (copy) copy.SetFloat("_Rotation", Mathf.Repeat(degrees, 360));
        }

        /// <summary>The rotation that puts the planet at a world azimuth (degrees) after `seconds` of turning.</summary>
        public float RotationForPlanet(float worldAzimuth, float seconds) => worldAzimuth - planetAzimuth - speed * seconds;

        /// <summary>World direction of the planet at a sky rotation (degrees).</summary>
        public Vector3 PlanetDirection(float rotation)
        {
            float a = (planetAzimuth + rotation) * Mathf.Deg2Rad, e = planetElevation * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(e) * Mathf.Cos(a), Mathf.Sin(e), Mathf.Cos(e) * Mathf.Sin(a));
        }

        void OnDisable()
        {
            if (copy && RenderSettings.skybox == copy) RenderSettings.skybox = original;
            if (copy) Destroy(copy);
            copy = null;
        }
    }
}
