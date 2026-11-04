using BiolaCampus.Osm;
using UnityEngine;
using UnityEngine.UI;

namespace BiolaCampus.UI
{
    /// <summary>
    /// Attribution that is visible on every screen (title, gameplay, pause): the Google mark required
    /// when displaying Google Photorealistic 3D Tiles, plus "© OpenStreetMap contributors".
    /// The per-tile data-provider credits (e.g. "Google, Airbus, ...") are rendered by Cesium's
    /// CesiumCreditSystem because the tileset has showCreditsOnScreen = true.
    ///
    /// Google requires its logo. Drop the official logo PNG (from Google's attribution guidelines) at
    /// Assets/Resources/Attribution/google_logo.png and it is used automatically; otherwise a text
    /// wordmark is shown.
    /// </summary>
    public class AttributionBar : MonoBehaviour
    {
        Text _google;

        public void Build(Canvas canvas, bool showGoogle)
        {
            var bar = UIFactory.Image("AttributionBar", canvas.transform, new Color(0, 0, 0, 0.45f)).rectTransform;
            bar.Place(new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-8, 34), new Vector2(520, 30));
            var h = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleRight;
            h.spacing = 12; h.padding = new RectOffset(10, 10, 2, 2);
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            bar.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (showGoogle)
            {
                var logo = Resources.Load<Texture2D>("Attribution/google_logo");
                if (logo != null)
                {
                    var img = UIFactory.Rect("GoogleLogo", bar).gameObject.AddComponent<RawImage>();
                    img.texture = logo;
                    var le = img.gameObject.AddComponent<LayoutElement>();
                    le.preferredHeight = 20; le.preferredWidth = 20f * logo.width / Mathf.Max(1, logo.height);
                }
                else
                {
                    _google = UIFactory.Label("Google", bar, "<b>Google</b>", 20, TextAnchor.MiddleLeft, Color.white);
                    _google.horizontalOverflow = HorizontalWrapMode.Overflow;
                }
            }

            var osm = UIFactory.Label("OSM", bar, (showGoogle ? "3D Tiles: Google  ·  " : "") + "Map data " + CampusMap.Attribution, 15, TextAnchor.MiddleLeft, Color.white);
            osm.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
