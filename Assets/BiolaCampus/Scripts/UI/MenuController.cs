using System;
using BiolaCampus.Core;
using BiolaCampus.Osm;
using UnityEngine;
using UnityEngine.UI;

namespace BiolaCampus.UI
{
    /// <summary>Title screen + pause menu with Controls, Settings and Credits pages.</summary>
    public class MenuController : MonoBehaviour
    {
        public event Action StartRequested, ResumeRequested, RespawnRequested, TitleRequested;

        public bool IsOpen => _root != null && _root.gameObject.activeSelf;
        public bool IsPauseMenu { get; private set; }

        RectTransform _root, _column, _main, _controls, _settings, _credits;
        Text _title, _subtitle, _status;
        Button _startButton, _respawnButton, _titleButton;
        Image _backdrop;
        CampusMap _map;

        public const string ControlsText =
            "<b>Walking</b>\n" +
            "W A S D / arrow keys — move\n" +
            "Mouse — look around (click the view to capture the mouse)\n" +
            "Shift — sprint\n" +
            "Space — jump\n\n" +
            "<b>Drone / fly mode</b>\n" +
            "F — toggle drone mode\n" +
            "Space or E — climb,  Ctrl, Q or C — descend\n" +
            "Shift — fast,  mouse wheel — change cruise speed\n\n" +
            "<b>Other</b>\n" +
            "N — toggle day / night (street lamps and windows light up)\n" +
            "M or Tab — enlarge / shrink the campus map\n" +
            "F3 — renderer diagnostics (tiles, FPS)\n" +
            "Esc — release the mouse and open this menu\n\n" +
            "Look at or walk up to a building to see its name.\n" +
            "The yellow line on the map is the campus boundary.";

