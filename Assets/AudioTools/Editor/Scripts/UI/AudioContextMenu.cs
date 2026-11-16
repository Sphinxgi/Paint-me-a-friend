using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioTool
{
    /// <summary>
    /// Context menu integration for editing audio files directly from the Project window.
    /// </summary>
    internal static class AudioContextMenu
    {
        private static readonly string[] SupportedExtensions = {".wav", ".mp3", ".ogg", ".aiff", ".aif", ".flac"};

        [MenuItem("Assets/Edit Audio", false, 200)]
        private static void EditAudio()
        {
            List<string> paths = CollectFromSelection(Selection.objects);
            if (paths.Count == 0)
            {
                EditorUtility.DisplayDialog("Edit Audio", "The selected folders contain no WAV, MP3, OGG, AIFF or FLAC files.", "OK");
                return;
            }

            AudioEditorUI window = AudioEditorUI.ShowWindow();
            if (paths.Count == 1) window.Init(new FileAudioSource(paths[0]), Path.GetDirectoryName(paths[0]));
            else window.InitMany(paths);
        }

        [MenuItem("Assets/Edit Audio", true)]
        private static bool ValidateEditAudio()
        {
            Object[] selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0) return false;

            foreach (Object obj in selectedObjects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) continue;
                if (IsAudioFile(assetPath) || AssetDatabase.IsValidFolder(assetPath)) return true;
            }

            return false;
        }

        /// <summary>Full paths of the selected audio files plus the audio files inside selected folders, without duplicates.</summary>
        internal static List<string> CollectFromSelection(Object[] objects)
        {
            List<string> paths = new List<string>();
            if (objects == null) return paths;
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (Object obj in objects)
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(assetPath)) continue;
                paths.Add(Path.GetFullPath(Path.Combine(projectRoot, assetPath)));
            }

            return Collect(paths);
        }

        /// <summary>Expands folders recursively and keeps supported audio files, sorted by folder then name.</summary>
        internal static List<string> Collect(IEnumerable<string> fullPaths)
        {
            List<string> files = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in fullPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                if (Directory.Exists(path))
                {
                    List<string> found = new List<string>();
                    foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        if (IsAudioFile(file)) found.Add(Path.GetFullPath(file));
                    }

                    found.Sort(ComparePaths);
                    foreach (string file in found)
                    {
                        if (seen.Add(file)) files.Add(file);
                    }
                }
                else if (IsAudioFile(path) && File.Exists(path))
                {
                    string full = Path.GetFullPath(path);
                    if (seen.Add(full)) files.Add(full);
                }
            }

            return files;
        }

        internal static bool IsAudioFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string extension = Path.GetExtension(path).ToLowerInvariant();
            foreach (string ext in SupportedExtensions)
            {
                if (extension == ext) return true;
            }

            return false;
        }

        private static int ComparePaths(string left, string right)
        {
            int folder = string.Compare(Path.GetDirectoryName(left), Path.GetDirectoryName(right), StringComparison.OrdinalIgnoreCase);
            return folder != 0 ? folder : string.Compare(Path.GetFileName(left), Path.GetFileName(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
