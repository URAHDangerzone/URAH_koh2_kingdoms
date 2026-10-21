using System;
using System.Collections.Generic;
using Logic;

namespace URAH_Kingdoms
{
    /// <summary>
    /// Profiles on the menu map (the title game the kingdom selection and the lobby show): the singleplayer
    /// selection, or every lobby player's override. Assigns them like a campaign does and refreshes the
    /// kingdoms whose look changed: arms (LoadKingdomCoAIndicesAndColors, then ArmsPatch), colours and crest
    /// (view Load, ColorPatch), labels, kingdom icons ("name_changed"). Names follow by themselves (NameKey).
    /// A map loading for a game clears it (LoadMap).
    /// </summary>
    internal static class Preview
    {
        /// <summary>Kingdom shown with a profile, by the profile's code (to see what changed).</summary>
        private static readonly Dictionary<Logic.Kingdom, string> shown = new Dictionary<Logic.Kingdom, string>();

        /// <summary>A map is loading: its kingdoms are new objects, the assignments are cleared with them.</summary>
        public static void Forget() => shown.Clear();

        public static void Clear() => Set(new Dictionary<Logic.Kingdom, Profile>());

        /// <summary>Singleplayer: the selected kingdom with the chosen profile.</summary>
        public static void Single(Logic.Kingdom kingdom)
        {
            var profiles = new Dictionary<Logic.Kingdom, Profile>();
            Profile profile = kingdom != null ? Plugin.Instance.ActiveProfile() : null;
            if (profile != null)
            {
                profiles[kingdom] = profile;
            }
            Set(profiles);
        }

        /// <summary>Lobby: every player's kingdom with the player's override.</summary>
        public static void Players(Campaign campaign)
        {
            var profiles = new Dictionary<Logic.Kingdom, Profile>();
            Logic.Game game = GameLogic.Get(create: false);
            if (campaign != null && game != null && campaign.state < Campaign.State.Started)
            {
                int count = campaign.playerDataPersistent?.Length ?? 0;
                for (int i = 0; i < count; i++)
                {
                    if (string.IsNullOrEmpty(campaign.GetPlayerID(i)))
                    {
                        continue;
                    }
                    string name = campaign.GetKingdomName(i, raw: true);
                    Logic.Kingdom kingdom = string.IsNullOrEmpty(name) || name == "random" ? null : game.GetKingdom(name);
                    Profile profile = kingdom != null ? Lobby.PlayerProfile(campaign, i) : null;
                    if (profile != null)
                    {
                        profiles[kingdom] = profile;
                    }
                }
            }
            Set(profiles);
        }

        private static void Set(Dictionary<Logic.Kingdom, Profile> profiles)
        {
            try
            {
                var changed = new List<Logic.Kingdom>();
                foreach (var kv in shown)
                {
                    if (!profiles.TryGetValue(kv.Key, out Profile p) || p.ToCode() != kv.Value)
                    {
                        changed.Add(kv.Key);
                    }
                }
                foreach (var kv in profiles)
                {
                    if (!shown.ContainsKey(kv.Key))
                    {
                        changed.Add(kv.Key);
                    }
                }
                if (changed.Count == 0)
                {
                    return;
                }
                Assignments.Clear();
                shown.Clear();
                foreach (var kv in profiles)
                {
                    Assignments.Assign(kv.Key.Name, kv.Value);
                    shown[kv.Key] = kv.Value.ToCode();
                }
                foreach (Logic.Kingdom kingdom in changed)
                {
                    Refresh(kingdom);
                }
                ViewMode.titleKingdomsView.Apply(force_reapply: true);
                LabelUpdater.Get()?.UpdateLabels();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Map preview: {e}");
            }
        }

        private static void Refresh(Logic.Kingdom kingdom)
        {
            Logic.Game game = kingdom.game;
            if (game == null || kingdom.destroyed)
            {
                return;
            }
            var map = new CoAMapping();
            map.Load(game.map_name);
            string suffix = string.IsNullOrEmpty(game.map_period) ? null : game.dt.GetString("Periods." + game.map_name + "." + game.map_period);
            game.LoadKingdomCoAIndicesAndColors(map, kingdom, suffix); // own arms and colours, then ArmsPatch
            if (kingdom.visuals is global::Kingdom view)
            {
                view.Load(kingdom); // colours (ColorPatch) and crest
                LabelUpdater labels = LabelUpdater.Get();
                if (labels != null)
                {
                    labels.ClearKingdomOldLabels(view);
                    labels.RecreateKingdomLabel(view);
                }
            }
            kingdom.NotifyListeners("name_changed"); // kingdom icons in the UI
        }
    }
}
