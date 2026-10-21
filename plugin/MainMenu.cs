using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The "Kingdoms (URAH)" button in the main menu, a copy of the game's Credits button as
    /// the column's last button (after Quit); it opens the profile editor. Made in TitleUI.UpdateMainMenuButtons (every frame), since
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
            // last of the column's buttons (after Quit), then the column is laid out again
            Transform last = title.btnQuit != null && title.btnQuit.transform.parent == original.transform.parent ? title.btnQuit.transform : original.transform;
            button.transform.SetSiblingIndex(last.GetSiblingIndex() + 1);
            var lastRt = (RectTransform)last;
            ((RectTransform)button.transform).anchoredPosition = lastRt.anchoredPosition - new Vector2(0f, lastRt.rect.height > 1f ? lastRt.rect.height : 46f);
            button.onClick = b => Editor.Open(title, null);
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
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)original.transform.parent);
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
