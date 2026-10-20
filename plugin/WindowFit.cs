using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_PluginManager
{
    /// <summary>
    /// The extra tab would make the category list scroll. Instead the window grows down by one tab (its
    /// height plus the list's spacing), so the list keeps the base game's free space at its end. The
    /// window (id_Preferences) has fixed-height groups anchored to its top, each holding a scroll view:
    /// Group_Categories (the tabs) and the right content's group; both and the window get taller with
    /// their top edge kept.
    ///
    /// Everything else in the window is placed by where it was before, in world space, because its
    /// anchors don't say what it belongs to (the tabs' background box is anchored to the window's centre,
    /// so it slid down instead of growing): what spanned a grown group top to bottom (a background) grows
    /// with it, what lay below one (Group_LanguageCategories, the bottom buttons) moves down, the rest
    /// stays. Scroll view contents and children of layout groups are left to their owners. Runs once per
    /// window, when the tab is made.
    /// </summary>
    internal static class WindowFit
    {
        private struct Box
        {
            public RectTransform Rt;
            public float Top, Bottom, Left, Right;
        }

        public static void Fit(RectTransform categories, RectTransform content, RectTransform tab, Transform window)
        {
            ScrollRect scroll = categories != null ? categories.GetComponentInParent<ScrollRect>() : null;
            if (scroll == null || tab == null)
            {
                return;
            }
            RectTransform list = scroll.content != null ? scroll.content : categories;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(list);
            VerticalLayoutGroup group = list.GetComponent<VerticalLayoutGroup>();
            float amount = tab.rect.height + (group != null ? group.spacing : 0f);
            if (amount <= 0.5f)
            {
                return;
            }

            // where everything was
            var scrolled = new List<RectTransform>(); // placed by their owners: scroll contents, scrollbar handles
            foreach (ScrollRect s in window.GetComponentsInChildren<ScrollRect>(includeInactive: true))
            {
                if (s.content != null)
                {
                    scrolled.Add(s.content);
                }
            }
            foreach (Scrollbar s in window.GetComponentsInChildren<Scrollbar>(includeInactive: true))
            {
                if (s.handleRect != null && s.handleRect.parent is RectTransform area)
                {
                    scrolled.Add(area);
                }
            }
            var before = new List<Box>();
            foreach (RectTransform rt in window.GetComponentsInChildren<RectTransform>(includeInactive: true))
            {
                if (rt != window && !Inside(rt, scrolled))
                {
                    before.Add(Measure(rt));
                }
            }

            // grow the window and the two groups
            var grown = new HashSet<RectTransform>();
            var sb = new StringBuilder($"Settings window {amount:0} px taller for the Plugins tab: grown");
            GrowChain(Viewport(scroll), window, amount, grown, sb);
            ScrollRect right = content != null ? content.GetComponentInParent<ScrollRect>() : null;
            if (right != null)
            {
                GrowChain(Viewport(right), window, amount, grown, sb);
            }
            var groups = new List<Box>();
            foreach (Box b in before)
            {
                if (grown.Contains(b.Rt) && b.Rt != window)
                {
                    groups.Add(b);
                }
            }
            float world = amount * Mathf.Abs(((RectTransform)window).lossyScale.y);
            float eps = world * 0.25f;

            // everything else where it belongs
            var grew = new StringBuilder();
            var moved = new StringBuilder();
            foreach (Box b in before)
            {
                RectTransform rt = b.Rt;
                if (grown.Contains(rt) || Contains(rt, grown) || (rt.parent != null && rt.parent.GetComponent<LayoutGroup>() != null))
                {
                    continue; // grown, an ancestor of a grown one, or placed by a layout group
                }
                float top = b.Top, bottom = b.Bottom;
                bool spans = false, below = false;
                foreach (Box g in groups)
                {
                    if (b.Left > g.Right || b.Right < g.Left)
                    {
                        continue;
                    }
                    if (b.Top >= g.Top - eps && b.Bottom <= g.Bottom + eps)
                    {
                        spans = true;
                    }
                    else if (b.Top <= g.Bottom + eps)
                    {
                        below = true;
                    }
                }
                if (spans)
                {
                    bottom -= world;
                }
                else if (below)
                {
                    top -= world;
                    bottom -= world;
                }
                if (Place(rt, top, bottom))
                {
                    (spans ? grew : moved).Append(' ').Append(rt.name);
                }
            }
            sb.Append(", grew with them:").Append(grew).Append(", moved down:").Append(moved);
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static RectTransform Viewport(ScrollRect scroll) => scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;

        private static bool Inside(Transform t, List<RectTransform> roots)
        {
            foreach (RectTransform r in roots)
            {
                if (t.IsChildOf(r))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>t is a strict ancestor of one of them.</summary>
        private static bool Contains(Transform t, HashSet<RectTransform> set)
        {
            foreach (RectTransform r in set)
            {
                if (r != t && r.IsChildOf(t))
                {
                    return true;
                }
            }
            return false;
        }

        private static readonly Vector3[] corners = new Vector3[4];

        private static Box Measure(RectTransform rt)
        {
            rt.GetWorldCorners(corners);
            return new Box { Rt = rt, Top = corners[1].y, Bottom = corners[0].y, Left = corners[0].x, Right = corners[2].x };
        }

        /// <summary>Moves the edges to these world heights, whatever the anchors; false if already there.</summary>
        private static bool Place(RectTransform rt, float top, float bottom)
        {
            Box now = Measure(rt);
            float dTop = top - now.Top, dBottom = bottom - now.Bottom;
            if (Mathf.Abs(dTop) < 0.5f && Mathf.Abs(dBottom) < 0.5f)
            {
                return false;
            }
            float scale = rt.parent != null ? Mathf.Abs(rt.parent.lossyScale.y) : 1f;
            if (scale < 1e-6f)
            {
                return false;
            }
            rt.offsetMax += new Vector2(0f, dTop / scale);
            rt.offsetMin += new Vector2(0f, dBottom / scale);
            return true;
        }

        /// <summary>Every fixed-height parent from start up to the window: stretched ones follow their parent.</summary>
        private static void GrowChain(RectTransform start, Transform window, float amount, HashSet<RectTransform> grown, StringBuilder sb)
        {
            for (Transform t = start; t != null && t != window.parent; t = t.parent)
            {
                if (t is RectTransform rt && rt.anchorMin.y == rt.anchorMax.y && grown.Add(rt))
                {
                    Grow(rt, amount);
                    sb.Append(' ').Append(rt.name);
                }
            }
        }

        /// <summary>Taller by amount, top edge kept: top = position + (1 - pivot) * height.</summary>
        private static void Grow(RectTransform rt, float amount)
        {
            LayoutElement element = rt.GetComponent<LayoutElement>();
            if (element != null && element.preferredHeight > 0f)
            {
                element.preferredHeight += amount;
            }
            rt.sizeDelta += new Vector2(0f, amount);
            rt.anchoredPosition -= new Vector2(0f, (1f - rt.pivot.y) * amount);
        }
    }
}
