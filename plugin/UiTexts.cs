using System.Collections.Generic;

namespace URAH_Kingdoms
{
    /// <summary>The plugin's own UI texts (no game text files): English and German, English as fallback.</summary>
    internal static class UiTexts
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["override"] = "Override kingdom",
            ["override_as"] = "As {0}",
            ["override_tip"] = "Play this kingdom with the name, colour, arms and ruler of one of your profiles. Only the look changes.",
            ["none"] = "No override",
            ["no_profiles"] = "No profiles yet: put them in BepInEx/config/urah.kingdoms/",
        };

        private static readonly Dictionary<string, string> De = new Dictionary<string, string>
        {
            ["override"] = "Königreich überschreiben",
            ["override_as"] = "Als {0}",
            ["override_tip"] = "Dieses Königreich mit Name, Farbe, Wappen und Herrscher eines deiner Profile spielen. Nur das Aussehen ändert sich.",
            ["none"] = "Nicht überschreiben",
            ["no_profiles"] = "Noch keine Profile: sie gehören nach BepInEx/config/urah.kingdoms/",
        };

        public static string Get(string key)
        {
            string lang = Defs.Language ?? "en";
            if (lang.StartsWith("de") && De.TryGetValue(key, out string de))
            {
                return de;
            }
            return En.TryGetValue(key, out string en) ? en : key;
        }
    }
}
