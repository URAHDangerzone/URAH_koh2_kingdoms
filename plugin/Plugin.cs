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
        private ConfigEntry<string> profileId;
        private ConfigEntry<bool> allowOthers;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Profiles = new ProfileStore(Path.Combine(Paths.ConfigPath, "urah.kingdoms"), Log);
            profileId = Config.Bind("General", "Profile", "",
                $"Id of the kingdom profile to play (file name without .json in {Profiles.Folder}); empty: play the picked kingdom as it is");
            allowOthers = Config.Bind("Multiplayer", "AllowOtherProfiles", true,
                "As host: the other players may play their own profiles (yours always shows)");
            link = new ModLink(Guid, typeof(Plugin).Assembly, Log);
            link.Changed += Lobby.PublishInLobby;
            Log.LogInfo($"{Name} {Version} loaded");
            Profile profile = ActiveProfile();
            Log.LogInfo(profile != null ? $"Profile '{profileId.Value}': {profile}" : "No kingdom profile chosen");
        }

        /// <summary>Id of the chosen profile ("" for none); setting it saves the config.</summary>
        internal string ProfileId
        {
            get => profileId.Value.Trim();
            set => profileId.Value = value ?? "";
        }

        internal bool AllowOthers => allowOthers.Value;

        /// <summary>The chosen profile, read fresh from its file; null when none is chosen or it is invalid (logged).</summary>
        internal Profile ActiveProfile()
        {
            string id = profileId.Value.Trim();
            return id.Length == 0 ? null : Profiles.Load(id);
        }
    }
}
