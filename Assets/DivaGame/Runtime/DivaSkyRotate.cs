using UnityEngine;

namespace Diva
{
    /// <summary>Slowly turns the starry skybox in Play mode (on a copy, so the material asset is never changed).</summary>
    public class DivaSkyRotate : MonoBehaviour
    {
        [Tooltip("Degrees per second.")] public float speed = .6f;
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

        void OnDisable()
        {
            if (copy && RenderSettings.skybox == copy) RenderSettings.skybox = original;
            if (copy) Destroy(copy);
            copy = null;
        }
    }
}
