using UnityEngine;

namespace BiolaCampus.Core
{
    /// <summary>
    /// Milestone 1 skeleton entry point: proves the project, packages, Web settings and CI build work.
    /// Later milestones replace this with the full campus bootstrap.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.72f, 0.92f);
            Debug.Log("[Biola] Skeleton running. Google key present: " + SecretsLoader.HasGoogleKey);
        }

        void OnGUI()
        {
            GUI.Label(new Rect(20, 20, 600, 30), "Biola Campus 3D — project skeleton (milestone 1)");
            GUI.Label(new Rect(20, 44, 600, 30), SecretsLoader.HasGoogleKey ? "Google Map Tiles key: found" : "Google Map Tiles key: missing (offline mode)");
        }
    }
}
