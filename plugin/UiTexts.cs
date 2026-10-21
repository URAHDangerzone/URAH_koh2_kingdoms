using System.Collections.Generic;

namespace URAH_Kingdoms
{
    /// <summary>The plugin's own UI texts (no game text files): English and German, English as fallback.</summary>
    internal static class UiTexts
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["override_as"] = "As {0}",
            ["override_tip"] = "Play this kingdom as itself, or with the name, colour, arms and ruler of one of your profiles (made under Kingdoms in the main menu). Only the look changes.",
            ["menu"] = "Kingdoms (URAH)",
            ["menu_tip"] = "Make and edit your kingdom profiles; choose one when setting up a campaign.",
            ["profiles"] = "Your kingdoms",
            ["invalid"] = "invalid",
            ["invalid_file"] = "The file of '{0}' is not valid (see BepInEx/LogOutput.log); saving replaces it.",
            ["saved"] = "Saved.",
            ["close"] = "Close",
            ["new"] = "+ New kingdom",
            ["editor_new"] = "New kingdom profile",
            ["editor_edit"] = "Edit kingdom profile",
            ["f_name"] = "Kingdom name",
            ["f_ruler"] = "First ruler",
            ["f_color"] = "Map colour",
            ["f_arms"] = "Arms",
            ["own_arms"] = "The kingdom's own",
            ["unknown_arms"] = "The arms of '{0}' are not on this map: the kingdom's own are used",
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
        };

        private static readonly Dictionary<string, string> De = new Dictionary<string, string>
        {
            ["override_as"] = "Als {0}",
            ["override_tip"] = "Dieses Königreich so spielen, wie es ist, oder mit Name, Farbe, Wappen und Herrscher eines deiner Profile (angelegt unter Königreiche im Hauptmenü). Nur das Aussehen ändert sich.",
            ["menu"] = "Königreiche (URAH)",
            ["menu_tip"] = "Eigene Königreichsprofile anlegen und bearbeiten; beim Einrichten einer Kampagne eines wählen.",
            ["profiles"] = "Deine Königreiche",
            ["invalid"] = "ungültig",
            ["invalid_file"] = "Die Datei von '{0}' ist ungültig (siehe BepInEx/LogOutput.log); Speichern ersetzt sie.",
            ["saved"] = "Gespeichert.",
            ["close"] = "Schließen",
            ["new"] = "+ Neues Königreich",
            ["editor_new"] = "Neues Königreichsprofil",
            ["editor_edit"] = "Königreichsprofil bearbeiten",
            ["f_name"] = "Name des Reichs",
            ["f_ruler"] = "Erster Herrscher",
            ["f_color"] = "Kartenfarbe",
            ["f_arms"] = "Wappen",
            ["own_arms"] = "Das eigene des Reichs",
            ["unknown_arms"] = "Das Wappen von '{0}' gibt es auf dieser Karte nicht: das eigene des Reichs wird verwendet",
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
