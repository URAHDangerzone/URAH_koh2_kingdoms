using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace URAH_Kingdoms.Coa
{
    /// <summary>An RGB image, 0-255, rows top to bottom (as the atlases look, not as Unity stores them).</summary>
    internal sealed class Img
    {
        public readonly int W, H;
        public readonly float[] P;

        public Img(int w, int h)
        {
            W = w;
            H = h;
            P = new float[w * h * 3];
        }

        /// <summary>A texture of it (Unity's rows bottom to top), not readable afterwards.</summary>
        public Texture2D ToTexture()
        {
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = (y * W + x) * 3;
                    px[(H - 1 - y) * W + x] = new Rgb(P[i], P[i + 1], P[i + 2]).ToColor32();
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, mipChain: false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
        }
    }

    /// <summary>A charge cut out of an atlas: coverage A and shade S per pixel, the anchor (where the def's
    /// position points) and the draw scale it was cut at; drawn at any other scale, turned and mirrored.</summary>
    internal sealed class ChargeSprite
    {
        public int W, H;
        public float[] A, S;
        public float Ax, Ay, Kx, Ky;
        public string Source;
    }

    /// <summary>
    /// The game's coat of arms parts, cut back out of its atlases (Maps/europe/coatOfArmsAtlas&lt;mode&gt;.png):
    /// the part textures themselves are not shipped. Same rules as tools/coa_parts.py, which also writes the
    /// recipes (coa_recipes.txt: where each charge is cut, in order):
    /// - division, ordinary (per mode): per pixel the share of the second colour, median over every crest that
    ///   uses it; patterns: the weights of their r, g, b channels (the rest: black, the shading), from the crests
    ///   where the channel they fill shows
    /// - charge: against the crest painted without it, per pixel colour = background * (1 - a) + a * w * tint
    /// </summary>
    internal sealed class Parts
    {
        public Dictionary<int, Crest> Crests;
        public Catalogue Catalogue;
        public List<KeyValuePair<string, Rgb>> Palette;

        /// <summary>The shield atlas as a texture for the UI (not readable); a crest's tile through ShieldUv.</summary>
        public Texture2D ShieldAtlas;
        public readonly Dictionary<string, ChargeSprite> Sprites = new Dictionary<string, ChargeSprite>();
        private readonly Dictionary<string, float[]> masks = new Dictionary<string, float[]>(); // 1 or 3 values per pixel
        private readonly Dictionary<string, Color32[]> atlas = new Dictionary<string, Color32[]>();
        private readonly Dictionary<string, Vector2Int> atlasSize = new Dictionary<string, Vector2Int>();

        private static Parts instance;

        /// <summary>The parts, cut on first use and kept for the session; null if that failed (see the log).</summary>
        public static Parts Get()
        {
            if (instance != null)
            {
                return instance;
            }
            try
            {
                var sw = Stopwatch.StartNew();
                var p = new Parts();
                p.Build();
                instance = p;
                Plugin.Log.LogInfo($"Coat of arms parts: {p.Crests.Count} crests, {p.Sprites.Count} charges, {p.masks.Count} division/ordinary/pattern masks in {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Coat of arms parts: {e}");
            }
            return instance;
        }

        private void Build()
        {
            Crests = CrestDefs.Load();
            Palette = CrestDefs.Palette();
            foreach (string m in Modes.All)
            {
                string path = Path.Combine(CrestDefs.EuropeDir, $"coatOfArmsAtlas{m}.png");
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                try
                {
                    if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path)))
                    {
                        throw new InvalidDataException("cannot read " + path);
                    }
                    atlas[m] = tex.GetPixels32();
                    atlasSize[m] = new Vector2Int(tex.width, tex.height);
                    if (m == Modes.Shield)
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
                        ShieldAtlas = tex;
                        tex = null;
                    }
                }
                finally
                {
                    if (tex != null)
                    {
                        UnityEngine.Object.Destroy(tex);
                    }
                }
            }
            VoteMasks();
            CutCharges();
            Catalogue = new Catalogue(Crests.Values, tex => Sprites.ContainsKey(tex));
            Plugin.Log.LogInfo("Coat of arms parts, painted again: " + Check(Modes.Shield) + "; " + Check(Modes.Flag));
            atlas.Clear(); // only needed for cutting (atlasSize stays: ShieldUv)
        }

        // ---------------------------------------------------------------- atlases

        /// <summary>A crest's tile in ShieldAtlas as UV coordinates (Unity's: y up).</summary>
        public Rect ShieldUv(int cid)
        {
            int w = Modes.Width(Modes.Shield), h = Modes.Height(Modes.Shield);
            Vector2Int size = atlasSize[Modes.Shield];
            int cols = size.x / w;
            return new Rect((float)(cid % cols) * w / size.x, (float)(cid / cols) * h / size.y, (float)w / size.x, (float)h / size.y);
        }

        /// <summary>Crest id's tile of a mode, rows top to bottom (tiles row-major, row 0 at the texture's bottom).</summary>
        private float[] Tile(int cid, string m)
        {
            int w = Modes.Width(m), h = Modes.Height(m);
            Vector2Int size = atlasSize[m];
            int cols = size.x / w, c = cid % cols, r = cid / cols;
            Color32[] px = atlas[m];
            var t = new float[w * h * 3];
            for (int ty = 0; ty < h; ty++)
            {
                int uy = (r + 1) * h - 1 - ty;
                if (uy < 0 || uy >= size.y)
                {
                    continue;
                }
                for (int tx = 0; tx < w; tx++)
                {
                    Color32 p = px[uy * size.x + c * w + tx];
                    int i = (ty * w + tx) * 3;
                    t[i] = p.r;
                    t[i + 1] = p.g;
                    t[i + 2] = p.b;
                }
            }
            return t;
        }

        // ---------------------------------------------------------------- divisions, ordinaries, patterns

        private static string MaskKey(string kind, Layer l, string m) =>
            $"{kind}|{l.Tex}|{m}|{Math.Abs(l.Tile.x):R}|{Math.Abs(l.Tile.y):R}|{l.Offset.x:R}|{l.Offset.y:R}";

        private void VoteMasks()
        {
            var votes = new Dictionary<string, List<float[]>>();
            foreach (Crest c in Crests.Values)
            {
                foreach (string m in Modes.All)
                {
                    if (!c.Division.TryGetValue(m, out Layer d) || d.Patterns.Count > 0)
                    {
                        continue;
                    }
                    float[] t = null;
                    Rgb g = d.Tint('g'), r = d.Tint('r');
                    if (d.Tex != "Solid_Color" && Dist(g, r) > 40f)
                    {
                        t = Tile(c.Id, m);
                        Add(votes, MaskKey("div", d, m), Unflip(Share(t, g, r), Modes.Width(m), Modes.Height(m), 1, d));
                    }
                    List<Layer> ords = c.OrdinariesOf(m);
                    if (ords.Count == 1 && d.Tex == "Solid_Color" && ords[0].Patterns.Count == 0 && ords[0].Rot == 0f)
                    {
                        Rgb tint = ords[0].MainTint;
                        if (Dist(g, tint) > 40f)
                        {
                            t = t ?? Tile(c.Id, m);
                            Add(votes, MaskKey("ord", ords[0], m), Unflip(Share(t, g, tint), Modes.Width(m), Modes.Height(m), 1, ords[0]));
                        }
                    }
                }
            }
            Median(votes, 1);

            votes.Clear();
            foreach (Crest c in Crests.Values)
            {
                foreach (string m in Modes.All)
                {
                    if (!c.Division.TryGetValue(m, out Layer d) || d.Patterns.Count == 0)
                    {
                        continue;
                    }
                    int w = Modes.Width(m), h = Modes.Height(m);
                    float[] M;
                    if (d.Tex == "Solid_Color")
                    {
                        M = new float[w * h];
                    }
                    else if (masks.TryGetValue(MaskKey("div", d, m), out float[] dm))
                    {
                        M = Unflip(dm, w, h, 1, d);
                    }
                    else
                    {
                        continue;
                    }
                    float[] t = Tile(c.Id, m);
                    foreach (var kv in d.Patterns)
                    {
                        if (kv.Key != 'g' && kv.Key != 'r')
                        {
                            continue;
                        }
                        float[] wts = Weights(t, kv.Value, out float[] off);
                        bool any = false;
                        for (int i = 0; i < w * h; i++)
                        {
                            bool region = kv.Key == 'g' ? M[i] < 0.05f : M[i] > 0.95f;
                            if (!region || off[i] >= 12f)
                            {
                                wts[i * 3] = wts[i * 3 + 1] = wts[i * 3 + 2] = float.NaN;
                            }
                            else
                            {
                                any = true;
                            }
                        }
                        if (any)
                        {
                            Add(votes, MaskKey("pat", kv.Value, m), Unflip(wts, w, h, 3, kv.Value));
                        }
                    }
                }
            }
            Median(votes, 3);
        }

        private static void Add(Dictionary<string, List<float[]>> votes, string key, float[] v)
        {
            if (!votes.TryGetValue(key, out var list))
            {
                votes[key] = list = new List<float[]>();
            }
            list.Add(v);
        }

        /// <summary>Per pixel the median of the votes (one value), or the best vote with its gaps filled from the
        /// others (three: a median per channel would mix different splits); then the gaps filled from neighbours.</summary>
        private void Median(Dictionary<string, List<float[]>> votes, int ch)
        {
            foreach (var kv in votes)
            {
                List<float[]> vs = kv.Value;
                int n = vs[0].Length;
                var med = new float[n];
                if (ch == 1)
                {
                    var buf = new List<float>(vs.Count);
                    for (int i = 0; i < n; i++)
                    {
                        buf.Clear();
                        foreach (float[] v in vs)
                        {
                            if (!float.IsNaN(v[i]))
                            {
                                buf.Add(v[i]);
                            }
                        }
                        if (buf.Count == 0)
                        {
                            med[i] = float.NaN;
                            continue;
                        }
                        buf.Sort();
                        int k = buf.Count / 2;
                        med[i] = buf.Count % 2 == 1 ? buf[k] : (buf[k - 1] + buf[k]) / 2f;
                    }
                }
                else
                {
                    List<float[]> order = vs.OrderBy(v => Enumerable.Range(0, n / 3).Count(i => float.IsNaN(v[i * 3]))).ToList();
                    Array.Copy(order[0], med, n);
                    foreach (float[] v in order.Skip(1))
                    {
                        for (int i = 0; i < n; i += 3)
                        {
                            if (float.IsNaN(med[i]))
                            {
                                med[i] = v[i];
                                med[i + 1] = v[i + 1];
                                med[i + 2] = v[i + 2];
                            }
                        }
                    }
                }
                string m = kv.Key.Split('|')[2];
                masks[kv.Key] = Inpaint(med, Modes.Width(m), Modes.Height(m), ch);
            }
        }

        /// <summary>NaN pixels (covered in every crest) get the mean of their known neighbours, outwards.</summary>
        private static float[] Inpaint(float[] a, int w, int h, int ch)
        {
            for (int c = 0; c < ch; c++)
            {
                while (true)
                {
                    var fill = new List<KeyValuePair<int, float>>();
                    bool anyKnown = false, anyNan = false;
                    for (int y = 0; y < h; y++)
                    {
                        for (int x = 0; x < w; x++)
                        {
                            float v = a[(y * w + x) * ch + c];
                            if (!float.IsNaN(v))
                            {
                                anyKnown = true;
                                continue;
                            }
                            anyNan = true;
                            float s = 0f;
                            int n = 0;
                            for (int dy = -1; dy <= 1; dy++)
                            {
                                for (int dx = -1; dx <= 1; dx++)
                                {
                                    int xx = x + dx, yy = y + dy;
                                    if (xx < 0 || yy < 0 || xx >= w || yy >= h)
                                    {
                                        continue;
                                    }
                                    float u = a[(yy * w + xx) * ch + c];
                                    if (!float.IsNaN(u))
                                    {
                                        s += u;
                                        n++;
                                    }
                                }
                            }
                            if (n > 0)
                            {
                                fill.Add(new KeyValuePair<int, float>((y * w + x) * ch + c, s / n));
                            }
                        }
                    }
                    if (!anyNan)
                    {
                        break;
                    }
                    if (!anyKnown)
                    {
                        for (int i = c; i < a.Length; i += ch)
                        {
                            a[i] = 0f;
                        }
                        break;
                    }
                    foreach (var kv in fill)
                    {
                        a[kv.Key] = kv.Value;
                    }
                }
            }
            return a;
        }

        /// <summary>Share of c1 on the segment c0 -> c1 per pixel; NaN where the pixel is 12 or more off it.</summary>
        private static float[] Share(float[] px, Rgb c0, Rgb c1)
        {
            int n = px.Length / 3;
            var t = new float[n];
            float dr = c1.R - c0.R, dg = c1.G - c0.G, db = c1.B - c0.B;
            float dd = Math.Max(dr * dr + dg * dg + db * db, 1e-6f);
            for (int i = 0; i < n; i++)
            {
                float pr = px[i * 3] - c0.R, pg = px[i * 3 + 1] - c0.G, pb = px[i * 3 + 2] - c0.B;
                float s = Mathf.Clamp01((pr * dr + pg * dg + pb * db) / dd);
                float er = pr - s * dr, eg = pg - s * dg, eb = pb - s * db;
                t[i] = Mathf.Sqrt(er * er + eg * eg + eb * eb) < 12f ? s : float.NaN;
            }
            return t;
        }

        /// <summary>
        /// px = w_r * tint_r + w_g * tint_g + w_b * tint_b (1 - sum: black, the shading), weights >= 0 with sum
        /// &lt;= 1: the best of the fits on every subset of the channels (fewer channels win ties).
        /// </summary>
        private static float[] Weights(float[] px, Layer lay, out float[] off)
        {
            Rgb[] T = { lay.Tint('r'), lay.Tint('g'), lay.Tint('b') };
            int[][] subsets = { new[] { 0 }, new[] { 1 }, new[] { 2 }, new[] { 0, 1 }, new[] { 0, 2 }, new[] { 1, 2 }, new[] { 0, 1, 2 } };
            int n = px.Length / 3;
            var best = new float[n * 3];
            var bestE = new float[n];
            off = new float[n];
            for (int i = 0; i < n; i++)
            {
                bestE[i] = Len(px[i * 3], px[i * 3 + 1], px[i * 3 + 2]);
            }
            var w = new float[3];
            foreach (int[] sub in subsets)
            {
                int k = sub.Length;
                // Gram matrix G = Ts Ts^T and its inverse (least squares on the subset)
                var G = new double[k, k];
                for (int a = 0; a < k; a++)
                {
                    for (int b = 0; b < k; b++)
                    {
                        G[a, b] = Dot(T[sub[a]], T[sub[b]]);
                    }
                }
                double[,] inv = Invert(G);
                if (inv == null)
                {
                    continue;
                }
                for (int i = 0; i < n; i++)
                {
                    float r = px[i * 3], g = px[i * 3 + 1], b = px[i * 3 + 2];
                    bool ok = true;
                    float sum = 0f;
                    w[0] = w[1] = w[2] = 0f;
                    for (int a = 0; a < k; a++)
                    {
                        double v = 0;
                        for (int c = 0; c < k; c++)
                        {
                            Rgb tc = T[sub[c]];
                            v += inv[a, c] * (tc.R * r + tc.G * g + tc.B * b);
                        }
                        if (v < -1e-3)
                        {
                            ok = false;
                        }
                        sum += (float)v;
                        w[sub[a]] = Mathf.Clamp01((float)v);
                    }
                    if (!ok || sum > 1f + 1e-3f)
                    {
                        continue;
                    }
                    float er = r - (w[0] * T[0].R + w[1] * T[1].R + w[2] * T[2].R);
                    float eg = g - (w[0] * T[0].G + w[1] * T[1].G + w[2] * T[2].G);
                    float eb = b - (w[0] * T[0].B + w[1] * T[1].B + w[2] * T[2].B);
                    float e = Len(er, eg, eb) + 0.5f * k;
                    if (e < bestE[i])
                    {
                        bestE[i] = e;
                        best[i * 3] = w[0];
                        best[i * 3 + 1] = w[1];
                        best[i * 3 + 2] = w[2];
                    }
                }
            }
            for (int i = 0; i < n; i++)
            {
                float r = px[i * 3] - (best[i * 3] * T[0].R + best[i * 3 + 1] * T[1].R + best[i * 3 + 2] * T[2].R);
                float g = px[i * 3 + 1] - (best[i * 3] * T[0].G + best[i * 3 + 1] * T[1].G + best[i * 3 + 2] * T[2].G);
                float b = px[i * 3 + 2] - (best[i * 3] * T[0].B + best[i * 3 + 1] * T[1].B + best[i * 3 + 2] * T[2].B);
                off[i] = Len(r, g, b);
            }
            return best;
        }

        private static double[,] Invert(double[,] m)
        {
            int k = m.GetLength(0);
            var a = new double[k, 2 * k];
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    a[i, j] = m[i, j];
                }
                a[i, k + i] = 1;
            }
            for (int col = 0; col < k; col++)
            {
                int piv = col;
                for (int r = col + 1; r < k; r++)
                {
                    if (Math.Abs(a[r, col]) > Math.Abs(a[piv, col]))
                    {
                        piv = r;
                    }
                }
                if (Math.Abs(a[piv, col]) < 1e-6)
                {
                    return null;
                }
                for (int j = 0; j < 2 * k; j++)
                {
                    double tmp = a[col, j];
                    a[col, j] = a[piv, j];
                    a[piv, j] = tmp;
                }
                double p = a[col, col];
                for (int j = 0; j < 2 * k; j++)
                {
                    a[col, j] /= p;
                }
                for (int r = 0; r < k; r++)
                {
                    if (r == col)
                    {
                        continue;
                    }
                    double f = a[r, col];
                    for (int j = 0; j < 2 * k; j++)
                    {
                        a[r, j] -= f * a[col, j];
                    }
                }
            }
            var inv = new double[k, k];
            for (int i = 0; i < k; i++)
            {
                for (int j = 0; j < k; j++)
                {
                    inv[i, j] = a[i, k + j];
                }
            }
            return inv;
        }

        /// <summary>The array with the layer's mirroring applied (applying it twice gives the original).</summary>
        private static float[] Unflip(float[] a, int w, int h, int ch, Layer lay)
        {
            bool fx = lay.Tile.x < 0f, fy = lay.Tile.y < 0f;
            if (!fx && !fy)
            {
                return a;
            }
            var o = new float[a.Length];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sx = fx ? w - 1 - x : x, sy = fy ? h - 1 - y : y;
                    for (int c = 0; c < ch; c++)
                    {
                        o[(y * w + x) * ch + c] = a[(sy * w + sx) * ch + c];
                    }
                }
            }
            return o;
        }

        /// <summary>Which of a pattern's channels (r, g, b) show anywhere (weight above 0.1 in any mode).</summary>
        public bool[] PatternChannels(string tex)
        {
            var uses = new bool[3];
            foreach (var kv in masks)
            {
                if (tex == null || !kv.Key.StartsWith("pat|" + tex + "|"))
                {
                    continue;
                }
                for (int i = 0; i < kv.Value.Length; i++)
                {
                    if (kv.Value[i] > 0.1f)
                    {
                        uses[i % 3] = true;
                    }
                }
            }
            if (!uses.Any(u => u))
            {
                uses[0] = uses[1] = true;
            }
            return uses;
        }

        // ---------------------------------------------------------------- charges

        private void CutCharges()
        {
            int failed = 0;
            foreach (string line in Recipes())
            {
                string[] f = line.Split('\t');
                if (f.Length < 4 || !int.TryParse(f[1], out int cid) || !int.TryParse(f[3], out int idx)
                    || !Crests.TryGetValue(cid, out Crest c))
                {
                    failed++;
                    continue;
                }
                string tex = f[0], m = f[2];
                List<Layer> ch = c.ChargesOf(m);
                if (idx >= ch.Count || ch[idx].Tex != tex)
                {
                    failed++;
                    continue;
                }
                Layer lay = ch[idx];
                var missing = new List<string>();
                Img bg = Paint(c, m, idx, tex, missing);
                if (missing.Count > 0)
                {
                    failed++;
                    continue;
                }
                int w = bg.W, h = bg.H;
                var occ = new float[w * h];
                var twins = new List<Vector2>();
                for (int j = 0; j < ch.Count; j++)
                {
                    if (j == idx)
                    {
                        continue;
                    }
                    if (ch[j].Tex == tex)
                    {
                        twins.Add(Centre(ch[j], m));
                    }
                    else if (j > idx && Sprites.TryGetValue(ch[j].Tex, out ChargeSprite other))
                    {
                        float[] a = Coverage(other, ch[j], m, w, h);
                        for (int i = 0; i < occ.Length; i++)
                        {
                            occ[i] = Math.Max(occ[i], a[i]);
                        }
                    }
                }
                ChargeSprite sp = Cut(Tile(cid, m), bg, lay, m, occ, twins);
                if (sp == null)
                {
                    failed++;
                    continue;
                }
                sp.Source = $"{cid}/{m}";
                Sprites[tex] = sp;
            }
            if (failed > 0)
            {
                Plugin.Log.LogWarning($"Coat of arms parts: {failed} charges could not be cut (game data changed?)");
            }
        }

        private static IEnumerable<string> Recipes()
        {
            using (Stream s = typeof(Parts).Assembly.GetManifestResourceStream("URAH_Kingdoms.coa_recipes.txt"))
            {
                if (s == null)
                {
                    throw new InvalidDataException("coa_recipes.txt is not in the plugin");
                }
                using (var r = new StreamReader(s))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Length > 0 && line[0] != '#')
                        {
                            yield return line;
                        }
                    }
                }
            }
        }

        private static ChargeSprite Cut(float[] t, Img bg, Layer lay, string m, float[] occ, List<Vector2> twins)
        {
            int w = bg.W, h = bg.H, n = w * h;
            Rgb T = lay.MainTint;
            var a = new float[n];
            var s = new float[n];
            Vector2 c = Centre(lay, m);
            for (int i = 0; i < n; i++)
            {
                float br = bg.P[i * 3], bgg = bg.P[i * 3 + 1], bb = bg.P[i * 3 + 2];
                float pr = t[i * 3], pg = t[i * 3 + 1], pb = t[i * 3 + 2];
                float resid = Math.Abs(pr - br) + Math.Abs(pg - bgg) + Math.Abs(pb - bb);
                if (resid <= 20f || occ[i] >= 0.1f)
                {
                    continue;
                }
                float x = i % w + 0.5f, y = i / w + 0.5f;
                bool mine = true;
                foreach (Vector2 o in twins) // pixels nearer another instance of this charge belong to that one
                {
                    if (Vector2.Distance(new Vector2(x, y), c) > Vector2.Distance(new Vector2(x, y), o))
                    {
                        mine = false;
                        break;
                    }
                }
                if (!mine)
                {
                    continue;
                }
                // px - bg = q * tint - p * bg, least squares for p = a and q = a * shade
                float dr = pr - br, dg = pg - bgg, db = pb - bb;
                float tt = T.R * T.R + T.G * T.G + T.B * T.B, tb = br * T.R + bgg * T.G + bb * T.B, bbb = br * br + bgg * bgg + bb * bb;
                float dt = dr * T.R + dg * T.G + db * T.B, dbg = dr * br + dg * bgg + db * bb;
                float det = tt * bbb - tb * tb;
                if (Math.Abs(det) < 1e-3f)
                {
                    det = 1e-3f;
                }
                float q = (dt * bbb - dbg * tb) / det, p = (dt * tb - dbg * tt) / det;
                a[i] = Mathf.Clamp01(p);
                s[i] = a[i] > 0.02f ? Mathf.Clamp01(q / Math.Max(a[i], 1e-6f)) : 0f;
            }
            DropSpecks(a, w, h, 4);
            if (!a.Any(v => v > 0.04f))
            {
                return null;
            }
            return Unwarp(a, s, w, h, lay, m);
        }

        /// <summary>Clears 8-connected groups of fewer than min pixels with coverage > 0.04 (noise along edges).</summary>
        private static void DropSpecks(float[] a, int w, int h, int min)
        {
            var lab = new int[w * h];
            int n = 0;
            var stack = new Stack<int>();
            var comp = new List<int>();
            for (int start = 0; start < a.Length; start++)
            {
                if (a[start] <= 0.04f || lab[start] != 0)
                {
                    continue;
                }
                n++;
                comp.Clear();
                stack.Push(start);
                lab[start] = n;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    comp.Add(i);
                    int x = i % w, y = i / w;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h)
                            {
                                continue;
                            }
                            int j = yy * w + xx;
                            if (a[j] > 0.04f && lab[j] == 0)
                            {
                                lab[j] = n;
                                stack.Push(j);
                            }
                        }
                    }
                }
                if (comp.Count < min)
                {
                    foreach (int i in comp)
                    {
                        a[i] = 0f;
                    }
                }
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] <= 0.04f)
                {
                    a[i] = 0f; // as the tool: only the kept groups' pixels count
                }
            }
        }

        /// <summary>The sprite in the charge's own frame (no rotation, no mirroring) from coverage and shade in the tile.</summary>
        private static ChargeSprite Unwarp(float[] a, float[] s, int w, int h, Layer lay, string m)
        {
            Vector2 c = Centre(lay, m);
            float rmax = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] > 0.04f)
                {
                    rmax = Math.Max(rmax, Vector2.Distance(new Vector2(i % w + 0.5f, i / w + 0.5f), c));
                }
            }
            int r = Mathf.CeilToInt(rmax) + 2, size = 2 * r + 1;
            var pa = new float[size * size];
            var ps = new float[size * size];
            float ax, ay;
            if (lay.Rot == 0f) // the tile's pixel grid: no resampling blur
            {
                int x0 = Mathf.FloorToInt(c.x) - r, y0 = Mathf.FloorToInt(c.y) - r;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int tx = x0 + x, ty = y0 + y;
                        if (tx < 0 || ty < 0 || tx >= w || ty >= h)
                        {
                            continue;
                        }
                        int sx = lay.Tile.x < 0f ? size - 1 - x : x, sy = lay.Tile.y < 0f ? size - 1 - y : y;
                        pa[sy * size + sx] = a[ty * w + tx];
                        ps[sy * size + sx] = s[ty * w + tx];
                    }
                }
                ax = c.x - x0;
                ay = c.y - y0;
                if (lay.Tile.x < 0f)
                {
                    ax = size - ax;
                }
                if (lay.Tile.y < 0f)
                {
                    ay = size - ay;
                }
            }
            else
            {
                ax = ay = r + 0.5f;
                float th = lay.Rot * Mathf.Deg2Rad, cos = Mathf.Cos(th), sin = Mathf.Sin(th);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float rx = x + 0.5f - ax, ry = y + 0.5f - ay;
                        if (lay.Tile.x < 0f)
                        {
                            rx = -rx;
                        }
                        if (lay.Tile.y < 0f)
                        {
                            ry = -ry;
                        }
                        float dx = rx * cos + ry * sin, dy = -rx * sin + ry * cos; // inverse of Draw's rotation
                        pa[y * size + x] = Bilinear(a, w, h, c.x + dx - 0.5f, c.y + dy - 0.5f);
                        ps[y * size + x] = Bilinear(s, w, h, c.x + dx - 0.5f, c.y + dy - 0.5f);
                    }
                }
            }
            int bx0 = size, by0 = size, bx1 = -1, by1 = -1;
            for (int i = 0; i < pa.Length; i++)
            {
                if (pa[i] > 0.04f)
                {
                    int x = i % size, y = i / size;
                    bx0 = Math.Min(bx0, x);
                    by0 = Math.Min(by0, y);
                    bx1 = Math.Max(bx1, x);
                    by1 = Math.Max(by1, y);
                }
            }
            var sp = new ChargeSprite { W = bx1 - bx0 + 1, H = by1 - by0 + 1, Ax = ax - bx0, Ay = ay - by0 };
            sp.A = new float[sp.W * sp.H];
            sp.S = new float[sp.W * sp.H];
            for (int y = 0; y < sp.H; y++)
            {
                for (int x = 0; x < sp.W; x++)
                {
                    sp.A[y * sp.W + x] = pa[(y + by0) * size + x + bx0];
                    sp.S[y * sp.W + x] = ps[(y + by0) * size + x + bx0];
                }
            }
            sp.Kx = lay.Scale.x * Modes.Ratio(m);
            sp.Ky = lay.Scale.y * Modes.Ratio(m);
            return sp;
        }

        // ---------------------------------------------------------------- painting

        /// <summary>Where the def's position points, in pixels (rows top to bottom; the def's y goes up).</summary>
        private static Vector2 Centre(Layer lay, string m) => new Vector2(lay.Pos.x * Modes.Width(m), (1f - lay.Pos.y) * Modes.Height(m));

        private static float Bilinear(float[] img, int w, int h, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            return At(img, w, h, x0, y0) * (1 - fx) * (1 - fy) + At(img, w, h, x0 + 1, y0) * fx * (1 - fy)
                + At(img, w, h, x0, y0 + 1) * (1 - fx) * fy + At(img, w, h, x0 + 1, y0 + 1) * fx * fy;
        }

        private static float At(float[] img, int w, int h, int x, int y) => x < 0 || y < 0 || x >= w || y >= h ? 0f : img[y * w + x];

        /// <summary>Coverage and shade of a charge drawn as the layer says, per pixel of a w × h tile.</summary>
        private static void Draw(ChargeSprite sp, Layer lay, string m, int w, int h, float[] a, float[] s)
        {
            Vector2 c = Centre(lay, m);
            float kx = lay.Scale.x * Modes.Ratio(m), ky = lay.Scale.y * Modes.Ratio(m);
            float th = lay.Rot * Mathf.Deg2Rad, cos = Mathf.Cos(th), sin = Mathf.Sin(th);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float dx = x + 0.5f - c.x, dy = y + 0.5f - c.y;
                    // the def's rotation turns counter-clockwise on screen (GL, y up); undone in y-down pixels
                    float rx = dx * cos - dy * sin, ry = dx * sin + dy * cos;
                    if (lay.Tile.x < 0f)
                    {
                        rx = -rx;
                    }
                    if (lay.Tile.y < 0f)
                    {
                        ry = -ry;
                    }
                    float sx = rx * sp.Kx / kx + sp.Ax - 0.5f, sy = ry * sp.Ky / ky + sp.Ay - 0.5f;
                    a[y * w + x] = Bilinear(sp.A, sp.W, sp.H, sx, sy);
                    if (s != null)
                    {
                        s[y * w + x] = Bilinear(sp.S, sp.W, sp.H, sx, sy);
                    }
                }
            }
        }

        private static float[] Coverage(ChargeSprite sp, Layer lay, string m, int w, int h)
        {
            var a = new float[w * h];
            Draw(sp, lay, m, w, h, a, null);
            return a;
        }

        /// <summary>What a division channel shows per pixel: its tint, or its pattern in the pattern's tints.</summary>
        private float[] ChannelFill(Layer d, char ch, string m, List<string> missing)
        {
            int n = Modes.Width(m) * Modes.Height(m);
            var o = new float[n * 3];
            if (!d.Patterns.TryGetValue(ch, out Layer pat))
            {
                Rgb c = d.Tint(ch);
                for (int i = 0; i < n; i++)
                {
                    o[i * 3] = c.R;
                    o[i * 3 + 1] = c.G;
                    o[i * 3 + 2] = c.B;
                }
                return o;
            }
            Rgb[] T = { pat.Tint('r'), pat.Tint('g'), pat.Tint('b') };
            if (!masks.TryGetValue(MaskKey("pat", pat, m), out float[] wts))
            {
                missing.Add("pattern " + pat.Tex);
                wts = new float[n * 3];
                for (int i = 0; i < n; i++)
                {
                    wts[i * 3 + 1] = 1f;
                }
            }
            else
            {
                wts = Unflip(wts, Modes.Width(m), Modes.Height(m), 3, pat);
            }
            for (int i = 0; i < n; i++)
            {
                float wr = wts[i * 3], wg = wts[i * 3 + 1], wb = wts[i * 3 + 2];
                o[i * 3] = wr * T[0].R + wg * T[1].R + wb * T[2].R;
                o[i * 3 + 1] = wr * T[0].G + wg * T[1].G + wb * T[2].G;
                o[i * 3 + 2] = wr * T[0].B + wg * T[1].B + wb * T[2].B;
            }
            return o;
        }

        /// <summary>Crest c in mode m painted from the parts (only the charges before index upto, if given; none
        /// with the texture skip); what could not be painted goes to missing.</summary>
        public Img Paint(Crest c, string m, int upto = int.MaxValue, string skip = null, List<string> missing = null)
        {
            missing = missing ?? new List<string>();
            int w = Modes.Width(m), h = Modes.Height(m), n = w * h;
            var img = new Img(w, h);
            float[] P = img.P;
            if (!c.Division.TryGetValue(m, out Layer d))
            {
                missing.Add("no division");
            }
            else
            {
                float[] g = ChannelFill(d, 'g', m, missing), r = ChannelFill(d, 'r', m, missing);
                float[] M = null;
                if (d.Tex != "Solid_Color")
                {
                    if (masks.TryGetValue(MaskKey("div", d, m), out float[] dm))
                    {
                        M = Unflip(dm, w, h, 1, d);
                    }
                    else
                    {
                        missing.Add("division " + d.Tex);
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    float k = M?[i] ?? 0f;
                    for (int j = 0; j < 3; j++)
                    {
                        P[i * 3 + j] = g[i * 3 + j] * (1 - k) + r[i * 3 + j] * k;
                    }
                }
            }
            foreach (Layer o in c.OrdinariesOf(m))
            {
                if (o.Patterns.Count == 0 && o.Rot == 0f && masks.TryGetValue(MaskKey("ord", o, m), out float[] om))
                {
                    float[] M = Unflip(om, w, h, 1, o);
                    Rgb t = o.MainTint;
                    for (int i = 0; i < n; i++)
                    {
                        float k = M[i];
                        P[i * 3] = P[i * 3] * (1 - k) + t.R * k;
                        P[i * 3 + 1] = P[i * 3 + 1] * (1 - k) + t.G * k;
                        P[i * 3 + 2] = P[i * 3 + 2] * (1 - k) + t.B * k;
                    }
                }
                else
                {
                    missing.Add("ordinary " + o.Tex);
                }
            }
            List<Layer> charges = c.ChargesOf(m);
            var a = new float[n];
            var s = new float[n];
            for (int ci = 0; ci < charges.Count && ci < upto; ci++)
            {
                Layer lay = charges[ci];
                if (lay.Tex == skip)
                {
                    continue;
                }
                if (!Sprites.TryGetValue(lay.Tex, out ChargeSprite sp) || lay.Pivot != new Vector2(0.5f, 0.5f) || lay.Patterns.Count > 0)
                {
                    missing.Add("charge " + lay.Tex);
                    continue;
                }
                Draw(sp, lay, m, w, h, a, s);
                Rgb t = lay.MainTint;
                for (int i = 0; i < n; i++)
                {
                    float k = a[i], q = a[i] * s[i];
                    P[i * 3] = P[i * 3] * (1 - k) + q * t.R;
                    P[i * 3 + 1] = P[i * 3 + 1] * (1 - k) + q * t.G;
                    P[i * 3 + 2] = P[i * 3 + 2] * (1 - k) + q * t.B;
                }
            }
            for (int i = 0; i < P.Length; i++)
            {
                P[i] = Mathf.Clamp(P[i], 0f, 255f);
            }
            return img;
        }

        /// <summary>Mean colour difference of every fully paintable crest against its tile (a check of the port).</summary>
        private string Check(string m)
        {
            var errs = new List<float>();
            foreach (Crest c in Crests.Values)
            {
                var missing = new List<string>();
                Img p = Paint(c, m, missing: missing);
                if (missing.Count > 0)
                {
                    continue;
                }
                float[] t = Tile(c.Id, m);
                float e = 0f;
                for (int i = 0; i < t.Length; i++)
                {
                    e += Math.Abs(t[i] - p.P[i]);
                }
                errs.Add(e / t.Length);
            }
            errs.Sort();
            return errs.Count == 0 ? $"{m}: nothing paintable" :
                $"{m}: {errs.Count} crests, mean err {errs.Average():F1}, median {errs[errs.Count / 2]:F1}, <4: {errs.Count(e => e < 4f) * 100 / errs.Count}%";
        }

        private static float Dist(Rgb a, Rgb b) => Len(a.R - b.R, a.G - b.G, a.B - b.B);

        private static float Len(float r, float g, float b) => Mathf.Sqrt(r * r + g * g + b * b);

        private static double Dot(Rgb a, Rgb b) => (double)a.R * b.R + (double)a.G * b.G + (double)a.B * b.B;
    }
}
