using UnityEngine;

namespace Diva
{
    /// <summary>Keeps this object at a fixed offset from a target, e.g. a light that keeps the elephant readable.</summary>
    public class DivaFollow : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0, 6, -2);
        void LateUpdate()
        {
            if (target) transform.position = target.position + target.rotation * offset;
        }
    }
}
