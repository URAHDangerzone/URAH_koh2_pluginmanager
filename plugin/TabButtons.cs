using Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_PluginManager
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The window's bottom buttons on our tab: copies of the game's, so they look the same and the game's
    /// own stay untouched. "URAH releases" takes the slot that Revert Defaults and Open Workshop share
    /// (UpdateButtons shows one of them on every tab; a postfix hides Revert Defaults on ours) and opens
    /// the releases page, "Fetch plugins" takes Apply Changes' slot (hidden on our tab, which has no
    /// setting). The copies sit right after their originals, so a layout group puts them in the same
    /// slot. PluginCategory switches them with its selection.
    /// </summary>
    internal static class TabButtons
    {
        public const string ReleasesUrl = "https://github.com/URAHDangerzone/URAH_koh2_releases/releases";

        public static BSGButton Copy(BSGButton original, string name, string text, BSGButton.OnClick onClick)
        {
            if (original == null)
            {
                return null;
            }
            BSGButton copy = Object.Instantiate(original, original.transform.parent, worldPositionStays: false);
            copy.name = name;
            copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            copy.onClick = onClick; // not serialized: the copy starts without the game's handler
            TextMeshProUGUI label = copy.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                UIText.SetText(label, text);
            }
            copy.gameObject.SetActive(false);
            return copy;
        }

        /// <summary>
        /// The status line: a text in the window right above the bottom buttons, as wide as the right
        /// content's scroll view, in the buttons' font. Fixed, so a message never moves the list.
        /// </summary>
        public static TextMeshProUGUI StatusLine(BSGButton button, RectTransform content)
        {
            ScrollRect scroll = content != null ? content.GetComponentInParent<ScrollRect>() : null;
            if (button == null || scroll == null)
            {
                return null;
            }
            Transform parent = button.transform.parent;
            var corners = new Vector3[4];
            ((RectTransform)button.transform).GetWorldCorners(corners);
            float buttonTop = corners[1].y, buttonHeight = corners[1].y - corners[0].y;
            ((RectTransform)scroll.transform).GetWorldCorners(corners);
            float left = corners[0].x, right = corners[2].x;

            var go = new GameObject("URAH_Status", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            Vector3 scale = parent.lossyScale;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = Vector2.zero;
            rt.position = new Vector3(left, buttonTop + buttonHeight * 0.1f, rt.position.z);
            rt.sizeDelta = new Vector2((right - left) / scale.x, buttonHeight * 0.55f / scale.y);

            var text = go.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI style = button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (style != null)
            {
                text.font = style.font;
                text.fontSharedMaterial = style.fontSharedMaterial;
                text.fontSizeMax = style.fontSize * 0.8f;
            }
            text.enableAutoSizing = true;
            text.fontSizeMin = 8f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            go.SetActive(false);
            return text;
        }

        public static void OpenReleases(BSGButton button)
        {
            if (THQNORequest.platformType == Logic.Common.PlatformType.Steam && THQNORequest.connected)
            {
                THQNORequest.ActivateGameOverlayToWebPage(ReleasesUrl);
            }
            else
            {
                Application.OpenURL(ReleasesUrl);
            }
        }
    }
}
