using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace URAH_Kingdoms.Coa
{
    /// <summary>What a part is used as in the game's crests.</summary>
    internal enum PartKind { Division, Ordinary, Charge, Pattern }

    /// <summary>
    /// The parts the game's crests use, by a stable id (the texture name with every run of other characters
    /// than letters and digits as one '_'; a clash gets _2, _3 in name order) and a readable name.
    /// </summary>
    internal sealed class Catalogue
    {
        public readonly Dictionary<string, string> TexById = new Dictionary<string, string>();
        public readonly Dictionary<string, string> IdByTex = new Dictionary<string, string>();
        public readonly Dictionary<PartKind, List<string>> Ids = new Dictionary<PartKind, List<string>>();

        public Catalogue(IEnumerable<Crest> crests, Func<string, bool> charge)
        {
            var kinds = new Dictionary<string, PartKind>();
            foreach (Crest c in crests)
            {
                foreach (string m in Modes.All)
                {
                    if (c.Division.TryGetValue(m, out Layer d))
                    {
                        kinds[d.Tex] = PartKind.Division;
                        foreach (Layer p in d.Patterns.Values)
                        {
                            kinds[p.Tex] = PartKind.Pattern;
                        }
                    }
                    foreach (Layer o in c.OrdinariesOf(m))
                    {
                        kinds[o.Tex] = PartKind.Ordinary;
                    }
                    foreach (Layer l in c.ChargesOf(m))
                    {
                        kinds[l.Tex] = PartKind.Charge;
                    }
                }
            }
            foreach (PartKind k in Enum.GetValues(typeof(PartKind)))
            {
                Ids[k] = new List<string>();
            }
            foreach (string tex in kinds.Keys.OrderBy(t => t, StringComparer.Ordinal))
            {
                string id = Regex.Replace(tex, "[^A-Za-z0-9]+", "_").Trim('_');
                for (int n = 2; TexById.ContainsKey(id); n++)
                {
                    id = Regex.Replace(tex, "[^A-Za-z0-9]+", "_").Trim('_') + "_" + n;
                }
                TexById[id] = tex;
                IdByTex[tex] = id;
                if (kinds[tex] != PartKind.Charge || charge(tex))
                {
                    Ids[kinds[tex]].Add(id);
                }
            }
            foreach (List<string> list in Ids.Values)
            {
                list.Sort((a, b) => string.Compare(Name(a), Name(b), StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>A readable name: without the kind prefixes, numbers and suffixes of the texture names.</summary>
        public string Name(string id)
        {
            string s = TexById.TryGetValue(id, out string tex) ? tex : id;
            s = Regex.Replace(s, "^(Charges?|Division|Ordinaries|Pattern)[_ -]+", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "[_ ](Ptrn|not_Ptrn|outlined?|Ord|cut)(?=$|_)", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\(.*?\)|_\d+$|\d+$", "");
            s = Regex.Replace(s, "[_-]+", " ").Trim();
            return s.Length == 0 ? id : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }

    /// <summary>A colour of a layer: a heraldic one by name, or any as #RRGGBB.</summary>
    internal struct Tincture
    {
        public string Name; // palette name, or null
        public Rgb Value;

        public override string ToString() => Name ?? "#" + ColorUtility.ToHtmlStringRGB(Value.ToColor32());

        public static Tincture Of(Rgb c, List<KeyValuePair<string, Rgb>> palette)
        {
            foreach (var kv in palette)
            {
                if (Math.Abs(kv.Value.R - c.R) < 1.5f && Math.Abs(kv.Value.G - c.G) < 1.5f && Math.Abs(kv.Value.B - c.B) < 1.5f)
                {
                    return new Tincture { Name = kv.Key, Value = kv.Value };
                }
            }
            return new Tincture { Value = c };
        }

        public static bool TryParse(string s, List<KeyValuePair<string, Rgb>> palette, out Tincture t)
        {
            t = default;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            if (s[0] == '#')
            {
                if (s.Length != 7 || !ColorUtility.TryParseHtmlString(s, out Color c))
                {
                    return false;
                }
                t = new Tincture { Value = Rgb.From(c) };
                return true;
            }
            foreach (var kv in palette)
            {
                if (string.Equals(kv.Key, s, StringComparison.OrdinalIgnoreCase))
                {
                    t = new Tincture { Name = kv.Key, Value = kv.Value };
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>A division channel's fill: a tincture, or a pattern in its own three tinctures.</summary>
    internal sealed class Fill
    {
        public Tincture Color;
        public string Pattern; // part id, or null
        public Tincture[] PatternColors = new Tincture[3]; // r, g, b

        public Fill Clone() => new Fill { Color = Color, Pattern = Pattern, PatternColors = (Tincture[])PatternColors.Clone() };
    }

    /// <summary>A charge: part, tincture, placement (a preset, or the position and turn it had in a vanilla crest), size.</summary>
    internal sealed class ChargeSpec
    {
        public string Part;
        public Tincture Color;
        public string Preset = Presets.Centred; // null: Pos, Rot, Mirror
        public Vector2 Pos = new Vector2(0.5f, 0.55f);
        public float Rot;
        public bool MirrorX, MirrorY;
        public float Size = 0.6f; // the scale on the shield

        public ChargeSpec Clone() => (ChargeSpec)MemberwiseClone();
    }

    /// <summary>Where a charge goes: the game's CoATemplates (centred, triangle, diagonal) and a pair.</summary>
    internal static class Presets
    {
        public const string Centred = "centred", Pair = "pair", Triangle = "triangle", Diagonal = "diagonal";

        public static readonly string[] All = { Centred, Pair, Triangle, Diagonal };

        /// <summary>Positions on a shield (def coordinates: y up); the shield's centre sits a bit high, as the game's are.</summary>
        public static Vector2[] Positions(string preset, string mode)
        {
            float cy = mode == Modes.Shield || mode == Modes.SmallShield ? 0.55f : 0.5f;
            switch (preset)
            {
                case Pair:
                    return new[] { new Vector2(0.3f, cy), new Vector2(0.7f, cy) };
                case Triangle:
                    return new[] { new Vector2(0.28f, cy + 0.17f), new Vector2(0.72f, cy + 0.17f), new Vector2(0.5f, cy - 0.2f) };
                case Diagonal:
                    return new[] { new Vector2(0.25f, cy + 0.22f), new Vector2(0.5f, cy), new Vector2(0.75f, cy - 0.22f) };
                default:
                    return new[] { new Vector2(0.5f, cy) };
            }
        }
    }

    /// <summary>
    /// Arms of a profile: a vanilla crest as it is (Base, no layers), or layers made in the editor, usually
    /// starting from a vanilla crest (Base kept to know where they came from). Text form, in the profile's
    /// "coa" field, ';'-separated:
    ///   b=&lt;crest id&gt;
    ///   d=&lt;division&gt;[~x|~y|~xy],&lt;fill g&gt;,&lt;fill r&gt;     fill: tincture, or &lt;pattern&gt;/&lt;r&gt;/&lt;g&gt;/&lt;b&gt;
    ///   o=&lt;ordinary&gt;[~x|~y|~xy],&lt;tincture&gt;               (any number, painted in order)
    ///   c=&lt;charge&gt;,&lt;tincture&gt;,&lt;preset&gt;,&lt;size&gt;   or   c=&lt;charge&gt;,&lt;tincture&gt;,@x:y:turn[:x|y|xy],&lt;size&gt;
    /// tincture: a heraldic colour's name (Or, Gules ...) or #RRGGBB; parts by their Catalogue id.
    /// Flags and banners follow the shield (positions kept, sizes by the width as the game converts them);
    /// a vanilla crest without layers keeps its own flag and banner.
    /// </summary>
    internal sealed class CoaSpec
    {
        public const int MaxLength = 600;
        public const int MaxLayers = 12;

        public int? Base;
        public bool Edited; // layers below; else the Base crest as it is
        public string Division = "Solid_Color";
        public bool DivMirrorX, DivMirrorY;
        public Fill[] Fills = { new Fill(), new Fill() }; // g, r
        public List<(string Part, bool MirrorX, bool MirrorY, Tincture Color)> Ordinaries = new List<(string, bool, bool, Tincture)>();
        public List<ChargeSpec> Charges = new List<ChargeSpec>();

        public CoaSpec Clone()
        {
            var s = (CoaSpec)MemberwiseClone();
            s.Fills = Fills.Select(f => f.Clone()).ToArray();
            s.Ordinaries = new List<(string, bool, bool, Tincture)>(Ordinaries);
            s.Charges = Charges.Select(c => c.Clone()).ToList();
            return s;
        }

        // ---------------------------------------------------------------- text

        public override string ToString()
        {
            var parts = new List<string>();
            if (Base.HasValue)
            {
                parts.Add("b=" + Base.Value.ToString(CultureInfo.InvariantCulture));
            }
            if (Edited)
            {
                parts.Add($"d={Division}{Mirror(DivMirrorX, DivMirrorY)},{FillText(Fills[0])},{FillText(Fills[1])}");
                parts.AddRange(Ordinaries.Select(o => $"o={o.Part}{Mirror(o.MirrorX, o.MirrorY)},{o.Color}"));
                parts.AddRange(Charges.Select(c => $"c={c.Part},{c.Color}," +
                    (c.Preset ?? $"@{F(c.Pos.x)}:{F(c.Pos.y)}:{F(c.Rot)}" + (c.MirrorX || c.MirrorY ? ":" + (c.MirrorX ? "x" : "") + (c.MirrorY ? "y" : "") : "")) +
                    "," + F(c.Size)));
            }
            return string.Join(";", parts);
        }

        private static string Mirror(bool x, bool y) => x || y ? "~" + (x ? "x" : "") + (y ? "y" : "") : "";

        private static string FillText(Fill f) =>
            f.Pattern == null ? f.Color.ToString() : f.Pattern + "/" + string.Join("/", f.PatternColors.Select(t => t.ToString()));

        private static string F(float v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Reads the text form; null and an error when it is not valid (parts are checked later, against the game's).</summary>
        public static CoaSpec Parse(string text, List<KeyValuePair<string, Rgb>> palette, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(text))
            {
                error = "empty";
                return null;
            }
            if (text.Length > MaxLength)
            {
                error = $"longer than {MaxLength} characters";
                return null;
            }
            if (text.Any(ch => ch >= 128 || !(char.IsLetterOrDigit(ch) || "_;=,/#@:~.-".IndexOf(ch) >= 0)))
            {
                error = "contains other characters than letters, digits and _;=,/#@:~.-";
                return null;
            }
            var s = new CoaSpec();
            int layers = 0;
            foreach (string item in text.Split(';'))
            {
                int eq = item.IndexOf('=');
                if (eq != 1)
                {
                    error = $"'{item}' is not k=value";
                    return null;
                }
                string[] f = item.Substring(2).Split(',');
                switch (item[0])
                {
                    case 'b':
                        if (!int.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out int b) || f.Length != 1)
                        {
                            error = $"'{item}': not a crest id";
                            return null;
                        }
                        s.Base = b;
                        break;
                    case 'd':
                        if (f.Length != 3 || !Part(f[0], out s.Division, out s.DivMirrorX, out s.DivMirrorY)
                            || !TryFill(f[1], palette, out s.Fills[0]) || !TryFill(f[2], palette, out s.Fills[1]))
                        {
                            error = $"'{item}': not division,fill,fill";
                            return null;
                        }
                        s.Edited = true;
                        break;
                    case 'o':
                        if (f.Length != 2 || !Part(f[0], out string op, out bool ox, out bool oy) || !Tincture.TryParse(f[1], palette, out Tincture ot))
                        {
                            error = $"'{item}': not ordinary,tincture";
                            return null;
                        }
                        s.Ordinaries.Add((op, ox, oy, ot));
                        s.Edited = true;
                        layers++;
                        break;
                    case 'c':
                        var c = new ChargeSpec();
                        if (f.Length != 4 || !Id(f[0]) || !Tincture.TryParse(f[1], palette, out c.Color) || !Placement(f[2], c)
                            || !float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out c.Size) || c.Size < 0.05f || c.Size > 3f)
                        {
                            error = $"'{item}': not charge,tincture,placement,size";
                            return null;
                        }
                        c.Part = f[0];
                        s.Charges.Add(c);
                        s.Edited = true;
                        layers++;
                        break;
                    default:
                        error = $"'{item}': unknown";
                        return null;
                }
            }
            if (layers > MaxLayers)
            {
                error = $"more than {MaxLayers} ordinaries and charges";
                return null;
            }
            if (!s.Base.HasValue && !s.Edited)
            {
                error = "neither a crest nor layers";
                return null;
            }
            return s;
        }

        private static bool Id(string s) => s.Length > 0 && s.Length <= 64 && s.All(ch => char.IsLetterOrDigit(ch) || ch == '_');

        private static bool Part(string s, out string id, out bool mx, out bool my)
        {
            int t = s.IndexOf('~');
            id = t < 0 ? s : s.Substring(0, t);
            string fl = t < 0 ? "" : s.Substring(t + 1);
            mx = fl.Contains("x");
            my = fl.Contains("y");
            return Id(id) && fl.All(ch => ch == 'x' || ch == 'y');
        }

        private static bool TryFill(string s, List<KeyValuePair<string, Rgb>> palette, out Fill fill)
        {
            fill = new Fill();
            string[] p = s.Split('/');
            if (p.Length == 1)
            {
                return Tincture.TryParse(p[0], palette, out fill.Color);
            }
            if (p.Length != 4 || !Id(p[0]))
            {
                return false;
            }
            fill.Pattern = p[0];
            for (int i = 0; i < 3; i++)
            {
                if (!Tincture.TryParse(p[i + 1], palette, out fill.PatternColors[i]))
                {
                    return false;
                }
            }
            fill.Color = fill.PatternColors[1];
            return true;
        }

        private static bool Placement(string s, ChargeSpec c)
        {
            if (Presets.All.Contains(s))
            {
                c.Preset = s;
                return true;
            }
            if (s.Length < 2 || s[0] != '@')
            {
                return false;
            }
            string[] p = s.Substring(1).Split(':');
            if (p.Length < 3 || p.Length > 4
                || !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out c.Rot)
                || x < -1f || x > 2f || y < -1f || y > 2f)
            {
                return false;
            }
            c.Preset = null;
            c.Pos = new Vector2(x, y);
            c.MirrorX = p.Length == 4 && p[3].Contains("x");
            c.MirrorY = p.Length == 4 && p[3].Contains("y");
            return true;
        }

        // ---------------------------------------------------------------- game crests

        /// <summary>The layers of a vanilla crest's shield, to edit (vanilla placements kept as they are).</summary>
        public static CoaSpec FromCrest(Crest c, Catalogue cat, List<KeyValuePair<string, Rgb>> palette)
        {
            var s = new CoaSpec { Base = c.Id, Edited = true };
            if (c.Division.TryGetValue(Modes.Shield, out Layer d) && cat.IdByTex.TryGetValue(d.Tex, out string did))
            {
                s.Division = did;
                s.DivMirrorX = d.Tile.x < 0f;
                s.DivMirrorY = d.Tile.y < 0f;
                for (int i = 0; i < 2; i++)
                {
                    char ch = i == 0 ? 'g' : 'r';
                    var fill = new Fill { Color = Tincture.Of(d.Tint(ch), palette) };
                    if (d.Patterns.TryGetValue(ch, out Layer p) && cat.IdByTex.TryGetValue(p.Tex, out string pid))
                    {
                        fill.Pattern = pid;
                        fill.PatternColors = new[] { Tincture.Of(p.Tint('r'), palette), Tincture.Of(p.Tint('g'), palette), Tincture.Of(p.Tint('b'), palette) };
                        fill.Color = fill.PatternColors[1];
                    }
                    s.Fills[i] = fill;
                }
            }
            foreach (Layer o in c.OrdinariesOf(Modes.Shield))
            {
                if (cat.IdByTex.TryGetValue(o.Tex, out string oid))
                {
                    s.Ordinaries.Add((oid, o.Tile.x < 0f, o.Tile.y < 0f, Tincture.Of(o.MainTint, palette)));
                }
            }
            foreach (Layer l in c.ChargesOf(Modes.Shield))
            {
                if (cat.IdByTex.TryGetValue(l.Tex, out string cid))
                {
                    s.Charges.Add(new ChargeSpec
                    {
                        Part = cid,
                        Color = Tincture.Of(l.MainTint, palette),
                        Preset = null,
                        Pos = l.Pos,
                        Rot = l.Rot,
                        MirrorX = l.Tile.x < 0f,
                        MirrorY = l.Tile.y < 0f,
                        Size = l.Scale.x,
                    });
                }
            }
            return s;
        }

        /// <summary>
        /// The crest to paint: the Base crest as it is when not edited, else the layers in every mode (flags and
        /// banners: positions kept, sizes by the width as CoatOfArmsUtility.ConvertChargeTransform does).
        /// Null when there is nothing to paint (an unknown base crest).
        /// </summary>
        public Crest ToCrest(Parts parts)
        {
            if (!Edited)
            {
                return Base.HasValue && parts.Crests.TryGetValue(Base.Value, out Crest vanilla) ? vanilla : null;
            }
            Catalogue cat = parts.Catalogue;
            var c = new Crest { Id = -1 };
            foreach (string m in Modes.All)
            {
                var d = new Layer { Tex = Tex(cat, Division) ?? "Solid_Color", Tile = new Vector2(DivMirrorX ? -1f : 1f, DivMirrorY ? -1f : 1f) };
                for (int i = 0; i < 2; i++)
                {
                    char ch = i == 0 ? 'g' : 'r';
                    Fill f = Fills[i];
                    d.Tints[ch] = f.Color.Value;
                    string pt = f.Pattern != null ? Tex(cat, f.Pattern) : null;
                    if (pt != null)
                    {
                        var p = new Layer { Tex = pt };
                        p.Tints['r'] = f.PatternColors[0].Value;
                        p.Tints['g'] = f.PatternColors[1].Value;
                        p.Tints['b'] = f.PatternColors[2].Value;
                        d.Patterns[ch] = p;
                    }
                }
                c.Division[m] = d;
                c.Ordinaries[m] = Ordinaries.Where(o => Tex(cat, o.Part) != null).Select(o =>
                {
                    var l = new Layer { Tex = Tex(cat, o.Part), Tile = new Vector2(o.MirrorX ? -1f : 1f, o.MirrorY ? -1f : 1f) };
                    l.Tints['r'] = o.Color.Value;
                    return l;
                }).ToList();
                float widthRatio = (float)Modes.Width(m) / Modes.Width(Modes.Shield);
                var charges = new List<Layer>();
                foreach (ChargeSpec cs in Charges)
                {
                    string tex = Tex(cat, cs.Part);
                    if (tex == null)
                    {
                        continue;
                    }
                    Vector2[] at = cs.Preset != null ? Presets.Positions(cs.Preset, m) : new[] { cs.Pos };
                    foreach (Vector2 pos in at)
                    {
                        var l = new Layer
                        {
                            Tex = tex,
                            Pos = pos,
                            Scale = Vector2.one * cs.Size * widthRatio,
                            Rot = cs.Rot,
                            Tile = new Vector2(cs.MirrorX ? -1f : 1f, cs.MirrorY ? -1f : 1f),
                        };
                        l.Tints['r'] = cs.Color.Value;
                        charges.Add(l);
                    }
                }
                c.Charges[m] = charges;
            }
            return c;
        }

        private static string Tex(Catalogue cat, string id) => id != null && cat.TexById.TryGetValue(id, out string t) ? t : null;
    }
}
