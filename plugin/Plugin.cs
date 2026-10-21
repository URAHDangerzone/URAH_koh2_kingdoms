using BepInEx;
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

        private ModLink link;

        private void Awake()
        {
            Log = Logger;
            link = new ModLink(Guid, typeof(Plugin).Assembly, Log);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }
}
