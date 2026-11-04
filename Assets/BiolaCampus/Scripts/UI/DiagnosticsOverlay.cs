using BiolaCampus.Core;
using BiolaCampus.World;
using UnityEngine;
using UnityEngine.UI;

namespace BiolaCampus.UI
{
    /// <summary>
    /// Always-visible one-line renderer status ("Google 3D Tiles: streaming, 412 tiles" or
    /// "FALLBACK: OSM boxes — HTTP 403 key rejected ...") plus an F3 detail panel with FPS, tile
    /// load progress, page origin and quality. State changes are also logged as [BiolaDiag] so CI
    /// (Playwright) can read them from the browser console.
    /// </summary>
    public class DiagnosticsOverlay : MonoBehaviour
    {
        CampusWorld _world;
        Text _line, _detail;
        Image _bg;
        bool _expanded;
        float _fpsAccum, _fpsTime, _fps;
        int _fpsFrames;
        float _logTimer;
        string _lastSummary;

        public static DiagnosticsOverlay Instance { get; private set; }
        public float Fps => _fps;

        public void Build(Canvas canvas, CampusWorld world)
        {
            Instance = this;
            _world = world;
            _bg = UIFactory.Image("Diagnostics", canvas.transform, new Color(0, 0, 0, 0.5f));
            _bg.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(8, 68), new Vector2(760, 26));
            _line = UIFactory.Label("Line", _bg.transform, "", 15, TextAnchor.MiddleLeft);
            _line.rectTransform.Stretch(4);
            _line.horizontalOverflow = HorizontalWrapMode.Overflow;
            _detail = UIFactory.Label("Detail", _bg.transform, "", 14, TextAnchor.LowerLeft, UIFactory.MutedColor);
            _detail.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(4, 4), new Vector2(-8, 150));
            _detail.Shadow();
            _detail.gameObject.SetActive(false);
        }

        string Summary()
        {
            switch (_world.State)
            {
                case CampusWorld.TileState.NoKey:
                    return "<color=#ffb347>FALLBACK: offline OSM boxes</color> — " + _world.FailureCause;
                case CampusWorld.TileState.Failed:
                    return "<color=#ff6b6b>FALLBACK: offline OSM boxes</color> — " + _world.FailureCause;
                case CampusWorld.TileState.Streaming:
                    return $"<color=#7CFC9A>Google Photorealistic 3D Tiles: streaming</color> · {_world.TilesCreated} tiles · {_world.LoadProgress:0}% loaded";
                default:
                    return "Google Photorealistic 3D Tiles: requesting root.json…";
            }
        }

        void Update()
        {
            if (_world == null) return;
            if (InputShim.Pressed(KeyCode.F3)) { _expanded = !_expanded; _detail.gameObject.SetActive(_expanded); }

            _fpsFrames++; _fpsTime += Time.unscaledDeltaTime;
            if (_fpsTime >= 0.5f) { _fps = _fpsFrames / _fpsTime; _fpsFrames = 0; _fpsTime = 0; }

            string summary = Summary();
            _line.text = summary + $"   <color=#aaaaaa>{_fps:0} fps · F3</color>";
            _bg.rectTransform.sizeDelta = new Vector2(Mathf.Min(1400, _line.preferredWidth + 16), 26);

            if (_expanded)
                _detail.text =
                    $"Page: {CampusWorld.PageOrigin()}\n" +
                    $"Tiles created: {_world.TilesCreated}   load: {_world.LoadProgress:0.0}%   first tile: {(_world.FirstTileTime < 0 ? "—" : _world.FirstTileTime.ToString("0.0") + " s")}\n" +
                    $"Quality: {GameSettings.Quality}   Unity quality: {QualitySettings.names[QualitySettings.GetQualityLevel()]}\n" +
                    $"Graphics: {SystemInfo.graphicsDeviceType} · {SystemInfo.graphicsDeviceName}\n" +
                    $"Resolution: {Screen.width}×{Screen.height}   FPS: {_fps:0.0}";

            // Periodic console line for CI logs; immediate on state changes.
            _logTimer -= Time.unscaledDeltaTime;
            string plain = System.Text.RegularExpressions.Regex.Replace(summary, "<.*?>", "");
            if (plain != _lastSummary && (_lastSummary == null || _world.State != CampusWorld.TileState.Streaming || _logTimer <= 0f))
            {
                _lastSummary = plain;
                _logTimer = 10f;
                CampusWorld.Diag(plain + $" · {_fps:0} fps");
            }
        }
    }
}
