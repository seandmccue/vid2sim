using System.Collections.Generic;
using UnityEngine;

namespace Vid2Sim
{
    /// <summary>
    /// Solid panels for the screen HUD. The default IMGUI box is translucent grey, unreadable over a light
    /// scene, and tinting a texture through GUI.color proved unreliable in the web player, so each panel colour
    /// is baked into its own 2x2 texture and drawn as a box-style background.
    /// </summary>
    public static class HudPanel
    {
        public static readonly Color Dark = new(0.10f, 0.11f, 0.14f, 0.94f);
        static readonly Dictionary<Color, GUIStyle> _styles = new();

        public static void Draw(Rect r, Color? color = null)
        {
            var c = color ?? Dark;
            if (!_styles.TryGetValue(c, out var style))
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.SetPixels(new[] { c, c, c, c });
                tex.Apply();
                style = new GUIStyle { normal = { background = tex }, border = new RectOffset(0, 0, 0, 0) };
                _styles[c] = style;
            }
            GUI.Box(r, GUIContent.none, style);
        }
    }
}
