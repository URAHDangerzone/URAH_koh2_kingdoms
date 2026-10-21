using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Logic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace URAH_Kingdoms
{
    using Object = UnityEngine.Object;

    /// <summary>
    /// The kingdom choice in the campaign setup and the lobby (UICampaignKingdomInfo): a dropdown where the
    /// lobby's "Pick kingdom" sits (hidden in singleplayer; in the lobby, once picked, the game shows
    /// "picked by you" there instead). Its first entry is the kingdom itself, then the player's profiles.
    /// A copy of the campaign settings' dropdown (UICampaignSettings' rule prototype), so it looks like the
    /// game's. The choice (Plugin.ProfileId) starts with the kingdom itself in every setup and lobby; the
    /// new game takes it (Patches.LoadMapPatch, Lobby.Publish). Profiles are made in the main menu (Editor).
    /// </summary>
    internal static class Selection
    {
        private static readonly AccessTools.FieldRef<UICampaignKingdomInfo, BSGButton> pickKingdom =
            AccessTools.FieldRefAccess<UICampaignKingdomInfo, BSGButton>("m_PickKingdom");
        private static readonly AccessTools.FieldRef<UICampaignKingdomInfo, TextMeshProUGUI> pickWarning =
            AccessTools.FieldRefAccess<UICampaignKingdomInfo, TextMeshProUGUI>("m_PickWarning");
        private static readonly AccessTools.FieldRef<UICampaignSettings, GameObject> rulePrototype =
            AccessTools.FieldRefAccess<UICampaignSettings, GameObject>("m_SettingsRulePrototype");

        private sealed class Ui
        {
            public BSGDropdown Dropdown;
            public readonly List<string> Ids = new List<string>(); // per option; "" = the kingdom itself
            public bool Filling;
        }

        private static readonly ConditionalWeakTable<UICampaignKingdomInfo, Ui> uis = new ConditionalWeakTable<UICampaignKingdomInfo, Ui>();

        /// <summary>
        /// Before the start, a new campaign: singleplayer, a kingdom the player may pick; multiplayer, the
        /// kingdom the player picked.
        /// </summary>
        private static bool Applies(UICampaignKingdomInfo panel)
        {
            Campaign campaign = panel.Campaign;
            Logic.Kingdom kingdom = panel.Kingdom;
            if (campaign == null || kingdom == null || campaign.state >= Campaign.State.Started || campaign.IsFromSave())
            {
                return false;
            }
            if (campaign.IsMultiplayerCampaign())
            {
                int own = campaign.GetLocalPlayerIndexInData();
                return own >= 0 && CampaignUtils.GetKingdomsOwningPlayerIndex(campaign, kingdom) == own;
            }
            string pick = campaign.GetVar("pick_kingdom");
            return pick != "random_kingdom" && pick != "random_province" &&
                CampaignUtils.IsPlayerEligible(kingdom.game, kingdom) && !CampaignUtils.IsBlackListedKingdom(campaign, kingdom);
        }

        [HarmonyPatch(typeof(UICampaignKingdomInfo), "UpdatePick")]
        private static class UpdatePickPatch
        {
            private static void Postfix(UICampaignKingdomInfo __instance)
            {
                try
                {
                    Update(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Kingdom dropdown: {e}");
                }
            }
        }

        private static void Update(UICampaignKingdomInfo panel)
        {
            bool applies = Applies(panel);
            if (!uis.TryGetValue(panel, out Ui ui))
            {
                if (!applies)
                {
                    return;
                }
                ui = Create(panel);
                if (ui == null)
                {
                    return;
                }
                uis.Add(panel, ui);
            }
            ui.Dropdown.gameObject.SetActive(applies);
            if (!applies)
            {
                return;
            }
            if (panel.Campaign.IsMultiplayerCampaign())
            {
                pickWarning(panel)?.gameObject.SetActive(false); // "picked by you": the dropdown takes its place
            }
            Fill(panel, ui);
        }

        private static Ui Create(UICampaignKingdomInfo panel)
        {
            BSGButton pick = pickKingdom(panel);
            BSGDropdown template = Template();
            if (pick == null || template == null)
            {
                Plugin.Log.LogWarning($"Kingdom dropdown not made: pick button {(pick != null ? "found" : "missing")}, dropdown template {(template != null ? "found" : "missing")}");
                return null;
            }
            var ui = new Ui();
            BSGDropdown dropdown = Object.Instantiate(template, pick.transform.parent, worldPositionStays: false);
            dropdown.name = "URAH_KingdomDropdown";
            dropdown.transform.SetSiblingIndex(pick.transform.GetSiblingIndex() + 1);
            // the button's place and size
            var from = (RectTransform)pick.transform;
            var to = (RectTransform)dropdown.transform;
            to.anchorMin = from.anchorMin;
            to.anchorMax = from.anchorMax;
            to.pivot = from.pivot;
            to.anchoredPosition = from.anchoredPosition;
            Vector2 size = from.rect.size.x > 1f ? from.rect.size : from.sizeDelta;
            to.sizeDelta = size;
            LayoutElement own = dropdown.GetComponent<LayoutElement>() ?? dropdown.gameObject.AddComponent<LayoutElement>();
            LayoutElement pickLayout = pick.GetComponent<LayoutElement>();
            own.ignoreLayout = pickLayout != null && pickLayout.ignoreLayout;
            own.preferredWidth = pickLayout != null && pickLayout.preferredWidth > 0f ? pickLayout.preferredWidth : size.x;
            own.preferredHeight = pickLayout != null && pickLayout.preferredHeight > 0f ? pickLayout.preferredHeight : size.y;
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(index => Chosen(panel, ui, index));
            dropdown.OnShow = () => Fill(panel, ui); // profiles made in the main menu meanwhile
            dropdown.interactable = true;
            Tooltip.Get(dropdown.gameObject).SetText("#" + UiTexts.Get("override_tip"));
            dropdown.gameObject.SetActive(true);
            ui.Dropdown = dropdown;
            return ui;
        }

        private static BSGDropdown template;

        /// <summary>
        /// The campaign settings' dropdown: from a settings panel's rule prototype (the lobby's has it at
        /// once, the singleplayer one not always), else any settings rule's dropdown loaded in the scene.
        /// </summary>
        private static BSGDropdown Template()
        {
            if (template != null)
            {
                return template;
            }
            foreach (UICampaignSettings settings in Resources.FindObjectsOfTypeAll<UICampaignSettings>())
            {
                GameObject prototype = settings.gameObject.scene.IsValid() ? rulePrototype(settings) : null;
                template = prototype != null ? prototype.GetComponentInChildren<BSGDropdown>(includeInactive: true) : null;
                if (template != null)
                {
                    return template;
                }
            }
            template = Resources.FindObjectsOfTypeAll<BSGDropdown>().FirstOrDefault(d =>
                d.gameObject.scene.IsValid() && d.name != "URAH_KingdomDropdown" &&
                d.GetComponentsInParent<UICampaignSettings.SettingsRule>(true).Length > 0);
            if (template != null)
            {
                Plugin.Log.LogInfo($"Kingdom dropdown: template from {template.transform.parent?.name}/{template.name}");
            }
            return template;
        }

        /// <summary>Options: the kingdom itself, then every valid profile; the current choice selected.</summary>
        private static void Fill(UICampaignKingdomInfo panel, Ui ui)
        {
            ui.Filling = true;
            try
            {
                ui.Ids.Clear();
                ui.Dropdown.options.Clear();
                Logic.Kingdom kingdom = panel.Kingdom;
                ui.Ids.Add("");
                ui.Dropdown.options.Add(new TMP_Dropdown.OptionData(Defs.Localize("tn_" + kingdom.ActiveName))); // its own name, not the preview's
                foreach (string id in Plugin.Profiles.Ids())
                {
                    Profile profile = Plugin.Profiles.Load(id);
                    if (profile != null)
                    {
                        ui.Ids.Add(id);
                        ui.Dropdown.options.Add(new TMP_Dropdown.OptionData(string.Format(UiTexts.Get("override_as"), profile.Name)));
                    }
                }
                int current = ui.Ids.IndexOf(Plugin.Instance.ProfileId);
                if (current < 0)
                {
                    current = 0; // the chosen profile was deleted or broke
                    if (Plugin.Instance.ProfileId.Length > 0)
                    {
                        Select(panel, "");
                    }
                }
                ui.Dropdown.SetValueWithoutNotify(current);
                ui.Dropdown.RefreshShownValue();
                ui.Dropdown.template.sizeDelta = new Vector2(0f, 25f * Math.Min(ui.Ids.Count, 10));
            }
            finally
            {
                ui.Filling = false;
            }
        }

        private static void Chosen(UICampaignKingdomInfo panel, Ui ui, int index)
        {
            if (ui.Filling || index < 0 || index >= ui.Ids.Count)
            {
                return;
            }
            Select(panel, ui.Ids[index]);
        }

        /// <summary>Makes a profile the chosen one and shows it (map, panel; lobby: tells the others).</summary>
        private static void Select(UICampaignKingdomInfo panel, string id)
        {
            id = id ?? "";
            if (id == Plugin.Instance.ProfileId)
            {
                return;
            }
            Plugin.Instance.ProfileId = id;
            Plugin.Log.LogInfo(id.Length > 0 ? $"Profile '{id}' chosen" : "No profile chosen");
            Campaign campaign = panel.Campaign;
            if (campaign != null && campaign.IsMultiplayerCampaign())
            {
                Lobby.PublishInLobby(true);
                Preview.Players(campaign);
            }
            else
            {
                Preview.Single(panel.Kingdom);
            }
            if (panel.Kingdom != null)
            {
                panel.SetKingdom(panel.Campaign, panel.Kingdom, panel.Realm); // name and crest of the panel
            }
        }

        /// <summary>Every campaign setup starts with the kingdom itself.</summary>
        public static void Reset()
        {
            if (Plugin.Instance.ProfileId.Length > 0)
            {
                Plugin.Log.LogInfo("Profile choice reset");
            }
            Plugin.Instance.ProfileId = "";
        }

        [HarmonyPatch(typeof(UISingleplayerCampaign), nameof(UISingleplayerCampaign.SetCampaignClient))]
        private static class SingleplayerOpenPatch
        {
            private static void Prefix()
            {
                Reset();
                Preview.Clear();
            }
        }

        /// <summary>The kingdom the player selected shows the chosen profile; any other kingdom its own look.</summary>
        [HarmonyPatch(typeof(UISingleplayerCampaign), nameof(UISingleplayerCampaign.SelectKingdom))]
        private static class SelectKingdomPatch
        {
            private static void Postfix(UISingleplayerCampaign __instance)
            {
                Campaign campaign = GameLogic.Get(create: false)?.campaign;
                Logic.Kingdom kingdom = __instance.SelectedKingdom;
                bool eligible = campaign != null && !campaign.IsMultiplayerCampaign() && kingdom != null &&
                    CampaignUtils.IsPlayerEligible(kingdom.game, kingdom);
                Preview.Single(eligible ? kingdom : null);
            }
        }

        /// <summary>Back to the main menu: the map shows the kingdoms as they are.</summary>
        [HarmonyPatch(typeof(UISingleplayerCampaign), "BackToMainMenu")]
        private static class BackPatch
        {
            private static void Prefix()
            {
                Reset();
                Preview.Clear();
            }
        }
    }
}
