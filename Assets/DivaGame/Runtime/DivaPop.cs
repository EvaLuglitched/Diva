using UnityEngine;

namespace Diva
{
    /// <summary>A camera-facing sprite that grows and fades, then removes itself (hit bursts).</summary>
    public class DivaPop : MonoBehaviour
    {
        public float seconds = .4f;
        float born, size;
        SpriteRenderer sprite;

        public static void Spawn(Sprite image, Vector3 position, float size, Color? tint = null)
        {
            var go = new GameObject("Hit burst");
            go.transform.position = position;
            var pop = go.AddComponent<DivaPop>();
            pop.sprite = go.AddComponent<SpriteRenderer>();
            pop.sprite.sprite = image;
            pop.sprite.color = tint ?? new Color(1, .75f, .2f);
            pop.size = size / Mathf.Max(.01f, image.bounds.size.x);
            pop.born = Time.time;
        }

        void LateUpdate()
        {
            float t = (Time.time - born) / seconds;
            if (t >= 1) { Destroy(gameObject); return; }
            var camera = Camera.main;
            if (camera) transform.rotation = camera.transform.rotation;
            transform.localScale = Vector3.one * size * Mathf.Lerp(.4f, 1.2f, t);
            var color = sprite.color;
            color.a = 1 - t * t;
            sprite.color = color;
        }
    }
}
