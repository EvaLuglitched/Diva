using UnityEngine;

namespace Diva
{
    /// <summary>
    /// 开关整套机甲。零件是骨骼的子物体（刚性跟随），由 Diva > Add D.Va Mech 生成并登记在这里。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class DivaMechToggle : MonoBehaviour
    {
        [Tooltip("Show or hide the whole mech suit.")]
        public bool showMech = true;

        [Tooltip("Generated armour parts, one per elephant bone.")]
        public GameObject[] parts = new GameObject[0];

        [Header("Elephant skin")]
        [Tooltip("Give the elephant's own skin a light candy-pink tint (the texture is unchanged).")]
        public bool candySkin = true;
        public Renderer skin;
        public Material originalSkin;
        public Material candyMaterial;
        [Tooltip("Set by DivaMechSkins at runtime: the chosen skin's elephant material (not saved).")]
        [System.NonSerialized] public Material skinOverride;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            if (parts != null)
                foreach (var part in parts)
                    if (part && part.activeSelf != showMech)
                        part.SetActive(showMech);
            if (skin && originalSkin && candyMaterial)
            {
                var want = candySkin ? (skinOverride ? skinOverride : candyMaterial) : originalSkin;
                if (skin.sharedMaterial != want) skin.sharedMaterial = want;
            }
        }

        public void SetVisible(bool visible)
        {
            showMech = visible;
            Apply();
        }
    }
}
