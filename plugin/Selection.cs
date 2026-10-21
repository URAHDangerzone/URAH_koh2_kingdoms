using System;
using System.Collections.Generic;
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
    /// Singleplayer kingdom selection: in the kingdom panel (UICampaignKingdomInfo, the MP lobby's too) an
    /// "Override kingdom" button sits where the lobby's "Pick kingdom" does (hidden in SP); it opens a list
    /// of the profiles. The choice is the config's Profile, which the new game takes (Patches.LoadMapPatch).
    /// The selected kingdom shows the chosen profile on the menu map and in the panel (Preview).
    /// </summary>
    internal static class Selection
    {
        private static readonly AccessTools.FieldRef<UICampaignKingdomInfo, BSGButton> pickKingdom =
            AccessTools.FieldRefAccess<UICampaignKingdomInfo, BSGButton>("m_PickKingdom");
        private static readonly AccessTools.FieldRef<UICampaignKingdomInfo, TextMeshProUGUI> pickWarning =
            AccessTools.FieldRefAccess<UICampaignKingdomInfo, TextMeshProUGUI>("m_PickWarning");

        private sealed class Ui
        {
            public BSGButton Button;
            public TextMeshProUGUI Label;
            public RectTransform List;
            public readonly List<BSGButton> Entries = new List<BSGButton>();
        }

        private static readonly ConditionalWeakTable<UICampaignKingdomInfo, Ui> uis = new ConditionalWeakTable<UICampaignKingdomInfo, Ui>();

        /// <summary>
        /// Before the start, a new campaign: singleplayer, a kingdom the player may pick; multiplayer, the
        /// kingdom the player picked (the game shows "picked by you" there instead of the pick button).
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
                    Plugin.Log.LogError($"Override button: {e}");
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
            ui.Button.gameObject.SetActive(applies);
            CloseList(ui);
            if (applies && panel.Campaign.IsMultiplayerCampaign())
            {
                pickWarning(panel)?.gameObject.SetActive(false); // "picked by you": the button takes its place
            }
            if (applies)
            {
                Profile profile = Plugin.Instance.ActiveProfile();
                UIText.SetText(ui.Label, profile != null ? string.Format(UiTexts.Get("override_as"), profile.Name) : UiTexts.Get("override"));
            }
        }

        private static Ui Create(UICampaignKingdomInfo panel)
        {
            BSGButton original = pickKingdom(panel);
            if (original == null)
            {
                Plugin.Log.LogWarning("Kingdom panel has no pick button, no override button");
                return null;
            }
            var ui = new Ui();
            ui.Button = Copy(original, "URAH_OverrideKingdom", btn => ToggleList(panel, ui));
            ui.Label = ui.Button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            Tooltip.Get(ui.Button.gameObject).SetText("#" + UiTexts.Get("override_tip"));
            return ui;
        }

        private static BSGButton Copy(BSGButton original, string name, BSGButton.OnClick onClick)
        {
            BSGButton copy = Object.Instantiate(original, original.transform.parent, worldPositionStays: false);
            copy.name = name;
            copy.transform.SetSiblingIndex(original.transform.GetSiblingIndex() + 1);
            copy.onClick = onClick; // not serialized: the copy starts without the game's handler
            copy.Enable(true);
            return copy;
        }

        private static void CloseList(Ui ui)
        {
            if (ui.List != null)
            {
                ui.List.gameObject.SetActive(false);
            }
        }

        /// <summary>The list: "No override" and every valid profile, as copies of the button stacked above it.</summary>
        private static void ToggleList(UICampaignKingdomInfo panel, Ui ui)
        {
            if (ui.List != null && ui.List.gameObject.activeSelf)
            {
                CloseList(ui);
                return;
            }
            if (ui.List == null)
            {
                var go = new GameObject("URAH_ProfileList", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
                ui.List = (RectTransform)go.transform;
                ui.List.SetParent(ui.Button.transform.parent, worldPositionStays: false);
                var layout = go.GetComponent<VerticalLayoutGroup>();
                layout.childControlWidth = layout.childControlHeight = false;
                layout.childForceExpandWidth = layout.childForceExpandHeight = false;
                layout.spacing = 2f;
                go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                go.AddComponent<LayoutElement>().ignoreLayout = true; // floats over the panel, the panel's layout stays
            }
            foreach (BSGButton entry in ui.Entries)
            {
                Object.Destroy(entry.gameObject);
            }
            ui.Entries.Clear();

            var choices = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("", UiTexts.Get("none")) };
            foreach (string id in Plugin.Profiles.Ids())
            {
                Profile profile = Plugin.Profiles.Load(id);
                if (profile != null)
                {
                    choices.Add(new KeyValuePair<string, string>(id, profile.Name));
                }
            }
            string current = Plugin.Instance.ProfileId;
            if (Plugin.Instance.ActiveProfile() != null)
            {
                choices.Add(new KeyValuePair<string, string>(EditChoice, UiTexts.Get("edit")));
            }
            choices.Add(new KeyValuePair<string, string>(NewChoice, UiTexts.Get("new")));
            var size = ((RectTransform)ui.Button.transform).rect.size;
            foreach (var choice in choices)
            {
                string id = choice.Key;
                BSGButton entry = Object.Instantiate(ui.Button, ui.List, worldPositionStays: false);
                entry.name = "URAH_Profile_" + (id ?? "hint");
                entry.onClick = btn => Choose(panel, ui, id);
                entry.SetSelected(id == current, force_update: true);
                Tooltip.Get(entry.gameObject).Clear();
                ((RectTransform)entry.transform).sizeDelta = size;
                UIText.SetText(entry.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true), choice.Value);
                entry.gameObject.SetActive(true);
                ui.Entries.Add(entry);
            }
            // right above the button, as wide
            var button = (RectTransform)ui.Button.transform;
            ui.List.anchorMin = ui.List.anchorMax = button.anchorMin;
            ui.List.pivot = new Vector2(button.pivot.x, 0f);
            ui.List.sizeDelta = new Vector2(size.x, 0f);
            ui.List.gameObject.SetActive(true);
            ui.List.SetAsLastSibling();
            var corners = new Vector3[4];
            button.GetWorldCorners(corners);
            ui.List.position = new Vector3((corners[0].x + corners[2].x) / 2f + (ui.List.pivot.x - 0.5f) * (corners[2].x - corners[0].x), corners[1].y, button.position.z);
        }

        private const string NewChoice = "\u0001new", EditChoice = "\u0001edit"; // never a profile id

        private static void Choose(UICampaignKingdomInfo panel, Ui ui, string id)
        {
            CloseList(ui);
            if (id == NewChoice || id == EditChoice)
            {
                string editId = id == EditChoice ? Plugin.Instance.ProfileId : null;
                Editor.Open(panel, editId, saved => Select(panel, saved, force: true));
                return;
            }
            Select(panel, id, force: false);
        }

        /// <summary>Makes a profile the chosen one and shows it (map, panel; lobby: tells the others).</summary>
        public static void Select(UICampaignKingdomInfo panel, string id, bool force)
        {
            id = id ?? "";
            if (id == Plugin.Instance.ProfileId && !force)
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
            if (panel != null && panel.Kingdom != null)
            {
                panel.SetKingdom(panel.Campaign, panel.Kingdom, panel.Realm); // name and crest of the panel, and Update
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
                Preview.Clear();
            }
        }
    }
}
