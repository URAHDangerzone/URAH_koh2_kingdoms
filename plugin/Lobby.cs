using System;
using HarmonyLib;
using Logic;

namespace URAH_Kingdoms
{
    /// <summary>
    /// Multiplayer: every player sends the code of their profile as a per-player lobby var
    /// (NonPersistentPlayerData, VarKey; empty without a profile or with the mod off). When a new game
    /// starts, the host reads the var of each player, validates it (Profile.FromCode) and assigns it to
    /// that player's kingdom; the LoadMap postfix stores it in the kingdom, and the clients get it with
    /// the game state (Patches.GameStartedPatch). Players without the plugin send nothing and keep their
    /// kingdom as it is.
    /// </summary>
    internal static class Lobby
    {
        public const string VarKey = "urah_kingdoms_profile"; // no '.': a dot is a path in Vars keys
        /// <summary>The host's switch (only the campaign owner's counts): false = only the host's own profile.</summary>
        public const string AllowKey = "urah_kingdoms_allow";

        /// <summary>Sets the local player's var; send: tell the others now (EnterLobby sends its vars itself at its end).</summary>
        public static void Publish(Campaign campaign, bool active, bool send)
        {
            try
            {
                string code = active ? Plugin.Instance.ActiveProfile()?.ToCode() ?? "" : "";
                campaign.SetLocalPlayerVar(RemoteVars.DataType.NonPersistentPlayerData, AllowKey, Plugin.Instance.AllowOthers, send);
                campaign.SetLocalPlayerVar(RemoteVars.DataType.NonPersistentPlayerData, VarKey, code, send);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Publishing the profile failed: {e}");
            }
        }

        /// <summary>The mod was toggled while in a lobby: tell the others at once.</summary>
        public static void PublishInLobby(bool active)
        {
            Campaign campaign = MPBoss.Get()?.game?.campaign;
            if (campaign != null && campaign.IsMultiplayerCampaign() &&
                campaign.GetLocalPlayerRuntimeState() == Campaign.RuntimeState.Lobby)
            {
                Publish(campaign, active, true);
            }
        }

        /// <summary>
        /// The profile a player shows in the lobby: a campaign from a save shows what its players played
        /// (CampaignStore), a new one the player's lobby var, unless the host does not allow it.
        /// </summary>
        public static Profile PlayerProfile(Campaign campaign, int index)
        {
            if (campaign.IsFromSave())
            {
                return CampaignStore.Get(campaign, index);
            }
            if (index != campaign.GetOwnerIndex() && !OthersAllowed(campaign))
            {
                return null;
            }
            return Read(campaign, index, out _);
        }

        private static bool OthersAllowed(Campaign campaign)
        {
            int owner = campaign.GetOwnerIndex();
            Value allow = owner >= 0 ? campaign.GetVar(RemoteVars.DataType.NonPersistentPlayerData, owner, AllowKey) : Value.Unknown;
            return allow.is_unknown || allow.type != Value.Type.Int || allow.Bool(); // an old host without the switch: allowed
        }

        /// <summary>The profile in a player's lobby var; null without one, or with an invalid one (error set).</summary>
        private static Profile Read(Campaign campaign, int index, out string error)
        {
            error = null;
            Value value = campaign.GetVar(RemoteVars.DataType.NonPersistentPlayerData, index, VarKey);
            string code = value.is_string ? value.String() : null;
            return string.IsNullOrEmpty(code) ? null : Profile.FromCode(code, out error);
        }

        /// <summary>Host, new game: assigns each player's profile to the player's kingdom.</summary>
        public static void AssignPlayers(Campaign campaign)
        {
            int count = campaign.playerDataPersistent?.Length ?? 0;
            int own = campaign.GetLocalPlayerIndexInData();
            for (int i = 0; i < count; i++)
            {
                string kingdom = campaign.GetKingdomName(i);
                if (string.IsNullOrEmpty(kingdom) || string.IsNullOrEmpty(campaign.GetPlayerID(i)))
                {
                    continue;
                }
                string player = campaign.GetPlayerName(i);
                Profile profile = Read(campaign, i, out string error);
                if (profile == null)
                {
                    if (error != null)
                    {
                        Plugin.Log.LogWarning($"{player} ({kingdom}): profile not used, {error}");
                    }
                    continue;
                }
                if (i != own && !Plugin.Instance.AllowOthers)
                {
                    Plugin.Log.LogInfo($"{player} ({kingdom}): profile not used, other profiles are switched off");
                    continue;
                }
                Assignments.Assign(kingdom, profile);
                Plugin.Log.LogInfo($"{kingdom} ({player}) plays as {profile}");
            }
        }

        private static readonly Type SlotType = AccessTools.TypeByName("UILobbyPlayers+UIPlayerTeamSlot");
        private static readonly AccessTools.FieldRef<object, bool> slotInvalidate =
            SlotType != null ? AccessTools.FieldRefAccess<object, bool>(AccessTools.Field(SlotType, "m_Invalidate")) : null;
        private static bool previewDirty;

        /// <summary>Any lobby var: a kingdom pick, a profile, the host's switch, the political data. Shown in LateUpdate.</summary>
        [HarmonyPatch(typeof(UILobby), nameof(UILobby.OnVarChanged))]
        private static class LobbyVarPatch
        {
            private static void Postfix()
            {
                previewDirty = true;
            }
        }

        [HarmonyPatch(typeof(UILobby), "LateUpdate")]
        private static class LobbyLateUpdatePatch
        {
            private static void Postfix(UILobby __instance)
            {
                if (!previewDirty)
                {
                    return;
                }
                previewDirty = false;
                Campaign campaign = GameLogic.Get(create: false)?.campaign;
                if (campaign == null || !campaign.IsMultiplayerCampaign())
                {
                    return;
                }
                Preview.Players(campaign);
                // the player rows show the kingdoms through the preview: redraw them after it
                if (SlotType != null && slotInvalidate != null)
                {
                    foreach (UnityEngine.Object slot in UnityEngine.Object.FindObjectsOfType(SlotType))
                    {
                        slotInvalidate(slot) = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(UILobby), nameof(UILobby.SetCampaign))]
        private static class LobbySetCampaignPatch
        {
            private static void Postfix()
            {
                previewDirty = true;
            }
        }

        [HarmonyPatch(typeof(UILobby), nameof(UILobby.CloseLobby))]
        private static class CloseLobbyPatch
        {
            private static void Prefix()
            {
                Preview.Clear();
            }
        }

        [HarmonyPatch(typeof(MPBoss), nameof(MPBoss.EnterLobby))]
        private static class EnterLobbyPatch
        {
            private static void Prefix(Campaign campaign)
            {
                if (campaign != null)
                {
                    Publish(campaign, true, false);
                }
            }
        }
    }
}
