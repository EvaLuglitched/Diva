using UnityEngine;

namespace Diva
{
    /// <summary>Gentle idle motion for props: bobbing and spinning.</summary>
    public class DivaFloat : MonoBehaviour
    {
        public float bobHeight = .3f, bobSpeed = .6f, spinDegreesPerSecond;
        Vector3 start;
        float phase;
        void OnEnable() { start = transform.localPosition; phase = (start.x + start.z) * .37f; }
        void Update()
        {
            transform.localPosition = start + Vector3.up * Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight;
            if (spinDegreesPerSecond != 0) transform.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.World);
        }
    }
}
