using System;
using System.Collections.Generic;
using HarmonyLib;
using Logic;

namespace URAH_Kingdoms
{
    /// <summary>
    /// Assigns the profiles when a campaign map loads and applies them: name (both kingdom classes build
    /// their own "tn_" key), arms (CoAIndex, set from CoA.csv on every map load and on union renames, never
    /// saved), map colour (the view kingdom's MapColor) and, in a new game, the first ruler's name
    /// (Character.ChangeName, the game's own rename: synced and saved).
    ///
    /// A new game takes the configured profile (multiplayer: each player's, see Lobby) and stores it in
    /// the kingdom; a loaded game and a multiplayer client take the profiles stored in the kingdoms (the vars come with the objects, after LoadMap, so in
    /// Game.OnGameStarted) and refreshes arms, colour and labels the way a union rename does.
    /// </summary>
    internal static class Patches
    {
        /// <summary>Singleplayer: the local player's kingdom gets the chosen profile. Multiplayer comes later.</summary>
        [HarmonyPatch(typeof(Logic.Game), nameof(Logic.Game.LoadMap))]
        private static class LoadMapPatch
        {
            private static void Prefix(Logic.Game __instance, bool new_game)
            {
                if (__instance.type != "world_view")
                {
                    return; // a battle loads its own map in another Game; the campaign's assignments stay
                }
                Assignments.Clear();
                if (!new_game || __instance.campaign == null)
                {
                    return;
                }
                if (__instance.campaign.IsMultiplayerCampaign())
                {
                    if (__instance.IsAuthority())
                    {
                        Lobby.AssignPlayers(__instance.campaign);
                    }
                    return;
                }
                string kingdom = __instance.multiplayer?.playerData?.kingdomName;
                Profile profile = Plugin.Instance.ActiveProfile();
                if (profile == null || string.IsNullOrEmpty(kingdom) || kingdom == "random")
                {
                    return;
                }
                Assignments.Assign(kingdom, profile);
                Plugin.Log.LogInfo($"{kingdom} plays as {profile}");
            }

            private static void Postfix(Logic.Game __instance, bool new_game)
            {
                if (!Assignments.Any || __instance.type != "world_view")
                {
                    return;
                }
                foreach (var kv in Assignments.All())
                {
                    Logic.Kingdom kingdom = __instance.GetKingdom(kv.Key);
                    if (kingdom == null)
                    {
                        Plugin.Log.LogWarning($"Kingdom {kv.Key} not on this map");
                        continue;
                    }
                    if (!new_game || !__instance.IsAuthority())
                    {
                        continue;
                    }
                    Assignments.Store(kingdom, kv.Value);
                    if (kv.Value.Ruler != null)
                    {
                        Logic.Character king = kingdom.GetKing();
                        if (king != null)
                        {
                            king.ChangeName(kv.Value.Ruler);
                        }
                        else
                        {
                            Plugin.Log.LogWarning($"{kv.Key} has no king to name");
                        }
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Logic.Game), nameof(Logic.Game.OnGameStarted), typeof(string))]
        private static class GameStartedPatch
        {
            private static void Postfix(Logic.Game __instance, string started_from)
            {
                // "load_game": a save was loaded (host, and clients through GameLoadedState); "new_game": a
                // client got the full game state, so the kingdoms carry the host's vars
                if (__instance.type != "world_view" || (started_from != "load_game" && __instance.IsAuthority()))
                {
                    return;
                }
                List<Logic.Kingdom> kingdoms = Assignments.FromKingdoms(__instance);
                if (kingdoms.Count == 0)
                {
                    return;
                }
                CoAMapping map = new CoAMapping();
                map.Load(__instance.map_name);
                string suffix = string.IsNullOrEmpty(__instance.map_period) ? null : __instance.dt.GetString("Periods." + __instance.map_name + "." + __instance.map_period);
                foreach (Logic.Kingdom kingdom in kingdoms)
                {
                    Plugin.Log.LogInfo($"{kingdom.Name} plays as {Assignments.Get(kingdom.Name)} (stored in the kingdom)");
                    ArmsPatch.Apply(map, kingdom, suffix);
                    kingdom.NotifyListeners("name_changed"); // the view kingdom reloads colours, arms and labels
                }
            }
        }

        [HarmonyPatch(typeof(Logic.Kingdom), nameof(Logic.Kingdom.NameKey), MethodType.Getter)]
        private static class LogicNameKeyPatch
        {
            private static void Postfix(Logic.Kingdom __instance, ref string __result)
            {
                __result = Assignments.NameKey(__instance.Name) ?? __result;
            }
        }

        [HarmonyPatch(typeof(global::Kingdom), nameof(global::Kingdom.GetNameKey))]
        private static class ViewNameKeyPatch
        {
            private static void Postfix(global::Kingdom __instance, ref string __result)
            {
                __result = Assignments.NameKey(__instance.Name) ?? __result;
            }
        }

        [HarmonyPatch(typeof(Logic.Game), nameof(Logic.Game.LoadKingdomCoAIndicesAndColors))]
        internal static class ArmsPatch
        {
            private static void Postfix(CoAMapping map, Logic.Kingdom k, string suffix)
            {
                Apply(map, k, suffix);
            }

            public static void Apply(CoAMapping map, Logic.Kingdom k, string suffix)
            {
                string arms = Assignments.Get(k?.Name)?.Arms;
                if (arms == null)
                {
                    return;
                }
                int index = map.Get(arms, suffix);
                if (index > 0)
                {
                    k.CoAIndex = index;
                }
                else
                {
                    Plugin.Log.LogWarning($"Arms of '{arms}' not found on this map, {k.Name} keeps its own");
                }
            }
        }

        [HarmonyPatch(typeof(global::Kingdom), nameof(global::Kingdom.UpdateColors))]
        private static class ColorPatch
        {
            private static void Postfix(global::Kingdom __instance, Logic.Kingdom logic)
            {
                UnityEngine.Color? color = Assignments.ColorOf(logic?.Name);
                if (color == null)
                {
                    return;
                }
                __instance.MapColor = color.Value;
                UnityEngine.Color[] unitColors = WorldMap.Get()?.unit_colors;
                __instance.unitColorID = global::Kingdom.PickClosestColorID(unitColors, __instance.MapColor);
                __instance.unitColor = __instance.unitColorID == -1 ? __instance.MapColor : unitColors[__instance.unitColorID];
            }
        }
    }
}
