using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BiolaCampus.UI
{
    /// <summary>Builds uGUI hierarchies in code so the project needs no hand-authored prefabs.</summary>
    public static class UIFactory
    {
        static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
                    if (_font == null) try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
                }
                return _font;
            }
        }

        public static readonly Color PanelColor = new Color(0.06f, 0.07f, 0.10f, 0.86f);
        public static readonly Color AccentColor = new Color(0.80f, 0.13f, 0.20f, 1f);   // cardinal red
        public static readonly Color TextColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        public static readonly Color MutedColor = new Color(0.72f, 0.74f, 0.78f, 1f);

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static Canvas Canvas(string name, int sortOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortOrder;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1600, 900);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Place(this RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(string name, Transform parent, Color color)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Label(string name, Transform parent, string text, int size, TextAnchor align, Color? color = null)
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = color ?? TextColor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        public static Outline Shadow(this Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.85f);
            o.effectDistance = new Vector2(1.5f, -1.5f);
            return o;
        }

        public static Button Button(string name, Transform parent, string label, Action onClick, int fontSize = 26)
        {
            var img = Image(name, parent, Color.white);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.10f);
            colors.highlightedColor = new Color(0.80f, 0.13f, 0.20f, 0.85f);
            colors.pressedColor = new Color(0.60f, 0.10f, 0.15f, 1f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.16f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Label("Label", img.transform, label, fontSize, TextAnchor.MiddleCenter);
            t.rectTransform.Stretch();
            return b;
        }

        /// <summary>Horizontal slider with a caption and live value readout.</summary>
        public static Slider Slider(string name, Transform parent, string caption, float min, float max, float value,
            Func<float, string> format, Action<float> onChange)
        {
            var row = Rect(name, parent);
            var cap = Label("Caption", row, caption, 22, TextAnchor.MiddleLeft);
            cap.rectTransform.Place(new Vector2(0, 0), new Vector2(0.38f, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            var val = Label("Value", row, format(value), 22, TextAnchor.MiddleRight, MutedColor);
            val.rectTransform.Place(new Vector2(0.86f, 0), new Vector2(1, 1), new Vector2(1, 0.5f), Vector2.zero, Vector2.zero);

            var track = Rect("Slider", row).Place(new Vector2(0.40f, 0.5f), new Vector2(0.84f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0, 28));
            var bg = Image("Background", track, new Color(1, 1, 1, 0.15f));
            bg.rectTransform.Place(new Vector2(0, 0.35f), new Vector2(1, 0.65f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var fillArea = Rect("Fill Area", track).Stretch();
            fillArea.offsetMin = new Vector2(0, 10); fillArea.offsetMax = new Vector2(0, -10);
            var fill = Image("Fill", fillArea, AccentColor);
            fill.rectTransform.Place(Vector2.zero, new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            var handleArea = Rect("Handle Area", track).Stretch();
            var handle = Image("Handle", handleArea, Color.white);
            handle.rectTransform.Place(new Vector2(0, 0), new Vector2(0, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 0));

            var s = track.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.direction = UnityEngine.UI.Slider.Direction.LeftToRight;
            s.minValue = min; s.maxValue = max; s.value = value;
            s.onValueChanged.AddListener(v => { val.text = format(v); onChange(v); });
            return s;
        }

        /// <summary>A button that cycles through options (simpler and more robust than a runtime-built Dropdown).</summary>
        public static Button Cycler(string name, Transform parent, string caption, string[] options, int index, Action<int> onChange)
        {
            var row = Rect(name, parent);
            var cap = Label("Caption", row, caption, 22, TextAnchor.MiddleLeft);
            cap.rectTransform.Place(new Vector2(0, 0), new Vector2(0.38f, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
            int current = index;
            Button b = null;
            b = Button("Value", row, "◀  " + options[current] + "  ▶", null, 22);
            ((RectTransform)b.transform).Place(new Vector2(0.40f, 0.1f), new Vector2(1f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var label = b.GetComponentInChildren<Text>();
            b.onClick.AddListener(() =>
            {
                current = (current + 1) % options.Length;
                label.text = "◀  " + options[current] + "  ▶";
                onChange(current);
            });
            return b;
        }

        public static VerticalLayoutGroup VerticalLayout(GameObject go, float spacing, int padding)
        {
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(padding, padding, padding, padding);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperCenter;
            return v;
        }

        public static LayoutElement Height(this Component c, float h)
        {
            var le = c.gameObject.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = h; le.minHeight = h;
            return le;
        }

        /// <summary>A white triangle (pointing up) used for the minimap player marker.</summary>
        public static Sprite ArrowSprite()
        {
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            Vector2 a = new Vector2(S * 0.5f, S - 3), b = new Vector2(6, 4), c = new Vector2(S - 6, 4), notch = new Vector2(S * 0.5f, S * 0.28f);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = InTri(p, a, b, notch) || InTri(p, a, notch, c);
                    px[y * S + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
