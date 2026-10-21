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
            ["new"] = "+ New profile",
            ["edit"] = "Edit profile",
            ["editor_new"] = "New kingdom profile",
            ["editor_edit"] = "Edit kingdom profile",
            ["f_name"] = "Kingdom name",
            ["f_ruler"] = "First ruler",
            ["f_color"] = "Map colour (#RRGGBB)",
            ["f_arms"] = "Arms of",
            ["arms_of"] = "Arms of {0}",
            ["unknown_arms"] = "No kingdom '{0}' with arms on this map",
            ["adjective_hint"] = "Adjective, used by the English texts only (e.g. Ostmarkian / an Ostmarkian / Ostmarkians); empty: the name",
            ["f_ian"] = "Adjective",
            ["f_anian"] = "One person",
            ["f_ians"] = "The people",
            ["copy_code"] = "Copy kingdom code",
            ["paste_code"] = "Paste kingdom code",
            ["copied"] = "Kingdom code copied: send it to share this kingdom.",
            ["pasted"] = "Kingdom code pasted: save to keep it.",
            ["paste_failed"] = "No kingdom code in the clipboard: {0}",
            ["save"] = "Save",
            ["save_failed"] = "Saving failed (see BepInEx/LogOutput.log).",
            ["delete"] = "Delete",
            ["delete_confirm"] = "Really delete?",
            ["cancel"] = "Cancel",
        };

        private static readonly Dictionary<string, string> De = new Dictionary<string, string>
        {
            ["override"] = "Königreich überschreiben",
            ["override_as"] = "Als {0}",
            ["override_tip"] = "Dieses Königreich mit Name, Farbe, Wappen und Herrscher eines deiner Profile spielen. Nur das Aussehen ändert sich.",
            ["none"] = "Nicht überschreiben",
            ["new"] = "+ Neues Profil",
            ["edit"] = "Profil bearbeiten",
            ["editor_new"] = "Neues Königreichsprofil",
            ["editor_edit"] = "Königreichsprofil bearbeiten",
            ["f_name"] = "Name des Reichs",
            ["f_ruler"] = "Erster Herrscher",
            ["f_color"] = "Kartenfarbe (#RRGGBB)",
            ["f_arms"] = "Wappen von",
            ["arms_of"] = "Wappen von {0}",
            ["unknown_arms"] = "Kein Königreich '{0}' mit Wappen auf dieser Karte",
            ["adjective_hint"] = "Adjektiv, nur in den englischen Texten (z. B. Ostmarkian / an Ostmarkian / Ostmarkians); leer: der Name",
            ["f_ian"] = "Adjektiv",
            ["f_anian"] = "Eine Person",
            ["f_ians"] = "Das Volk",
            ["copy_code"] = "Reichscode kopieren",
            ["paste_code"] = "Reichscode einfügen",
            ["copied"] = "Reichscode kopiert: zum Teilen weitergeben.",
            ["pasted"] = "Reichscode eingefügt: speichern, um ihn zu behalten.",
            ["paste_failed"] = "Kein Reichscode in der Zwischenablage: {0}",
            ["save"] = "Speichern",
            ["save_failed"] = "Speichern fehlgeschlagen (siehe BepInEx/LogOutput.log).",
            ["delete"] = "Löschen",
            ["delete_confirm"] = "Wirklich löschen?",
            ["cancel"] = "Abbrechen",
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
