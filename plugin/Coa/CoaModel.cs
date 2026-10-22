using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Logic;
using UnityEngine;
using Path = System.IO.Path;

namespace URAH_Kingdoms.Coa
{
    /// <summary>The four kinds of arms images and their tile sizes (Defs/CoatOfArms/coatOfArms.def, CoatOfArmsModes).</summary>
    internal static class Modes
    {
        public const string Shield = "shield", Flag = "flag", Banner = "banner", SmallShield = "small_shield";

        public static readonly string[] All = { Shield, Flag, Banner, SmallShield };

        public static int Width(string m) => m == Flag ? 128 : m == Banner ? 64 : m == SmallShield ? 36 : 72;

        public static int Height(string m) => m == Flag ? 64 : m == Banner ? 128 : m == SmallShield ? 48 : 96;

        /// <summary>min / max of the tile's sides: a charge's drawn size is its texture size × scale × this.</summary>
        public static float Ratio(string m) => (float)Math.Min(Width(m), Height(m)) / Math.Max(Width(m), Height(m));
    }

    /// <summary>An RGB colour, 0-255 per channel, as the atlases and the painter use them.</summary>
    internal struct Rgb
    {
        public float R, G, B;

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public static Rgb From(Color c) => new Rgb(c.r * 255f, c.g * 255f, c.b * 255f);

        public Color32 ToColor32() => new Color32(Byte(R), Byte(G), Byte(B), 255);

        private static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        /// <summary>A tint channel without a tint keeps its own colour (TextureMapper.GetTintColor).</summary>
        public static Rgb Default(char channel) =>
            channel == 'r' ? new Rgb(255, 0, 0) : channel == 'g' ? new Rgb(0, 255, 0) : new Rgb(0, 0, 255);
    }

    /// <summary>One part of a crest in one mode: a division, an ordinary, a charge or a pattern.</summary>
    internal sealed class Layer
    {
        public string Tex; // the texture's file name without .png
        public Dictionary<char, Rgb> Tints = new Dictionary<char, Rgb>();
        public Dictionary<char, Layer> Patterns = new Dictionary<char, Layer>(); // a channel filled with a pattern
        public Vector2 Pos = Vector2.zero, Scale = Vector2.one, Pivot = new Vector2(0.5f, 0.5f);
        public float Rot;
        public Vector2 Tile = Vector2.one, Offset = Vector2.zero; // tile -1: mirrored

        public Rgb Tint(char channel) => Tints.TryGetValue(channel, out Rgb c) ? c : Rgb.Default(channel);

        /// <summary>The colour of a charge or an ordinary: its one channel.</summary>
        public Rgb MainTint => Tints.Count > 0 ? Tints.Values.First() : Rgb.Default('r');

