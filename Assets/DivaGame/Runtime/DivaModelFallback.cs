using UnityEngine;

namespace Diva
{
    /// <summary>
    /// Shows a stand-in model when the preferred one is missing. The Fab rocket and clouds are not in git, so on a
    /// teammate's machine their prefab instances load as empty "Missing Prefab" placeholders; this switches to the
    /// kit rocket / Kenney cloud built next to them. Children are plain groups, so no reference points into a prefab.
    /// </summary>
    [ExecuteAlways]
    public class DivaModelFallback : MonoBehaviour
    {
        [Tooltip("Group holding the preferred (Fab) model.")] public Transform preferred;
        [Tooltip("Group holding the stand-in model, shown when the preferred one has nothing to draw.")] public Transform standIn;

        /// <summary>True when the preferred model loaded and has something to draw.</summary>
        public bool PreferredAvailable => preferred && preferred.GetComponentInChildren<Renderer>(true);

        void OnEnable() => Apply();

        public void Apply()
        {
            bool ok = PreferredAvailable;
            if (preferred && preferred.gameObject.activeSelf != ok) preferred.gameObject.SetActive(ok);
            if (standIn && standIn.gameObject.activeSelf == ok) standIn.gameObject.SetActive(!ok);
        }
    }
}
