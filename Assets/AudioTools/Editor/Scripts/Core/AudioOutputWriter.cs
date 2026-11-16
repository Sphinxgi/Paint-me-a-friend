using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AudioTool
{
    /// <summary>A written file and the project asset whose import settings it should inherit.</summary>
    internal readonly struct AudioImportRequest
    {
        internal AudioImportRequest(string path, string sourceProjectPath)
        {
            Path = path;
            SourceProjectPath = sourceProjectPath;
        }

        internal string Path { get; }
        internal string SourceProjectPath { get; }
    }

    /// <summary>Writes rendered clips to disk and imports them, shared by single-clip saves and batch saves.</summary>
    internal static class AudioOutputWriter
    {
        /// <summary>Writes every output of one source.</summary>
        /// <param name="outputs">Rendered clips in timeline order.</param>
        /// <param name="folder">Folder for numbered copies when no explicit path is given.</param>
        /// <param name="sourceFileName">Source name that numbered copies derive from.</param>
        /// <param name="explicitPath">Chosen file; with several outputs it becomes the base of numbered names.</param>
        /// <param name="format">Sample encoding.</param>
        internal static List<string> Write(IReadOnlyList<AudioOutputClip> outputs, string folder, string sourceFileName, string explicitPath, WavSampleFormat format)
        {
            if (outputs == null || outputs.Count == 0) throw new InvalidOperationException("The selected region contains no audio samples.");
            List<string> written = new List<string>(outputs.Count);
            string baseFolder = string.IsNullOrEmpty(explicitPath) ? folder : Path.GetDirectoryName(explicitPath);
            string baseName = string.IsNullOrEmpty(explicitPath) ? sourceFileName ?? "audio.wav" : Path.GetFileName(explicitPath);
            if (string.IsNullOrEmpty(baseFolder)) throw new ArgumentException("Choose an output path first.", nameof(explicitPath));
            foreach (AudioOutputClip output in outputs)
            {
                string path = !string.IsNullOrEmpty(explicitPath) && outputs.Count == 1 ? explicitPath : AudioProcessor.GenerateUniqueFilename(baseFolder, baseName);
                if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, ".wav");
                AudioWavWriter.Write(path, output.Samples, output.Channels, output.Frequency, format);
                written.Add(path);
            }

            return written;
        }

        /// <summary>Imports written files under Assets in one pass, then gives new copies the import settings of their source asset.</summary>
        internal static void Import(IReadOnlyList<AudioImportRequest> requests, bool copyImportSettings)
        {
            List<(string asset, string source)> assets = new List<(string asset, string source)>();
            foreach (AudioImportRequest request in requests)
            {
                string asset = ToAssetPath(request.Path);
                if (asset != null) assets.Add((asset, request.SourceProjectPath));
            }

            if (assets.Count == 0) return;
            // A file in a folder the AssetDatabase has not seen yet needs a refresh to bring in the folder too.
            if (assets.Exists(item => !AssetDatabase.IsValidFolder(Path.GetDirectoryName(item.asset)?.Replace(Path.DirectorySeparatorChar, '/'))))
            {
                AssetDatabase.Refresh();
            }
            else
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach ((string asset, string _) in assets) AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
            }

            if (!copyImportSettings) return;
            List<(string asset, string source)> copies = assets.FindAll(item => !string.IsNullOrEmpty(item.source) && !string.Equals(item.source, item.asset, StringComparison.OrdinalIgnoreCase));
            if (copies.Count == 0) return;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach ((string asset, string source) in copies) CopyImportSettings(source, asset);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        /// <summary>Copies the AudioImporter settings of <paramref name="sourceAssetPath"/> onto <paramref name="targetAssetPath"/>, keeping the target's bundle and user data.</summary>
        internal static bool CopyImportSettings(string sourceAssetPath, string targetAssetPath)
        {
            AudioImporter source = AssetImporter.GetAtPath(sourceAssetPath) as AudioImporter;
            AudioImporter target = AssetImporter.GetAtPath(targetAssetPath) as AudioImporter;
            if (source == null || target == null) return false;

            string bundleName = target.assetBundleName;
            string bundleVariant = target.assetBundleVariant;
            string userData = target.userData;
            Preset preset = new Preset(source);
            try
            {
                if (!preset.ApplyTo(target)) return false;
            }
            finally
            {
                Object.DestroyImmediate(preset);
            }

            target.SetAssetBundleNameAndVariant(bundleName, bundleVariant);
            target.userData = userData;
            target.SaveAndReimport();
            return true;
        }

        /// <summary>Project-relative path for a file under Assets, or null outside the project.</summary>
        internal static string ToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            string normalized = Path.GetFullPath(fullPath).Replace('\\', '/');
            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            if (!normalized.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase)) return null;
            return "Assets" + normalized.Substring(dataPath.Length);
        }

        internal static bool IsInsideProjectAssets(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string fullFolder = Path.GetFullPath(folder).Replace('\\', '/').TrimEnd('/');
            string dataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return fullFolder.Equals(dataPath, StringComparison.OrdinalIgnoreCase) || fullFolder.StartsWith(dataPath + "/", StringComparison.OrdinalIgnoreCase);
        }
    }
}