        public Layer Clone()
        {
            var l = (Layer)MemberwiseClone();
            l.Tints = new Dictionary<char, Rgb>(Tints);
            l.Patterns = Patterns.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());
            return l;
        }
    }

    /// <summary>A crest: per mode its division, ordinaries and charges, as the game draws them.</summary>
    internal sealed class Crest
    {
        public int Id;
        public readonly Dictionary<string, Layer> Division = new Dictionary<string, Layer>();
        public readonly Dictionary<string, List<Layer>> Ordinaries = new Dictionary<string, List<Layer>>();
        public readonly Dictionary<string, List<Layer>> Charges = new Dictionary<string, List<Layer>>();

        public List<Layer> OrdinariesOf(string m) => Ordinaries.TryGetValue(m, out var l) ? l : new List<Layer>();

        public List<Layer> ChargesOf(string m) => Charges.TryGetValue(m, out var l) ? l : new List<Layer>();
    }

    /// <summary>
    /// The game's crests, read from its own Maps/europe/coatOfArmsCrests.def with its own DT parser (a DT of
    /// their own, so the game's defs stay untouched), the way CoatOfArmsCrest(DT.Field) builds them per mode.
    /// </summary>
    internal static class CrestDefs
    {
        public static string EuropeDir => Path.Combine(Application.dataPath, "..", "Maps", "europe");

        /// <summary>CoatOfArmsPaletteColors: the heraldic colours by name, in the def's order.</summary>
        public static List<KeyValuePair<string, Rgb>> Palette()
        {
            var list = new List<KeyValuePair<string, Rgb>>();
            DT.Field def = Defs.GetDefField("CoatOfArmsPaletteColors");
            foreach (DT.Field f in def?.children ?? new List<DT.Field>())
            {
                if (!string.IsNullOrEmpty(f.key) && !string.IsNullOrEmpty(f.value_str))
                {
                    list.Add(new KeyValuePair<string, Rgb>(f.key, Rgb.From(Defs.ColorFromString(f.value_str, Color.white))));
                }
            }
            return list;
        }

        public static Dictionary<int, Crest> Load()
        {
            string path = Path.Combine(EuropeDir, "coatOfArmsCrests.def");
            DT.Field file = new DT().LoadDefFile(path) ?? throw new IOException("cannot read " + path);
            DT.Field europe = file.children?.Where(f => f.key == "CoatOfArmsCrests").Select(f => Child(f, "europe")).FirstOrDefault(f => f != null)
                ?? throw new InvalidDataException("no CoatOfArmsCrests.europe in " + path);
            Dictionary<string, Rgb> palette = Palette().ToDictionary(kv => kv.Key, kv => kv.Value);
            var crests = new Dictionary<int, Crest>();
            foreach (DT.Field f in europe.children ?? new List<DT.Field>())
            {
                if (int.TryParse(f.key, out int id))
                {
                    crests[id] = Crest(f, id, palette);
                }
            }
            return crests;
        }

        private static Crest Crest(DT.Field f, int id, Dictionary<string, Rgb> pal)
        {
            var c = new Crest { Id = id };
            string first = Modes.All[0];
            DT.Field baseDiv = Child(f, "division");
            foreach (string m in Modes.All)
            {
                DT.Field d = Child(f, "division_" + m);
                Layer lay;
                if (d != null)
                {
                    lay = Layer(d, pal, m);
                }
                else if (baseDiv != null && FindInChildren(f, m) != null)
                {
                    lay = Layer(baseDiv, pal, m);
                }
                else
                {
                    lay = m != first ? (c.Division.TryGetValue(first, out Layer l0) ? l0.Clone() : null)
                        : (baseDiv != null ? Layer(baseDiv, pal, null) : null);
                }
                if (lay != null)
                {
                    c.Division[m] = lay;
                }
            }
            foreach (string grp in new[] { "ordinaries", "charges" })
            {
                Dictionary<string, List<Layer>> output = grp == "charges" ? c.Charges : c.Ordinaries;
                DT.Field baseF = Child(f, grp);
                List<DT.Field> baseChildren = baseF?.children?.Where(x => !string.IsNullOrEmpty(x.key)).ToList() ?? new List<DT.Field>();
                List<Layer> baseList = baseChildren.Select(x => Layer(x, pal, null)).Where(x => x != null).ToList();
                for (int i = 0; i < Modes.All.Length; i++)
                {
                    string m = Modes.All[i];
                    DT.Field g = Child(f, grp + "_" + m);
                    List<Layer> lays = null;
                    if (g?.children != null && g.children.Any(x => !string.IsNullOrEmpty(x.key)))
                    {
                        lays = g.children.Where(x => !string.IsNullOrEmpty(x.key)).Select(x => Layer(x, pal, m)).Where(x => x != null).ToList();
                    }
                    else if (baseChildren.Count > 0 && baseChildren.Any(x => FindInChildren(x, m) != null))
                    {
                        lays = grp == "ordinaries" ? baseList // the game keeps the base list here
                            : baseChildren.Where(x => FindInChildren(x, m) != null).Select(x => Layer(x, pal, m)).Where(x => x != null).ToList();
                    }
                    if (i == 0 && lays != null)
                    {
                        baseList = lays;
                    }
                    if (lays == null)
                    {
                        float r = (float)Modes.Width(m) / Modes.Width(first); // CoatOfArmsUtility.ConvertChargeTransform
                        lays = baseList.Select(x =>
                        {
                            Layer y = x.Clone();
                            if (grp == "charges")
                            {
                                y.Scale *= r;
                            }
                            return y;
                        }).ToList();
                    }
                    output[m] = lays;
                }
            }
            return c;
        }

        /// <summary>CoatOfArmsTexture(def, mode): texture and mapper from f, the transform from f's block for the mode if any.</summary>
        private static Layer Layer(DT.Field f, Dictionary<string, Rgb> pal, string mode)
        {
            string t = TexName(Child(f, "t")?.value_str);
            if (string.IsNullOrEmpty(t))
            {
                return null;
            }
            DT.Field tf = (mode != null ? FindInChildren(f, mode) : null) ?? f;
            var lay = new Layer { Tex = t };
            DT.Field mapper = Child(f, "mapper");
            foreach (DT.Field ch in mapper?.children ?? new List<DT.Field>())
            {
                if (string.IsNullOrEmpty(ch.key) || "rgb".IndexOf(ch.key[0]) < 0 || ch.key.Length != 1)
                {
                    continue;
                }
                DT.Field tint = Child(ch, "tint");
                if (tint != null && !string.IsNullOrEmpty(tint.value_str))
                {
                    string v = DT.Unquote(tint.value_str);
                    lay.Tints[ch.key[0]] = pal.TryGetValue(v, out Rgb c) ? c : Rgb.From(Defs.ColorFromString(v, Color.white));
                }
                DT.Field tex = Child(ch, "texture");
                if (tex != null && Child(tex, "t") != null)
                {
                    Layer p = Layer(tex, pal, mode);
                    if (p != null)
                    {
                        lay.Patterns[ch.key[0]] = p;
                    }
                }
            }
            if (lay.Tints.Count == 0 && lay.Patterns.Count == 0)
            {
                lay.Tints['r'] = Rgb.Default('r'); // no mapper: the channel keeps its own colour
            }
            lay.Pos = Point(tf, "position", Vector2.zero);
            lay.Scale = Point(tf, "scale", Vector2.one);
            lay.Pivot = Point(tf, "pivot", new Vector2(0.5f, 0.5f));
            lay.Rot = Float(tf, "rotation", 0f);
            lay.Tile = new Vector2(Float(tf, "tile_x", 1f), Float(tf, "tile_y", 1f));
            lay.Offset = new Vector2(Float(tf, "offset_x", 0f), Float(tf, "offset_y", 0f));
            return lay;
        }

        public static string TexName(string v)
        {
            if (string.IsNullOrEmpty(v))
            {
                return null;
            }
            v = DT.Unquote(v);
            int slash = v.LastIndexOf('/');
            v = slash >= 0 ? v.Substring(slash + 1) : v;
            return v.EndsWith(".png") ? v.Substring(0, v.Length - 4) : v;
        }

        private static DT.Field Child(DT.Field f, string key) => f?.children?.FirstOrDefault(c => c.key == key);

        /// <summary>CoatOfArmsUtility.FindInChildren: a child named key, else the first one found depth-first.</summary>
        private static DT.Field FindInChildren(DT.Field f, string key)
        {
            if (f?.children == null)
            {
                return null;
            }
            DT.Field c = Child(f, key);
            if (c != null)
            {
                return c;
            }
            foreach (DT.Field ch in f.children)
            {
                DT.Field r = FindInChildren(ch, key);
                if (r != null)
                {
                    return r;
                }
            }
            return null;
        }

        private static Vector2 Point(DT.Field f, string key, Vector2 def)
        {
            DT.Field c = Child(f, key);
            if (c == null)
            {
                return def;
            }
            float[] v = Numbers(c);
            return v.Length == 0 ? def : new Vector2(v[0], v.Length > 1 ? v[1] : v[0]);
        }

        private static float Float(DT.Field f, string key, float def)
        {
            DT.Field c = Child(f, key);
            if (c == null)
            {
                return def;
            }
            float[] v = Numbers(c);
            return v.Length == 0 ? def : v[0];
        }

        /// <summary>The numbers of a value: "0.5, 0.55" or one number (the parsed value if the text is empty).</summary>
        private static float[] Numbers(DT.Field c)
        {
            string s = c.value_str;
            if (string.IsNullOrEmpty(s))
            {
                Point p = DT.GetPoint(c);
                return new[] { p.x, p.y };
            }
            return s.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => float.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? (float?)v : null)
                .Where(v => v.HasValue).Select(v => v.Value).ToArray();
        }
    }
}
