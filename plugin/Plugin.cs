using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using URAH;

namespace URAH_Kingdoms
{
    /// <summary>
    /// Player-defined kingdoms, cosmetic: the kingdom a player picked shows the name, adjective, colour,
    /// arms and ruler name of the player's own profile. Needs the URAH_Kingdoms mod; the patches are only
    /// applied while that mod is active (its bepinex_plugins.txt names this plugin, see ModLink).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "urah.koh2.kingdoms";
        public const string Name = "URAH_Kingdoms";
        public const string Version = URAH.BuildVersion.Value; // plugin/version.txt

        internal static ManualLogSource Log;
        internal static ProfileStore Profiles;
        internal static Plugin Instance;

        private ModLink link;
        private ConfigEntry<bool> allowOthers;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Profiles = new ProfileStore(Path.Combine(Paths.ConfigPath, "urah.kingdoms"), Log);
            allowOthers = Config.Bind("Multiplayer", "AllowOtherProfiles", true,
                "As host: the other players may play their own profiles (yours always shows)");
            link = new ModLink(Guid, typeof(Plugin).Assembly, Log);
            link.Changed += Lobby.PublishInLobby;
            link.Changed += active =>
            {
                if (!active)
                {
                    MainMenu.Remove();
                }
            };
            Log.LogInfo($"{Name} {Version} loaded");
            Log.LogInfo($"{Profiles.Ids().Count} kingdom profiles in {Profiles.Folder}");
        }

        /// <summary>
        /// Id of the profile chosen for the campaign being set up ("" plays the picked kingdom as it is).
        /// Not saved: every campaign setup and lobby starts with the kingdom itself (Selection resets it).
        /// </summary>
        internal string ProfileId { get; set; } = "";

        internal bool AllowOthers => allowOthers.Value;

        /// <summary>The chosen profile, read fresh from its file; null when none is chosen or it is invalid (logged).</summary>
        internal Profile ActiveProfile()
        {
            string id = ProfileId ?? "";
            return id.Length == 0 ? null : Profiles.Load(id);
        }
    }
}
