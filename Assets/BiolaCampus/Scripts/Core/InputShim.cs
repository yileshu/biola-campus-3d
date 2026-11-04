using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BiolaCampus.Core
{
    /// <summary>
    /// Thin wrapper so the game works whether the project uses the new Input System (pulled in by
    /// Cesium for Unity), the legacy Input Manager, or both.
    /// </summary>
    public static class InputShim
    {
#if ENABLE_INPUT_SYSTEM
        static Key Map(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.W: return Key.W;
                case KeyCode.A: return Key.A;
                case KeyCode.S: return Key.S;
                case KeyCode.D: return Key.D;
                case KeyCode.Q: return Key.Q;
                case KeyCode.E: return Key.E;
                case KeyCode.F: return Key.F;
                case KeyCode.M: return Key.M;
                case KeyCode.N: return Key.N;
                case KeyCode.F3: return Key.F3;
                case KeyCode.L: return Key.L;
                case KeyCode.R: return Key.R;
                case KeyCode.C: return Key.C;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Space: return Key.Space;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                default: return Key.None;
            }
        }
#endif

        public static bool Held(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; var key = Map(k);
            if (kb != null && key != Key.None) return kb[key].isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(k);
#else
            return false;
#endif
        }

        public static bool Pressed(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current; var key = Map(k);
            if (kb != null && key != Key.None) return kb[key].wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(k);
#else
            return false;
#endif
        }

        /// <summary>Mouse movement this frame in "legacy axis units" (≈ pixels × 0.1).</summary>
        public static Vector2 MouseDelta
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                if (m != null) return m.delta.ReadValue() * 0.1f;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>Scroll wheel, positive = up, roughly ±1 per notch.</summary>
        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                if (m != null) return Mathf.Clamp(m.scroll.ReadValue().y / 120f, -3f, 3f);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#else
                return 0f;
#endif
            }
        }

        public static bool LeftClickPressed
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var m = Mouse.current;
                if (m != null) return m.leftButton.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.GetMouseButtonDown(0);
#else
                return false;
#endif
            }
        }
    }
}
