using UnityEngine;

/// <summary>
/// Optional replacement models for the game layer. Drag a prefab (for example from a pack you
/// downloaded) into a slot, then run Diva > Game > Build Game Layer: every object of that kind is
/// replaced by your prefab at the same position, scaled to the same height. Empty slots keep the
/// Diva town kit (Assets/DivaGame/Models/Town, built in Blender from Art/Blender/build_town_kit.py).
/// </summary>
[CreateAssetMenu(menuName = "Diva/Model Slots")]
public class DivaModelSlots : ScriptableObject
{
    [Header("Town (outside the course)")]
    [Tooltip("Replaces the row townhouses; several prefabs are used in turn.")] public GameObject[] houses;
    [Tooltip("Replaces the tall tech buildings of the skyline.")] public GameObject techBuilding;
    [Tooltip("Replaces the clock tower landmark.")] public GameObject landmark;

    [Header("Plaza props (same positions)")]
    public GameObject billboard;
    public GameObject kiosk;
    public GameObject planter;
    public GameObject crates;

    [Header("Game pieces")]
    [Tooltip("Replaces the arches over the path.")] public GameObject gate;
    [Tooltip("Replaces the hinged target boards.")] public GameObject targetBoard;
    [Tooltip("Replaces the hovering target drones.")] public GameObject targetDrone;

    [Header("Sky")]
    [Tooltip("Replaces the flying rocket. Its nose axis is set below.")] public GameObject rocket;
    [Tooltip("Which local axis of the rocket prefab is its nose (most rockets: up).")] public Vector3 rocketNoseAxis = Vector3.up;
    [Min(1)] public float rocketLength = 70;
    [Tooltip("Replace the clouds; several prefabs are used in turn.")] public GameObject[] clouds;

    [Header("Course scenery (positions unchanged; the original is hidden, not deleted)")]
    [Tooltip("Replaces the 18 acacia trees of the instructor's course.")] public GameObject courseTree;
}
