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

        /// <summary>Sets the local player's var; send: tell the others now (EnterLobby sends its vars itself at its end).</summary>
        public static void Publish(Campaign campaign, bool active, bool send)
        {
            try
            {
                string code = active ? Plugin.Instance.ActiveProfile()?.ToCode() ?? "" : "";
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

        /// <summary>Host, new game: assigns each player's profile to the player's kingdom.</summary>
        public static void AssignPlayers(Campaign campaign)
        {
            int count = campaign.playerDataPersistent?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                string kingdom = campaign.GetKingdomName(i);
                if (string.IsNullOrEmpty(kingdom) || string.IsNullOrEmpty(campaign.GetPlayerID(i)))
                {
                    continue;
                }
                Value value = campaign.GetVar(RemoteVars.DataType.NonPersistentPlayerData, i, VarKey);
                string code = value.is_string ? value.String() : null;
                if (string.IsNullOrEmpty(code))
                {
                    continue;
                }
                string player = campaign.GetPlayerName(i);
                Profile profile = Profile.FromCode(code, out string error);
                if (profile == null)
                {
                    Plugin.Log.LogWarning($"{player} ({kingdom}): profile not used, {error}");
                    continue;
                }
                Assignments.Assign(kingdom, profile);
                Plugin.Log.LogInfo($"{kingdom} ({player}) plays as {profile}");
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
