using UnityEngine;

namespace BiolaCampus.Player
{
    /// <summary>
    /// Scales the camera near plane with height above the campus ground so draped surfaces (roads,
    /// parking, fields) don't z-fight with the terrain when seen from the drone. 0.1 m when walking,
    /// up to 3 m at drone height.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class AdaptiveNearPlane : MonoBehaviour
    {
        public float GroundY;
        Camera _cam;

        void Awake() => _cam = GetComponent<Camera>();

        void LateUpdate()
        {
            float h = Mathf.Max(0f, transform.position.y - GroundY);
            _cam.nearClipPlane = Mathf.Clamp(h * 0.01f, 0.1f, 3f);
        }
    }
}
