using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;

namespace URAH_Kingdoms
{
    /// <summary>
    /// Which kingdom of the campaign being played shows which profile, keyed by the kingdom's Name (its
    /// key in kingdoms.csv, never changed; ActiveName changes with unions). Rebuilt when a map loads.
    /// Each assigned kingdom gets the text key tn_urah_ck_&lt;slot&gt;; the game's own kingdom name keys
    /// stay as they are, so realms or lobby entries with the same key keep their names.
    /// </summary>
    internal static class Assignments
    {
        public const string KeyPrefix = "tn_urah_ck_";

        private sealed class Entry
        {
            public Profile Profile;
            public string TextKey;
            public UnityEngine.Color? Color;
        }

        private static readonly Dictionary<string, Entry> byKingdom = new Dictionary<string, Entry>();

        public static bool Any => byKingdom.Count > 0;

        public static void Clear() => byKingdom.Clear();

        public static void Assign(string kingdom, Profile profile)
        {
            UnityEngine.Color? color = null;
            if (profile.Color != null && UnityEngine.ColorUtility.TryParseHtmlString(profile.Color, out UnityEngine.Color c))
            {
                color = c;
            }
            byKingdom[kingdom] = new Entry { Profile = profile, TextKey = KeyPrefix + byKingdom.Count, Color = color };
        }

        public static Profile Get(string kingdom)
        {
            return kingdom != null && byKingdom.TryGetValue(kingdom, out Entry e) ? e.Profile : null;
        }

        public static UnityEngine.Color? ColorOf(string kingdom)
        {
            return kingdom != null && byKingdom.TryGetValue(kingdom, out Entry e) ? e.Color : null;
        }

        /// <summary>The text key of an assigned kingdom, registered in the current language if it is not (texts reload with the language or map defs); null when not assigned.</summary>
        public static string NameKey(string kingdom)
        {
            if (kingdom == null || !byKingdom.TryGetValue(kingdom, out Entry e))
            {
                return null;
            }
            Defs defs = Defs.Get();
            if (defs != null && !defs.texts.ContainsKey(e.TextKey))
            {
                Texts.Register(defs, e.TextKey, e.Profile);
            }
            return e.TextKey;
        }

        public static IEnumerable<KeyValuePair<string, Profile>> All()
        {
            return byKingdom.Select(kv => new KeyValuePair<string, Profile>(kv.Key, kv.Value.Profile));
        }
    }

    /// <summary>
    /// Registers a kingdom name as a territory text the way the game loads its own: one csv row under the
    /// header (and default row) of the current language's teritories.csv, through Defs.LoadCSVTexts.
    /// A form the default row has a template for stays empty and takes it ({base}'s, aus {base}, ...);
    /// the adjective columns (ian, anian, ians) take the profile's adjective where the language's default
    /// row knows them (English and the languages copying its forms). Every other form is the name itself:
    /// French has no default row, Polish and Russian ones are empty, and an empty form would show nothing.
    /// </summary>
    internal static class Texts
    {
        private static readonly AccessTools.FieldRef<Defs, string> loadingLanguage = AccessTools.FieldRefAccess<Defs, string>("loading_language");
        private static readonly Action<Defs, string, string> loadCsvTexts =
            AccessTools.MethodDelegate<Action<Defs, string, string>>(AccessTools.Method(typeof(Defs), "LoadCSVTexts"));

        public static void Register(Defs defs, string key, Profile profile)
        {
            string lang = Defs.Language;
            if (!Head(lang, out string header, out string defaults))
            {
                Plugin.Log.LogWarning($"No teritories.csv for language '{lang}': '{profile.Name}' shows as {key}");
                return;
            }
            char delimiter = header.Count(c => c == ';') >= header.Count(c => c == ',') ? ';' : ',';
            List<string> columns = Cells(header, delimiter);
            List<string> templates = defaults != null ? Cells(defaults, delimiter) : new List<string>();
            var cells = new string[columns.Count];
            cells[0] = key;
            for (int i = 1; i < columns.Count; i++)
            {
                bool template = i < templates.Count && templates[i].Length > 0;
                string adjective = null;
                switch (columns[i].TrimStart(':'))
                {
                    case "base": cells[i] = profile.Name; continue;
                    case "ian": adjective = profile.Adjective?.Ian; break;
                    case "anian": adjective = profile.Adjective?.Anian; break;
                    case "ians": adjective = profile.Adjective?.Ians; break;
                }
                cells[i] = template ? adjective ?? "" : profile.Name;
            }
            // the profile forbids the delimiters and quotes, so cells need no quoting
            string csv = header + "\n" + (defaults != null ? defaults + "\n" : "") + string.Join(delimiter.ToString(), cells) + "\n";
            string previous = loadingLanguage(defs);
            try
            {
                loadingLanguage(defs) = lang;
                loadCsvTexts(defs, $"urah_kingdoms/{lang}_teritories.csv", csv);
            }
            finally
            {
                loadingLanguage(defs) = previous;
            }
            Plugin.Log.LogInfo($"Registered {key} = '{Defs.Localize(key)}' ({lang})");
        }

        private static List<string> Cells(string line, char delimiter)
        {
            return line.Split(delimiter).Select(c => c.Trim().Trim('\uFEFF').Trim('"')).ToList();
        }

        /// <summary>Header and default row (null when the file has none) of the language's teritories.csv in the game folder (the game's cwd).</summary>
        private static bool Head(string lang, out string header, out string defaults)
        {
            header = defaults = null;
            foreach (string name in new[] { $"{lang}_teritories.csv", "teritories.csv" })
            {
                string path = System.IO.Path.Combine(Defs.texts_path + lang, name);
                if (!File.Exists(path))
                {
                    continue;
                }
                string[] lines = File.ReadLines(path, Encoding.UTF8).Take(3).ToArray();
                if (lines.Length == 0)
                {
                    continue;
                }
                header = lines[0];
                defaults = lines.Skip(1).FirstOrDefault(l => l.TrimStart('"').StartsWith("default", StringComparison.Ordinal));
                return true;
            }
            return false;
        }
    }
}