        public void Build(Canvas canvas, CampusMap map)
        {
            _map = map;
            _root = UIFactory.Rect("Menu", canvas.transform).Stretch();
            _backdrop = UIFactory.Image("Backdrop", _root, new Color(0, 0, 0, 0.25f));
            _backdrop.rectTransform.Stretch();
            _backdrop.raycastTarget = true;

            var colImg = UIFactory.Image("Column", _root, UIFactory.PanelColor);
            _column = colImg.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(60, 0), new Vector2(600, -120));
            var stripe = UIFactory.Image("Stripe", _column, UIFactory.AccentColor);
            stripe.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 8));

            _title = UIFactory.Label("Title", _column, "BIOLA UNIVERSITY\n<size=30>3D Campus Explorer</size>", 50, TextAnchor.UpperLeft);
            _title.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(40, -40), new Vector2(-80, 130));
            _title.Shadow();
            _subtitle = UIFactory.Label("Subtitle", _column, "La Mirada, California  ·  unofficial fan-made recreation", 19, TextAnchor.UpperLeft, UIFactory.MutedColor);
            _subtitle.rectTransform.Place(new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(40, -172), new Vector2(-80, 30));

            _main = Page("Main");
            _startButton = AddButton(_main, "Start", () => (IsPauseMenu ? ResumeRequested : StartRequested)?.Invoke());
            _respawnButton = AddButton(_main, "Respawn at campus centre", () => RespawnRequested?.Invoke());
            AddButton(_main, "Controls", () => Show(_controls));
            AddButton(_main, "Settings", () => Show(_settings));
            AddButton(_main, "Credits", () => Show(_credits));
            _titleButton = AddButton(_main, "Back to title screen", () => TitleRequested?.Invoke());

            _status = UIFactory.Label("Status", _column, "", 17, TextAnchor.LowerLeft, new Color(1f, 0.85f, 0.45f));
            _status.rectTransform.Place(new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(40, 28), new Vector2(-80, 110));

            _controls = Page("Controls");
            AddText(_controls, ControlsText, 21, 600);
            AddButton(_controls, "Back", () => Show(_main));

            _settings = Page("Settings");
            BuildSettings(_settings);
            AddButton(_settings, "Back", () => Show(_main));

            _credits = Page("Credits");
            AddText(_credits, CreditsText(map), 18, 560);
            var links = UIFactory.Rect("Links", _credits);
            links.Height(44);
            var h = links.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10; h.childForceExpandWidth = true; h.childControlWidth = true; h.childControlHeight = true;
            UIFactory.Button("OSM", links, "OSM copyright", () => Application.OpenURL("https://www.openstreetmap.org/copyright"), 18);
            UIFactory.Button("Cesium", links, "Cesium for Unity", () => Application.OpenURL("https://cesium.com/platform/cesium-for-unity/"), 18);
            UIFactory.Button("Google", links, "Google Maps terms", () => Application.OpenURL("https://cloud.google.com/maps-platform/terms"), 18);
            AddButton(_credits, "Back", () => Show(_main));

            Show(_main);
            OpenTitle();
        }

        RectTransform Page(string name)
        {
            var rt = UIFactory.Rect(name, _column).Place(new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -215), new Vector2(-60, -330));
            UIFactory.VerticalLayout(rt.gameObject, 12, 10);
            return rt;
        }

        Button AddButton(RectTransform page, string label, Action onClick)
        {
            var b = UIFactory.Button(label, page, label, onClick);
            b.Height(54);
            return b;
        }

        Text AddText(RectTransform page, string text, int size, float height)
        {
            var t = UIFactory.Label("Text", page, text, size, TextAnchor.UpperLeft);
            t.Height(height);
            return t;
        }

        void BuildSettings(RectTransform page)
        {
            UIFactory.Slider("Sensitivity", page, "Mouse sensitivity", 0.1f, 5f, GameSettings.MouseSensitivity,
                v => v.ToString("0.00") + "×", v => GameSettings.MouseSensitivity = v).transform.parent.GetComponent<RectTransform>().Height(52);
            UIFactory.Slider("FOV", page, "Field of view", 50f, 100f, GameSettings.FieldOfView,
                v => v.ToString("0") + "°", v => GameSettings.FieldOfView = v).transform.parent.GetComponent<RectTransform>().Height(52);
            UIFactory.Cycler("Quality", page, "Quality level", new[] { "Low (fast)", "Medium", "High (sharp)" }, (int)GameSettings.Quality,
                i => GameSettings.Quality = (TileQuality)i).transform.parent.GetComponent<RectTransform>().Height(52);
            UIFactory.Cycler("InvertY", page, "Invert mouse Y", new[] { "Off", "On" }, GameSettings.InvertY ? 1 : 0,
                i => GameSettings.InvertY = i == 1).transform.parent.GetComponent<RectTransform>().Height(52);
            UIFactory.Cycler("Labels", page, "Floating labels", new[] { "On", "Off" }, GameSettings.ShowFloatingLabels ? 0 : 1,
                i => GameSettings.ShowFloatingLabels = i == 0).transform.parent.GetComponent<RectTransform>().Height(52);
            var note = UIFactory.Label("Note", page,
                "Low: no shadows/bloom, fastest. Medium (web default): soft shadows, bloom, ACES. High: SSAO, sharper shadows, SMAA. " +
                "Also sets Google 3D Tiles detail (screen-space error 12 / 6 / 4).", 16, TextAnchor.UpperLeft, UIFactory.MutedColor);
            note.Height(70);
        }

        public static string CreditsText(CampusMap map)
        {
            string osmDate = map?.Meta != null && !string.IsNullOrEmpty(map.Meta.fetchedUtc) ? $" (snapshot {map.Meta.fetchedUtc})" : "";
            return
                "<b>3D imagery & terrain</b>\nGoogle Photorealistic 3D Tiles, streamed live via the Google Maps Platform Map Tiles API. " +
                "© Google and the data providers listed at the bottom of the screen. Tiles are streamed only, never stored.\n\n" +
                "<b>Map data</b>\n© OpenStreetMap contributors" + osmDate + ". Building names, paths and the campus boundary come from " +
                "OpenStreetMap, available under the Open Database License (ODbL 1.0).\n\n" +
                "<b>Engine & geospatial runtime</b>\nBuilt with Cesium for Unity (© Cesium GS, Inc., Apache License 2.0) and Unity.\n" +
                "coi-serviceworker © Guido Zuidhof (MIT) enables multithreading on static hosts.\n\n" +
                "<b>Disclaimer</b>\nAn unofficial, non-commercial fan project. Not affiliated with or endorsed by Biola University or Google.";
        }

        void Show(RectTransform page)
        {
            bool main = page == _main;
            foreach (var p in new[] { _main, _controls, _settings, _credits })
            {
                if (p == null) continue;
                p.gameObject.SetActive(p == page);
                // Main page sits under the title; sub-pages use the whole column.
                p.offsetMax = new Vector2(-30, main ? -215 : -30);
                p.offsetMin = new Vector2(30, main ? 115 : 20);
            }
            _title.gameObject.SetActive(main);
            _subtitle.gameObject.SetActive(main);
            _status.gameObject.SetActive(main);
        }

        public void SetStatus(string text) { if (_status != null) _status.text = text ?? ""; }

        public void OpenTitle()
        {
            IsPauseMenu = false;
            _root.gameObject.SetActive(true);
            _backdrop.color = new Color(0, 0, 0, 0.15f);
            _startButton.GetComponentInChildren<Text>().text = "Start";
            _respawnButton.gameObject.SetActive(false);
            _titleButton.gameObject.SetActive(false);
            Show(_main);
        }

        public void OpenPause()
        {
            IsPauseMenu = true;
            _root.gameObject.SetActive(true);
            _backdrop.color = new Color(0, 0, 0, 0.45f);
            _startButton.GetComponentInChildren<Text>().text = "Resume";
            _respawnButton.gameObject.SetActive(true);
            _titleButton.gameObject.SetActive(true);
            Show(_main);
        }

        public void Close() => _root.gameObject.SetActive(false);
    }
}
