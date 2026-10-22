using System;
using System.Collections.Generic;
using System.Linq;
using Logic;
using UnityEngine;
using URAH_Kingdoms.Coa;

namespace URAH_Kingdoms
{
    using CoaParts = URAH_Kingdoms.Coa.Parts;

    /// <summary>
    /// Arms made in the editor, in the game: painted into a free tile of the map's four coat of arms atlases
    /// (Assets/Maps/&lt;map&gt;/CoatOfArmsAtlas&lt;mode&gt;.png, shared by the map, the armies and the UI; ARGB32 and
    /// readable), so the kingdom's CoAIndex can point there like at any crest. Free: no crest of the map's CoA
    /// table uses the tile (on europe also none of coatOfArmsCrests.def), the highest first. The same arms
    /// keep their tile for the session; CoAIndex is set on every machine on every map load, never saved.
    /// </summary>
    internal static class CustomArms
    {
        private static string map;
        private static readonly Dictionary<string, int> tiles = new Dictionary<string, int>(); // arms text -> tile

        /// <summary>The tile of these arms on the map, painted if new; -1 when they are a vanilla crest as it is, or on failure (logged).</summary>
        public static int Place(string mapName, string coa)
        {
            if (string.IsNullOrEmpty(mapName) || string.IsNullOrEmpty(coa))
            {
                return -1;
            }
            try
            {
                CoaParts parts = CoaParts.Get();
                if (parts == null)
                {
                    return -1;
                }
                CoaSpec spec = CoaSpec.Parse(coa, parts.Palette, out string error);
                if (spec == null)
                {
                    Plugin.Log.LogWarning($"Arms '{coa}': {error}");
                    return -1;
                }
                if (!spec.Edited)
                {
                    return -1;
                }
                if (map != mapName)
                {
                    map = mapName;
                    tiles.Clear();
                }
                if (tiles.TryGetValue(coa, out int known))
                {
                    return known;
                }
                Crest crest = spec.ToCrest(parts);
                var atlases = new Dictionary<string, Texture2D>();
                int capacity = int.MaxValue;
                foreach (string m in Modes.All)
                {
                    Texture2D atlas = Assets.Get<Texture2D>($"Assets/Maps/{mapName}/CoatOfArmsAtlas{m}.png");
                    if (atlas == null || !atlas.isReadable)
                    {
                        Plugin.Log.LogWarning($"Arms: the {m} atlas of {mapName} is {(atlas == null ? "missing" : "not readable")}");
                        return -1;
                    }
                    atlases[m] = atlas;
                    capacity = Math.Min(capacity, (atlas.width / Modes.Width(m)) * (atlas.height / Modes.Height(m)));
                }
                HashSet<int> used = Used(mapName, parts);
                int tile = capacity - 1;
                while (tile > 0 && (used.Contains(tile) || tiles.ContainsValue(tile)))
                {
                    tile--;
                }
                if (tile <= 0)
                {
                    Plugin.Log.LogWarning($"Arms: no free tile in the atlases of {mapName}");
                    return -1;
                }
                foreach (string m in Modes.All)
                {
                    Write(atlases[m], tile, m, parts.Paint(crest, m));
                }
                tiles[coa] = tile;
                Plugin.Log.LogInfo($"Arms painted into tile {tile} of {mapName}: {coa}");
                return tile;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Arms '{coa}': {e}");
                return -1;
            }
        }

        /// <summary>The tiles the map's crests use: its CoA table, and on europe every crest of the def.</summary>
        private static HashSet<int> Used(string mapName, CoaParts parts)
        {
            var used = new HashSet<int>();
            CoAMapping mapping = CoAMapping.GetMapping(mapName);
            if (mapping?.mapping != null)
            {
                used.UnionWith(mapping.mapping.Values);
            }
            if (mapName == "europe")
            {
                used.UnionWith(parts.Crests.Keys);
            }
            return used;
        }

        /// <summary>The image into the tile (tiles row-major, row 0 at the texture's bottom; the image's rows top to bottom), mipmaps anew.</summary>
        private static void Write(Texture2D atlas, int tile, string m, Img img)
        {
            int w = Modes.Width(m), h = Modes.Height(m), cols = atlas.width / w;
            int x0 = tile % cols * w, y0 = tile / cols * h;
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = (y * w + x) * 3;
                    px[(h - 1 - y) * w + x] = new Rgb(img.P[i], img.P[i + 1], img.P[i + 2]).ToColor32();
                }
            }
            atlas.SetPixels32(x0, y0, w, h, px);
            atlas.Apply(updateMipmaps: true);
        }
    }
}
