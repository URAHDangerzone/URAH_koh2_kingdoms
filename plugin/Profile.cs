using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace URAH_Kingdoms
{
    /// <summary>
    /// A player's kingdom look, cosmetic only: what it is called and how it shows. Kingdom and era are
    /// set in the lobby, so nothing here depends on them; an empty field keeps the replaced kingdom's
    /// value. Stored as JSON in BepInEx/config/urah.kingdoms/, shared and sent in multiplayer as the
    /// kingdom code (Code: prefix + base64 of the compact JSON; the game's DT strings turn '"' into
    /// "''", so plain JSON would arrive broken).
    ///
    /// The game reads every text through its own markup ({var}, [tags], #literal, @...), so the strings
    /// may not contain those characters (Forbidden); they are shown as typed, in every language.
    /// </summary>
    internal sealed class Profile
    {
        public const int Format = 1;
        public const string CodePrefix = "URAHK1:";
        public const int MaxName = 32;
        public const int MaxKey = 64;
        public const int MaxJson = 1400; // the code (base64) stays below 2 KB
        public const string Forbidden = "{}[]|;\"#@\\";

        [JsonProperty("format", Order = 0)]
        public int FormatVersion = Format;

        /// <summary>The kingdom's name, the "base" form of its text key.</summary>
        [JsonProperty("name", Order = 1)]
        public string Name;

        /// <summary>Optional adjective forms; an empty one falls back to the language's default (usually the name).</summary>
        [JsonProperty("adjective", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public Adjective Adjective;

        /// <summary>Map colour as #RRGGBB.</summary>
        [JsonProperty("color", Order = 3, NullValueHandling = NullValueHandling.Ignore)]
        public string Color;

        /// <summary>Key of an existing kingdom whose arms to show (kingdoms.csv); checked against the map at campaign start.</summary>
        [JsonProperty("arms", Order = 4, NullValueHandling = NullValueHandling.Ignore)]
        public string Arms;

        /// <summary>Own arms (Coa.CoaSpec's text form): a vanilla crest, or layers made in the editor.</summary>
        [JsonProperty("coa", Order = 6, NullValueHandling = NullValueHandling.Ignore)]
        public string Coa;

        /// <summary>Name of the first ruler.</summary>
        [JsonProperty("ruler", Order = 5, NullValueHandling = NullValueHandling.Ignore)]
        public string Ruler;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error, // a typo in a field name is an error, not a silent default
            ContractResolver = new DefaultContractResolver(),
            MaxDepth = 4,
        };

        public static Profile FromJson(string json)
        {
            Profile profile = JsonConvert.DeserializeObject<Profile>(json, Settings);
            if (profile == null)
            {
                throw new FormatException("empty profile");
            }
            return profile;
        }

        public string ToJson(bool indented = false)
        {
            return JsonConvert.SerializeObject(this, indented ? Formatting.Indented : Formatting.None, Settings);
        }

        public string ToCode()
        {
            return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(ToJson()));
        }

        /// <summary>Reads a kingdom code; null and an error when it is not one or the profile in it is invalid.</summary>
        public static Profile FromCode(string code, out string error)
        {
            error = null;
            code = code?.Trim();
            if (string.IsNullOrEmpty(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
            {
                error = "not a kingdom code";
                return null;
            }
            string data = code.Substring(CodePrefix.Length);
            if (data.Length > (MaxJson + 2) / 3 * 4)
            {
                error = "kingdom code too long";
                return null;
            }
            Profile profile;
            try
            {
                profile = FromJson(Encoding.UTF8.GetString(Convert.FromBase64String(data)));
            }
            catch (Exception e) when (e is FormatException || e is JsonException || e is ArgumentException)
            {
                error = $"broken kingdom code: {e.Message}";
                return null;
            }
            List<string> errors = profile.Normalize();
            if (errors.Count > 0)
            {
                error = string.Join("; ", errors);
                return null;
            }
            return profile;
        }

        /// <summary>Trims every text, empties blank ones, upper-cases the colour; returns what is still wrong (empty list: valid).</summary>
        public List<string> Normalize()
        {
            var errors = new List<string>();
            if (FormatVersion != Format)
            {
                errors.Add($"format {FormatVersion} is not supported (this plugin reads {Format})");
            }
            Name = Text(Name, "name", MaxName, errors);
            if (Name == null)
            {
                errors.Add("name is missing");
            }
            if (Adjective != null)
            {
                Adjective.Ian = Text(Adjective.Ian, "adjective.ian", MaxName, errors);
                Adjective.Anian = Text(Adjective.Anian, "adjective.anian", MaxName, errors);
                Adjective.Ians = Text(Adjective.Ians, "adjective.ians", MaxName, errors);
                if (Adjective.IsEmpty)
                {
                    Adjective = null;
                }
            }
            Color = Blank(Color)?.ToUpperInvariant();
            if (Color != null && !IsColor(Color))
            {
                errors.Add($"color '{Color}' is not #RRGGBB");
            }
            Arms = Blank(Arms);
            if (Arms != null && (Arms.Length > MaxKey || !Arms.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '_'))))
            {
                errors.Add($"arms '{Arms}' is not a kingdom key (letters, digits, _)");
            }
            Coa = Blank(Coa);
            if (Coa != null && global::URAH_Kingdoms.Coa.CoaSpec.Parse(Coa, global::URAH_Kingdoms.Coa.CrestDefs.Palette(), out string coaError) == null)
            {
                errors.Add($"coa: {coaError}");
            }
            Ruler = Text(Ruler, "ruler", MaxName, errors);
            if (Encoding.UTF8.GetByteCount(ToJson()) > MaxJson)
            {
                errors.Add($"profile is longer than {MaxJson} bytes");
            }
            return errors;
        }

        public override string ToString()
        {
            return $"'{Name}'" + (Ruler != null ? $", ruler '{Ruler}'" : "") + (Color != null ? $", color {Color}" : "") +
                (Arms != null ? $", arms of {Arms}" : "") + (Coa != null ? $", coa {Coa}" : "") + (Adjective != null ? $", adjective {Adjective}" : "");
        }

        private static string Blank(string s)
        {
            s = s?.Trim();
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static string Text(string s, string field, int max, List<string> errors)
        {
            s = Blank(s);
            if (s == null)
            {
                return null;
            }
            // StringInfo: length in characters as people count them, not UTF-16 units
            if (new StringInfo(s).LengthInTextElements > max)
            {
                errors.Add($"{field} is longer than {max} characters");
            }
            char bad = s.FirstOrDefault(c => Forbidden.IndexOf(c) >= 0 || char.IsControl(c));
            if (bad != default(char))
            {
                errors.Add($"{field} contains '{(char.IsControl(bad) ? "control character" : bad.ToString())}' (not allowed: {Forbidden})");
            }
            return s;
        }

        private static bool IsColor(string s)
        {
            return s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
        }
    }

    /// <summary>The adjective forms the game's English texts use (en_teritories.csv: ian, anian, ians); the same in every language.</summary>
    internal sealed class Adjective
    {
        [JsonProperty("ian", Order = 0, NullValueHandling = NullValueHandling.Ignore)]
        public string Ian;

        [JsonProperty("anian", Order = 1, NullValueHandling = NullValueHandling.Ignore)]
        public string Anian;

        [JsonProperty("ians", Order = 2, NullValueHandling = NullValueHandling.Ignore)]
        public string Ians;

        [JsonIgnore]
        public bool IsEmpty => Ian == null && Anian == null && Ians == null;

        public override string ToString()
        {
            return string.Join("/", new[] { Ian, Anian, Ians }.Select(s => s ?? "-"));
        }
    }
}
