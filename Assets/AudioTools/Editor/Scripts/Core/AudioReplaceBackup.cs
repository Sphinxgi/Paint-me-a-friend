using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace AudioTool
{
    /// <summary>Keeps copies of files before Audio Tools overwrites them, so the last replacement can be restored.</summary>
    /// <remarks>Backups live in the project's Library folder, which Unity does not import and version control usually ignores.</remarks>
#if UNITY_6000_7_OR_NEWER
    // The root override is an editor-session test hook, not play state.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class AudioReplaceBackup
    {
        private const string ManifestName = "manifest.json";
        private const int KeptSets = 5;

        /// <summary>Redirects backups, for tests.</summary>
        internal static string RootOverride { get; set; }

        internal static string Root => RootOverride ?? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, "Library", "AudioTools", "Backups");

        [Serializable]
        private sealed class Manifest
        {
            public List<string> Originals = new List<string>();
            public List<string> Copies = new List<string>();
        }

        /// <summary>Copies the files about to be replaced into a new backup set.</summary>
        /// <returns>The backup set folder.</returns>
        internal static string Create(IReadOnlyList<string> originalPaths)
        {
            string folder = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);
            Manifest manifest = new Manifest();
            for (int i = 0; i < originalPaths.Count; i++)
            {
                string original = Path.GetFullPath(originalPaths[i]);
                string copyName = i.ToString("D4", CultureInfo.InvariantCulture) + "_" + Path.GetFileName(original);
                File.Copy(original, Path.Combine(folder, copyName), true);
                manifest.Originals.Add(original);
                manifest.Copies.Add(copyName);
            }

            File.WriteAllText(Path.Combine(folder, ManifestName), JsonUtility.ToJson(manifest, true));
            Prune();
            return folder;
        }

        /// <summary>Number of files in the newest backup set, or zero.</summary>
        internal static int LatestCount()
        {
            Manifest manifest = ReadManifest(FindLatest());
            return manifest?.Originals.Count ?? 0;
        }

        /// <summary>Copies the newest backup set back over its originals and removes the set.</summary>
        /// <returns>The restored file paths.</returns>
        internal static List<string> RestoreLatest()
        {
            string folder = FindLatest();
            Manifest manifest = ReadManifest(folder);
            List<string> restored = new List<string>();
            if (manifest == null) return restored;
            for (int i = 0; i < manifest.Originals.Count && i < manifest.Copies.Count; i++)
            {
                string copy = Path.Combine(folder, manifest.Copies[i]);
                if (!File.Exists(copy)) continue;
                string original = manifest.Originals[i];
                string directory = Path.GetDirectoryName(original);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(copy, original, true);
                restored.Add(original);
            }

            Directory.Delete(folder, true);
            return restored;
        }

        private static string FindLatest()
        {
            if (!Directory.Exists(Root)) return null;
            string[] sets = Directory.GetDirectories(Root);
            Array.Sort(sets, StringComparer.Ordinal);
            for (int i = sets.Length - 1; i >= 0; i--)
            {
                if (File.Exists(Path.Combine(sets[i], ManifestName))) return sets[i];
            }

            return null;
        }

        private static Manifest ReadManifest(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try
            {
                return JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(folder, ManifestName)));
            }
            catch (IOException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static void Prune()
        {
            string[] sets = Directory.GetDirectories(Root);
            if (sets.Length <= KeptSets) return;
            Array.Sort(sets, StringComparer.Ordinal);
            for (int i = 0; i < sets.Length - KeptSets; i++)
            {
                try
                {
                    Directory.Delete(sets[i], true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
