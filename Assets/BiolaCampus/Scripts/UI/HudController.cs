using System.Collections.Generic;
using BiolaCampus.Core;
using BiolaCampus.Osm;
using BiolaCampus.Player;
using UnityEngine;
using UnityEngine.UI;

namespace BiolaCampus.UI
{
    /// <summary>
    /// In-game HUD: crosshair, building name for the building you look at / are next to,
    /// floating labels over nearby named buildings, mode + hints, loading progress and toasts.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        const float LookDistance = 220f;
        const float NearDistance = 18f;
        const float FloatingLabelRange = 160f;
        const int MaxFloatingLabels = 8;

        CampusMap _map;
        FirstPersonController _player;
        Canvas _canvas;
        RectTransform _root;
        Text _buildingName, _buildingSub, _mode, _hints, _toast, _loading, _crosshair;
        Image _buildingPanel;
        float _toastUntil;
        readonly List<Text> _floating = new List<Text>();
        CampusBuilding _shown;
        float _lookTimer;

        public void Build(Canvas canvas, CampusMap map)
        {
            _canvas = canvas;
            _map = map;
            _root = UIFactory.Rect("HUD", canvas.transform).Stretch();

            _crosshair = UIFactory.Label("Crosshair", _root, "+", 30, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.8f));
            _crosshair.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40, 40));
            _crosshair.Shadow();

            _buildingPanel = UIFactory.Image("BuildingPanel", _root, new Color(0.06f, 0.07f, 0.10f, 0.72f));
            _buildingPanel.rectTransform.Place(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 120), new Vector2(620, 84));
            var accent = UIFactory.Image("Accent", _buildingPanel.transform, UIFactory.AccentColor);
            accent.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, new Vector2(6, 0));
            _buildingName = UIFactory.Label("Name", _buildingPanel.transform, "", 34, TextAnchor.MiddleCenter);
            _buildingName.rectTransform.Place(new Vector2(0, 0.38f), new Vector2(1, 1), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _buildingName.Shadow();
            _buildingSub = UIFactory.Label("Sub", _buildingPanel.transform, "", 18, TextAnchor.MiddleCenter, UIFactory.MutedColor);
            _buildingSub.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0.4f), new Vector2(0.5f, 0.5f), new Vector2(0, 4), Vector2.zero);
            _buildingPanel.gameObject.SetActive(false);

            _mode = UIFactory.Label("Mode", _root, "", 22, TextAnchor.UpperLeft);
            _mode.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -16), new Vector2(700, 30));
            _mode.Shadow();
            _hints = UIFactory.Label("Hints", _root, "", 17, TextAnchor.UpperLeft, UIFactory.MutedColor);
            _hints.rectTransform.Place(new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -46), new Vector2(760, 26));
            _hints.Shadow();

            _toast = UIFactory.Label("Toast", _root, "", 22, TextAnchor.MiddleCenter, new Color(1f, 0.88f, 0.5f));
            _toast.rectTransform.Place(new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(1000, 60));
            _toast.Shadow();

            _loading = UIFactory.Label("Loading", canvas.transform, "", 26, TextAnchor.MiddleCenter);
            _loading.rectTransform.Place(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -80), new Vector2(1100, 120));
            _loading.Shadow();

            for (int i = 0; i < MaxFloatingLabels; i++)
            {
                var t = UIFactory.Label("Floating", _root, "", 18, TextAnchor.MiddleCenter);
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.rectTransform.anchorMin = t.rectTransform.anchorMax = Vector2.zero;
                t.rectTransform.sizeDelta = new Vector2(300, 26);
                t.Shadow();
                t.gameObject.SetActive(false);
                _floating.Add(t);
            }
            SetVisible(false);
        }

        public void SetPlayer(FirstPersonController p)
        {
            _player = p;
            p.FlyModeChanged += fly => Toast(fly ? "Drone mode — Space/E up, Ctrl/Q down, scroll = speed" : "Walking mode");
        }

        public void SetVisible(bool v) { if (_root != null) _root.gameObject.SetActive(v); }

        public void SetLoading(string text) { if (_loading != null) _loading.text = text ?? ""; }

        public void Toast(string text, float seconds = 4f)
        {
            if (_toast == null) return;
            _toast.text = text;
            _toastUntil = Time.unscaledTime + seconds;
        }

        void Update()
        {
            if (_toast != null && Time.unscaledTime > _toastUntil) _toast.text = "";
            if (_player == null || _root == null || !_root.gameObject.activeInHierarchy) return;

            _mode.text = _player.Flying ? "<b>DRONE</b>  ·  bird's-eye view" : "<b>WALK</b>";
            _hints.text = _player.Flying
                ? "WASD move · Space/E up · Ctrl/Q down · Shift fast · Scroll speed · F walk · N day/night · M map · Esc menu"
                : "WASD move · Shift sprint · Space jump · F drone · N day/night · M map · Esc menu";
            if (_player.NearBoundary) Toast("Edge of campus", 0.6f);

            _lookTimer -= Time.unscaledDeltaTime;
            if (_lookTimer <= 0f) { _lookTimer = 0.1f; UpdateBuildingLabel(); }
            UpdateFloatingLabels();
        }

        void UpdateBuildingLabel()
        {
            CampusBuilding target = null;
            string sub = null;
            var cam = _player.Camera;

            if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, LookDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Vector2 p = CampusMap.XZ(hit.point);
                // Facades: step 1.5 m into the wall so the hit lands inside the footprint.
                Vector2 inward = CampusMap.XZ(cam.transform.forward).normalized * 1.5f;
                target = _map.BuildingAt(p, 2.5f) ?? _map.BuildingAt(p + inward, 2.5f);
                if (target != null)
                {
                    sub = $"{hit.distance:0} m ahead";
                    if (float.IsNaN(target.CachedGroundY)) target.CachedGroundY = _player.transform.position.y;
                }
            }
            if (target == null)
            {
                var near = _map.NearestNamedBuilding(CampusMap.XZ(_player.transform.position), NearDistance, out float d);
                if (near != null) { target = near; sub = d < 0.5f ? "You are here" : $"Nearby · {d:0} m"; }
            }

            if (target != _shown) _shown = target;
            _buildingPanel.gameObject.SetActive(target != null);
            if (target != null) { _buildingName.text = target.Name; _buildingSub.text = sub; }
        }

        readonly List<(CampusBuilding b, float d)> _candidates = new List<(CampusBuilding, float)>();

        void UpdateFloatingLabels()
        {
            var cam = _player.Camera;
            bool enabled = GameSettings.ShowFloatingLabels && cam != null;
            _candidates.Clear();
            if (enabled)
            {
                Vector2 me = CampusMap.XZ(_player.transform.position);
                float range = _player.Flying ? FloatingLabelRange * 3f : FloatingLabelRange;
                foreach (var b in _map.Buildings)
                {
                    if (!b.HasName || b == _shown) continue;
                    float d = Vector2.Distance(me, b.Centroid);
                    if (d < range) _candidates.Add((b, d));
                }
                _candidates.Sort((x, y) => x.d.CompareTo(y.d));
            }

            var canvasRect = (RectTransform)_canvas.transform;
            float scale = canvasRect.rect.width / Mathf.Max(1, Screen.width);
            int used = 0;
            for (int i = 0; i < _candidates.Count && used < _floating.Count; i++)
            {
                var (b, d) = _candidates[i];
                float groundY = float.IsNaN(b.CachedGroundY) ? _player.GroundReferenceY : b.CachedGroundY;
                Vector3 world = new Vector3(b.Centroid.x, groundY + b.HeightMeters + 6f, b.Centroid.y);
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z <= 0 || sp.x < 0 || sp.x > Screen.width || sp.y < 0 || sp.y > Screen.height) continue;
                var t = _floating[used++];
                t.gameObject.SetActive(true);
                t.text = b.Name;
                t.fontSize = Mathf.RoundToInt(Mathf.Lerp(22, 14, d / (FloatingLabelRange * 2f)));
                t.color = new Color(1, 1, 1, Mathf.Lerp(1f, 0.55f, d / (FloatingLabelRange * 2f)));
                t.rectTransform.anchoredPosition = new Vector2(sp.x * scale, sp.y * scale);
            }
            for (int i = used; i < _floating.Count; i++) _floating[i].gameObject.SetActive(false);
        }
    }
}
