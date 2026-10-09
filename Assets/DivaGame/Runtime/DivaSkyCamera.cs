using UnityEngine;
using DigiPhant;

namespace Diva
{
    /// <summary>
    /// Lifts the team's third-person camera (DivaDemo) so the sky shows: after DivaDemo aims the camera
    /// at the elephant each frame, this tilts it up and widens the view, keeping the elephant in the lower
    /// third and the rocket and clouds above the skyline in frame. DivaDemo itself is unchanged.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // after DivaDemo.LateUpdate
    public class DivaSkyCamera : MonoBehaviour
    {
        public Camera gameCamera;
        [Tooltip("Degrees to tilt the camera up after it looks at the elephant.")]
        [Range(0, 30)] public float lookUp = 16;
        [Range(40, 90)] public float fieldOfView = 62;
        DivaDemo demo;

        void LateUpdate()
        {
            if (!demo) demo = FindAnyObjectByType<DivaDemo>();
            if (!demo || !demo.isActiveAndEnabled) return;
            var cam = gameCamera ? gameCamera : demo.thirdPersonCamera;
            if (!cam) return;
            cam.fieldOfView = fieldOfView;
            cam.transform.rotation = Quaternion.AngleAxis(-lookUp, cam.transform.right) * cam.transform.rotation;
        }
    }
}
