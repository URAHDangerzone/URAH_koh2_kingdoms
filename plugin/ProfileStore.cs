using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Logging;

namespace URAH_Kingdoms
{
    /// <summary>
    /// The player's profiles: one JSON file per profile in BepInEx/config/urah.kingdoms/, the file name
    /// (without .json) is its id. Outside the mod folder, so editing a profile never changes the mod
    /// checksum. Invalid files are logged and skipped, never fixed on disk.
    /// </summary>
    internal sealed class ProfileStore
    {
        public const int MaxId = 32;

        private readonly string folder;
        private readonly ManualLogSource log;

        public ProfileStore(string folder, ManualLogSource log)
        {
            this.folder = folder;
            this.log = log;
        }

        public string Folder => folder;

        /// <summary>Ids of all profile files, sorted; valid or not.</summary>
        public List<string> Ids()
        {
            if (!Directory.Exists(folder))
            {
                return new List<string>();
            }
            return Directory.GetFiles(folder, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(IsId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The valid profile with this id, or null (missing or invalid, logged).</summary>
        public Profile Load(string id)
        {
            if (!IsId(id))
            {
                log.LogWarning($"Profile id '{id}' is not valid (a-z, 0-9, _ and -, up to {MaxId})");
                return null;
            }
            string path = PathOf(id);
            if (!File.Exists(path))
            {
                log.LogWarning($"Profile '{id}' not found: {path}");
                return null;
            }
            Profile profile;
            try
            {
                profile = Profile.FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e)
            {
                log.LogWarning($"Profile '{id}' can't be read: {e.Message}");
                return null;
            }
            List<string> errors = profile.Normalize();
            if (errors.Count > 0)
            {
                log.LogWarning($"Profile '{id}' is not valid: {string.Join("; ", errors)}");
                return null;
            }
            return profile;
        }

        /// <summary>Writes a valid profile; false (logged) when it is not valid.</summary>
        public bool Save(string id, Profile profile)
        {
            List<string> errors = profile.Normalize();
            if (!IsId(id) || errors.Count > 0)
            {
                log.LogWarning($"Profile '{id}' not saved: {(IsId(id) ? string.Join("; ", errors) : "invalid id")}");
                return false;
            }
            Directory.CreateDirectory(folder);
            string path = PathOf(id);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, profile.ToJson(indented: true) + "\n", new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(tmp, path, null);
            }
            else
            {
                File.Move(tmp, path);
            }
            return true;
        }

        /// <summary>Saves the profile of a kingdom code under a new id made from its name; the id, or null with an error.</summary>
        public string Import(string code, out string error)
        {
            Profile profile = Profile.FromCode(code, out error);
            if (profile == null)
            {
                return null;
            }
            string id = FreeId(IdFromName(profile.Name));
            return Save(id, profile) ? id : null;
        }

        public static bool IsId(string id)
        {
            return !string.IsNullOrEmpty(id) && id.Length <= MaxId &&
                id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-');
        }

        /// <summary>A file name for a kingdom name: ASCII letters and digits kept (lower case), the rest one '_'.</summary>
        public static string IdFromName(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in (name ?? "").ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_')
                {
                    sb.Append('_');
                }
            }
            string id = sb.ToString().Trim('_');
            if (id.Length > MaxId - 3)
            {
                id = id.Substring(0, MaxId - 3).TrimEnd('_');
            }
            return id.Length > 0 ? id : "kingdom";
        }

        private string FreeId(string id)
        {
            if (!File.Exists(PathOf(id)))
            {
                return id;
            }
            for (int i = 2; ; i++)
            {
                string next = $"{id}_{i}";
                if (!File.Exists(PathOf(next)))
                {
                    return next;
                }
            }
        }

        private string PathOf(string id) => Path.Combine(folder, id + ".json");
    }
}
