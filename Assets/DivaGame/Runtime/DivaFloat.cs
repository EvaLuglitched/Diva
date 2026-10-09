using UnityEngine;

namespace Diva
{
    /// <summary>Gentle idle motion for props: bobbing, slow horizontal drift and spinning.</summary>
    public class DivaFloat : MonoBehaviour
    {
        public float bobHeight = .3f, bobSpeed = .6f, spinDegreesPerSecond;
        [Tooltip("Radius of a slow horizontal loop, for floating clouds.")] public float driftRadius, driftSpeed = .05f;
        Vector3 start;
        float phase;
        void OnEnable() { start = transform.localPosition; phase = (start.x + start.z) * .37f; }
        void Update()
        {
            float drift = Time.time * driftSpeed + phase;
            transform.localPosition = start + Vector3.up * Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight
                + new Vector3(Mathf.Cos(drift), 0, Mathf.Sin(drift * .8f)) * driftRadius;
            if (spinDegreesPerSecond != 0) transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
