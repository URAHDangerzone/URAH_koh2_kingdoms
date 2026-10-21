using HarmonyLib;
using Logic;

namespace URAH_Kingdoms
{
    /// <summary>
    /// The profile each player started the campaign with, in the campaign's player data
    /// (PersistentPlayerData, saved with the campaign, sent to the clients): the save list shows the
    /// campaign's name and arms from it (singleplayer: the campaign is named after the kingdom), and a
    /// lobby from a save shows the players' kingdoms with it. Campaigns from before keep the kingdom's own.
    /// </summary>
    internal static class CampaignStore
    {
        public const string Key = Lobby.VarKey;

        /// <summary>New game, authority: every player's profile, or "" (a campaign started again keeps no old one).</summary>
        public static void StoreAll(Campaign campaign)
        {
            int count = campaign.playerDataPersistent?.Length ?? 0;
            for (int i = 0; i < count; i++)
            {
                string kingdom = campaign.GetKingdomName(i);
                if (string.IsNullOrEmpty(kingdom))
                {
                    continue;
                }
                Profile profile = Assignments.Get(kingdom);
                campaign.SetVar(RemoteVars.DataType.PersistentPlayerData, i, Key, profile != null ? profile.ToCode() : "");
            }
        }

        public static Profile Get(Campaign campaign, int index)
        {
            if (campaign == null || index < 0 || index >= (campaign.playerDataPersistent?.Length ?? 0))
            {
                return null;
            }
            Value value = campaign.GetVar(RemoteVars.DataType.PersistentPlayerData, index, Key);
            string code = value.is_string ? value.String() : null;
            return string.IsNullOrEmpty(code) ? null : Profile.FromCode(code, out _);
        }

        private static int ArmsIndex(Campaign campaign, Profile profile)
        {
            return profile?.Arms != null ? CoAMapping.GetCoAIndex(campaign.GetMapName(), campaign.GetPeriod(), profile.Arms) : -1;
        }

        /// <summary>Singleplayer campaigns without a custom name are named after the kingdom.</summary>
        [HarmonyPatch(typeof(Campaign), nameof(Campaign.GetNameKey))]
        private static class NameKeyPatch
        {
            private static void Postfix(Campaign __instance, ref string __result)
            {
                if (__result == null || !__result.StartsWith("tn_") || __instance.IsMultiplayerCampaign())
                {
                    return;
                }
                Profile profile = Get(__instance, __instance.GetLocalPlayerIndexInData());
                if (profile != null)
                {
                    __result = "#" + profile.Name; // the profile forbids text markup
                }
            }
        }

        [HarmonyPatch(typeof(Campaign), nameof(Campaign.GetCoAIndex))]
        private static class CoAIndexPatch
        {
            private static void Postfix(Campaign __instance, ref int __result)
            {
                int index = ArmsIndex(__instance, Get(__instance, __instance.GetLocalPlayerIndexInData()));
                if (index > 0)
                {
                    __result = index;
                }
            }
        }

        [HarmonyPatch(typeof(Campaign), nameof(Campaign.GetPlayerKingdomCoAIndex))]
        private static class PlayerCoAIndexPatch
        {
            private static void Postfix(Campaign __instance, string player_id, ref int __result)
            {
                int index = ArmsIndex(__instance, Get(__instance, __instance.GetIndexInPlayerData(player_id)));
                if (index > 0)
                {
                    __result = index;
                }
            }
        }
    }
}
