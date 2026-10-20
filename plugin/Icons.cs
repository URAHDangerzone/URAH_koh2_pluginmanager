using UnityEngine;

namespace URAH_PluginManager
{
    /// <summary>
    /// The row buttons' icons: the game's white button glyphs from assets/ui/elements/common/icons (the
    /// minus of the Exile button for Remove, the plus of Hire for Install, the circle of arrows of
    /// ModeChange for Update), loaded like Defs loads "path:name#2". Tinted by the Image to the
    /// button's text color. If a game update moves one, a drawn stand-in takes its place: white shapes
    /// on transparent, anti-aliased from distances, capsules on a 64 px grid, y up.
    /// </summary>
    internal static class Icons
    {
        private const int Size = 64;
        private const float Line = 3.2f;

        private static Sprite remove, install, update;

        private const string Glyphs = "Assets/UI/Elements/Common/Icons/";

        public static Sprite Remove => remove != null ? remove : (remove = Game(Glyphs + "Icon_Action_Exile.png", "Icon_Action_Exile") ?? Make(TrashShape));
        public static Sprite Install => install != null ? install : (install = Game(Glyphs + "Icon_Action_Hire.png", "Icon_Action_Hire") ?? Make(InstallShape));
        public static Sprite Update => update != null ? update : (update = Game(Glyphs + "Icon_Action_Change.png", "Icon_Action_Change") ?? Make(UpdateShape));

        private static Sprite Game(string path, string name)
        {
            try
            {
                if (Assets.GetObject(path, name, 2) is Sprite s && s != null)
                {
                    return s;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Icon {name}: {e.Message}");
            }
            Plugin.Log.LogWarning($"Icon {name} not found, drawn stand-in used");
            return null;
        }

        private delegate float Shape(Vector2 p);

        private static Sprite Make(Shape shape)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float d = shape(new Vector2(x + 0.5f, y + 0.5f));
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    pixels[y * Size + x] = new Color32(255, 255, 255, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            Sprite s = Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        /// <summary>Distance to a capsule from a to b with radius r (negative inside).</summary>
        private static float Capsule(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return (p - (a + t * ab)).magnitude - r;
        }

        private static float Lines(Vector2 p, float r, params Vector2[] pts)
        {
            float d = float.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i += 2)
            {
                d = Mathf.Min(d, Capsule(p, pts[i], pts[i + 1], r));
            }
            return d;
        }

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        /// <summary>Lid with handle, tapered can with two thinner ribs.</summary>
        private static float TrashShape(Vector2 p) => Mathf.Min(
            Lines(p, Line,
                V(14, 46), V(50, 46),      // lid
                V(24, 54), V(40, 54),      // handle
                V(24, 54), V(24, 46),
                V(40, 54), V(40, 46),
                V(18, 40), V(21, 10),      // can
                V(46, 40), V(43, 10),
                V(21, 10), V(43, 10)),
            Lines(p, 2.2f,
                V(28, 34), V(28.5f, 17),   // ribs
                V(36, 34), V(35.5f, 17)));

        /// <summary>Arrow down into a tray.</summary>
        private static float InstallShape(Vector2 p) => Lines(p, Line,
            V(32, 54), V(32, 24),          // shaft
            V(32, 22), V(21, 33),          // head
            V(32, 22), V(43, 33),
            V(13, 21), V(13, 10),          // tray
            V(13, 10), V(51, 10),
            V(51, 10), V(51, 21));

        /// <summary>A circle of arrows: an arc with an arrowhead at its end.</summary>
        private static float UpdateShape(Vector2 p)
        {
            Vector2 c = V(32, 32);
            const float radius = 18f, start = 50f, end = 340f; // degrees, counter-clockwise, gap on the upper right
            Vector2 q = p - c;
            float angle = Mathf.Atan2(q.y, q.x) * Mathf.Rad2Deg;
            if (angle < 0f)
            {
                angle += 360f;
            }
            float arc;
            if (angle >= start && angle <= end)
            {
                arc = Mathf.Abs(q.magnitude - radius) - Line;
            }
            else
            {
                arc = Mathf.Min(Capsule(p, Point(c, radius, start), Point(c, radius, start), Line),
                    Capsule(p, Point(c, radius, end), Point(c, radius, end), Line));
            }
            // arrowhead at the start of the arc, pointing on clockwise (towards the gap)
            Vector2 tip = Point(c, radius, start - 14f);
            Vector2 back = Point(c, radius, start + 4f);
            Vector2 n = (back - c).normalized * 8f;
            float head = Lines(p, Line, tip, back + n, tip, back - n);
            return Mathf.Min(arc, head);
        }

        private static Vector2 Point(Vector2 c, float r, float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
    }
}
