using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The "Kingdoms (URAH)" button in the main menu, a copy of the game's Credits button right after
    /// Multiplayer; it opens the profile editor. Made in TitleUI.UpdateMainMenuButtons (every frame), since
    /// the patches may only be applied after the title screen started (ModLink); removed when the mod is
    /// switched off.
    /// </summary>
    internal static class MainMenu
    {
        private static BSGButton button;

        [HarmonyPatch(typeof(TitleUI), "UpdateMainMenuButtons")]
        private static class UpdatePatch
        {
            private static void Postfix(TitleUI __instance)
            {
                if (button != null || __instance.btnCredits == null || __instance.m_MainMenuButtons == null || !__instance.m_MainMenuButtons.activeInHierarchy)
                {
                    return;
                }
                try
                {
                    Create(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Main menu button: {e}");
                    button = null;
                }
            }
        }

        private static void Create(TitleUI title)
        {
            BSGButton original = title.btnCredits;
            button = Object.Instantiate(original, original.transform.parent, worldPositionStays: false);
            button.name = "URAH_Kingdoms";
            Transform after = (title.btnMultiplayer != null && title.btnMultiplayer.transform.parent == original.transform.parent ? title.btnMultiplayer : original).transform;
            button.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            if (original.transform.parent.GetComponent<LayoutGroup>() == null)
            {
                Place((RectTransform)button.transform, (RectTransform)after);
            }
            button.onClick = b => Editor.Open(title.GetComponentInParent<Canvas>()?.rootCanvas ?? title.GetComponentInChildren<Canvas>(), null);
            button.Enable(true);
            button.SetSelected(false, force_update: true);
            TextMeshProUGUI label = Common.FindChildComponent<TextMeshProUGUI>(button.gameObject, "id_Label") ??
                button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
            {
                UIText.SetText(label, UiTexts.Get("menu"));
            }
            Tooltip.Get(button.gameObject).SetText("#" + UiTexts.Get("menu_tip"));
            button.gameObject.SetActive(true);
        }

        /// <summary>
        /// The menu column has no layout group: the button goes one step below "after" and every button
        /// below moves down a step (the step: the gap between "after" and the next button below it).
        /// </summary>
        private static void Place(RectTransform rt, RectTransform after)
        {
            Transform parent = after.parent;
            float y = after.anchoredPosition.y;
            var below = new List<RectTransform>();
            float step = float.MaxValue;
            foreach (Transform child in parent)
            {
                var c = child as RectTransform;
                if (c == null || c == rt || c.GetComponent<BSGButton>() == null)
                {
                    continue;
                }
                float dy = y - c.anchoredPosition.y;
                if (dy > 0.5f)
                {
                    below.Add(c);
                    step = Mathf.Min(step, dy);
                }
            }
            if (step == float.MaxValue)
            {
                step = after.rect.height > 1f ? after.rect.height : 40f;
            }
            rt.anchorMin = after.anchorMin;
            rt.anchorMax = after.anchorMax;
            rt.pivot = after.pivot;
            rt.anchoredPosition = new Vector2(after.anchoredPosition.x, y - step);
            foreach (RectTransform c in below)
            {
                c.anchoredPosition -= new Vector2(0f, step);
            }
            Plugin.Log.LogInfo($"Main menu button placed below {after.name}, {below.Count} buttons moved down {step:0}");
        }

        /// <summary>The mod was switched off: the button goes (the patches are removed with it).</summary>
        public static void Remove()
        {
            Editor.Close();
            if (button != null)
            {
                Object.Destroy(button.gameObject);
                button = null;
            }
        }
    }
}
