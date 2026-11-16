using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ImpossibleRobert.Common.Editor.Packages
{
    /// <summary>
    /// Rebinds TextMesh Pro materials in first-party packages whose saved shader reference does not resolve. TMP Essential
    /// Resources, earlier TextMesh Pro releases and uGUI 7, which ships the TMP shaders inside its package, store each TMP
    /// shader under a different GUID, so a shipped material resolves in only some Unity versions. The GUID the material
    /// stores names the TMP shader, which is then found by its stable shader name.
    /// </summary>
    /// <remarks>
    /// Scans the folders of <c>com.wetzold.*</c> package manifests once per editor session and then the first-party assets
    /// Unity imports. Repairs are saved only where <see cref="PackageAssetOwnership.IsProjectOwned"/> allows; other repairs
    /// stay in memory and are applied again after domain reloads and scene loads, which can unload the assets.
    /// </remarks>
    [InitializeOnLoad]
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class TmpShaderReferenceRepair
    {
        const string PackagePrefix = "com.wetzold.";
        const string ErrorShaderName = "Hidden/InternalErrorShader";
        const string ProbeShaderName = "TextMeshPro/Mobile/Distance Field";
        const string ScannedKey = "ImpossibleRobert.Common.TmpShaderReferenceRepair.Scanned";
        const string UnsavedKey = "ImpossibleRobert.Common.TmpShaderReferenceRepair.Unsaved";

        // Shader name, then its GUIDs in earlier TextMesh Pro releases, TMP Essential Resources and uGUI 7, as listed in
        // uGUI 7's PackageConversionData.json. The Surface shaders have no uGUI 7 counterpart.
        static readonly string[][] s_shaderGuids =
        {
            new[] { "TextMeshPro/Bitmap", "edfcf888cd11d9245b91d2883049a57e", "128e987d567d4e2c824d754223b3f3b0", "95ccbecb7cf14cc2bbd06888665ae1cc" },
            new[] { "TextMeshPro/Bitmap Custom Atlas", "48bb5f55d8670e349b6e614913f9d910", "9b353cff71f94293aa3a1b84ec7726a8" },
            new[] { "TextMeshPro/Mobile/Bitmap", "d1cf17907700cb647aa3ea423ba38f2e", "1e3b057af24249748ff873be7fafee47", "8e65cc65e85c4c1f80405a3bcc6b004b" },
            new[] { "TextMeshPro/Distance Field", "dca26082f9cb439469295791d9f76fe5", "68e6db2ebdc24f95958faec2be5558d6", "abcf10cc92e843dd8530c741953ac8fd" },
            new[] { "TextMeshPro/Distance Field Overlay", "4a7755d6b5b67874f89c85f56f95fe97", "dd89cf5b9246416f84610a006f916af7", "90037dd8d748477399c1be07e85eb288" },
            new[] { "TextMeshPro/Distance Field SSD", "14eb328de4b8eb245bb7cea29e4ac00b", "ab4ae7a015a441e79ffd42ca1c8ecf10" },
            new[] { "TextMeshPro/Distance Field - SpaceWarp", "b5ab6cb89ffa7004ca59c01dddd06493", "3cede5ba99c240c288e2fc02b961c7d4" },
            new[] { "TextMeshPro/Distance Field SSD - SpaceWarp", "5c6f31a5a80ebc04f8bcfdeed8327fe1", "0557482ffe6241d584b7ad91cfd7ad49" },
            new[] { "TextMeshPro/Distance Field (Surface)", "f7ada0af4f174f0694ca6a487b8f543d" },
            new[] { "TextMeshPro/Mobile/Distance Field", "cafd18099dfc0114896e0a8b277b81b6", "fe393ace9b354375a9cb14cdbbc28be4", "f99637934d35413090e376899561717a" },
            new[] { "TextMeshPro/Mobile/Distance Field - Masking", "afc255f7c2be52e41973a3d10a2e632d", "bc1ede39bf3643ee8e493720e4259791", "7d48a8a963564c8babfa426684695bb8" },
            new[] { "TextMeshPro/Mobile/Distance Field Overlay", "9ecb3fe313cb5f7478141eba4a2d54ed", "a02a7d8c237544f1962732b55a9aebf1", "2dbab6ddeacf4399a9d621920215cec0" },
            new[] { "TextMeshPro/Mobile/Distance Field SSD", "c8d12adcee749c344b8117cf7c7eb912", "37f0765cde2f40f8bec87d141295c99c" },
            new[] { "TextMeshPro/Mobile/Distance Field - SpaceWarp", "2ae3f84ba70f31a448850bf95ca45125", "082483ce6e2c49e58894bcef425c54ea" },
            new[] { "TextMeshPro/Mobile/Distance Field SSD - SpaceWarp", "2f52e8cb070aaf94289b130df84e2882", "3dbce2c8fcef47d6b790c168260e4ca4" },
            new[] { "TextMeshPro/Mobile/Distance Field - 2 Pass", "0178fcb869bafef4690d177d31d17db8", "92552ea265b64b9d80e770ea1d0dbd5b" },
            new[] { "TextMeshPro/Mobile/Distance Field (Surface)", "85187c2149c549c5b33f0cdb02836b17" },
            new[] { "TextMeshPro/Sprite", "3a1c68c8292caf046bd21158886c5e40", "cf81c85f95fe47e1a27f6ae460cf182c", "c13dbb07723c428a869261a1220bd4a2" },
            new[] { "TextMeshPro/SRP/TMP_SDF-URP Lit", "a3d800b099a06e0478fb790c5e79057a", "e29e7355e8f14ae2a18b9218039b72e2" },
            new[] { "TextMeshPro/SRP/TMP_SDF-URP Unlit", "124c112a6e8f1a54e8b0870e881b56d8", "3233e7968f094bc2b7cd2f93841bac91" },
            new[] { "TextMeshPro/SRP/TMP_SDF-HDRP LIT", "ca2ed216f98028c4dae6c5224a952b3c", "9b2782d4f28245a28a30830493625044" },
            new[] { "TextMeshPro/SRP/TMP_SDF-HDRP UNLIT", "f63d574838ccfb44f84acc05fed0af48", "f22099489d7f4a14a13a8e2fdb12691c" }
        };

        // A FindAssets filter naming several of these types matched only the materials, so each type is queried on its own.
        static readonly string[] s_candidateFilters = { "t:Material", "t:TMP_FontAsset", "t:TMP_SpriteAsset" };
        static readonly Dictionary<string, string> s_shaderNamesByGuid = CreateShaderNamesByGuid();
        static readonly HashSet<string> s_shaderNames = new HashSet<string>(s_shaderNamesByGuid.Values, StringComparer.Ordinal);
        static readonly List<string> s_includedFolders = new List<string>();
        static readonly HashSet<string> s_pendingFolders = new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> s_pendingPaths = new HashSet<string>(StringComparer.Ordinal);
        static List<string> s_roots;
        static bool s_queued;

        /// <summary>Raised for each repaired material so editors that know TextMesh Pro can rebuild text rendered with it.</summary>
        public static event Action<Material> MaterialRepaired;

        static TmpShaderReferenceRepair()
        {
            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            AddPendingPaths(LoadUnsavedPaths());
            Queue();
        }

        /// <summary>
        /// Includes a first-party folder without a <c>com.wetzold.*</c> package manifest, such as an optional integration.
        /// </summary>
        public static void IncludeFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder)) return;

            assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (!s_includedFolders.Contains(assetFolder))
            {
                s_includedFolders.Add(assetFolder);
                s_roots = null;
            }

            s_pendingFolders.Add(assetFolder);
            Queue();
        }

        /// <summary>
        /// Assigns the TextMesh Pro shader that the material's saved, unresolved shader reference names. Returns false when
        /// the shader resolves, the reference is not a TMP shader, or that shader is not available in this project.
        /// </summary>
        public static bool RepairMaterial(Material material)
        {
            if (material == null || !IsShaderMissing(material)) return false;
            if (!TryGetSavedShaderName(material, out string shaderName)) return false;

            Shader shader = Shader.Find(shaderName);
            if (shader == null) return false;

            // The material keeps its saved properties and keywords while the shader is missing, so it renders as authored.
            material.shader = shader;
            return true;
        }

        internal static bool TryGetSavedShaderName(Material material, out string shaderName)
        {
            shaderName = null;
            using (SerializedObject serializedMaterial = new SerializedObject(material))
            {
                SerializedProperty shaderProperty = serializedMaterial.FindProperty("m_Shader");
                return shaderProperty != null &&
                       UnityEditorCompat.TryGetReferencedAssetGuid(shaderProperty, out string guid) &&
                       s_shaderNamesByGuid.TryGetValue(guid, out shaderName);
            }
        }

        internal static void HandleAssetChanges(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
        {
            if (ContainsManifest(importedAssets) || ContainsManifest(deletedAssets) || ContainsManifest(movedAssets))
                s_roots = null;

            bool added = AddPendingPaths(importedAssets);
            added |= AddPendingPaths(movedAssets);
            if (added) Queue();
        }

        static void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (AddPendingPaths(LoadUnsavedPaths())) Queue();
        }

        static void Queue()
        {
            if (s_queued) return;

            s_queued = true;
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            EditorApplication.delayCall -= Run;
            s_queued = false;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Queue();
                return;
            }

            RepairPending();
        }

        /// <summary>Runs the queued scan and repairs now; returns the number of repaired assets.</summary>
        internal static int RepairPending()
        {
            // Without TMP shaders in the project there is nothing to bind to; TextMesh Pro asks for its resources itself.
            if (Shader.Find(ProbeShaderName) == null) return 0;

            List<string> roots = Roots();
            if (!SessionState.GetBool(ScannedKey, false))
            {
                SessionState.SetBool(ScannedKey, true);
                AddCandidates(roots.ToArray());
            }

            foreach (string folder in s_pendingFolders)
                AddCandidates(new[] { folder });
            s_pendingFolders.Clear();

            string[] paths = new string[s_pendingPaths.Count];
            s_pendingPaths.CopyTo(paths);
            s_pendingPaths.Clear();

            int repaired = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                if (IsUnderRoot(paths[i], roots) && RepairAsset(paths[i]))
                    repaired++;
            }

            if (repaired > 0)
                InternalEditorUtility.RepaintAllViews();
            return repaired;
        }

        static bool RepairAsset(string path)
        {
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                return false;

            // Resolved shader references are answered from the asset database, so their large font assets are not loaded.
            if (HasShaderDependency(path))
            {
                RemoveUnsavedPath(path);
                return false;
            }

            bool projectOwned = PackageAssetOwnership.IsProjectOwned(path);
            bool repaired = false;
            bool usesTmpShader = false;
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            for (int i = 0; i < assets.Length; i++)
            {
                Material material = assets[i] as Material;
                if (material == null) continue;

                if (RepairMaterial(material))
                {
                    if (projectOwned)
                    {
                        EditorUtility.SetDirty(material);
                    }
                    else
                    {
                        // Keep project-wide saves from writing this editor's shader GUID into a shared or read-only package.
                        EditorUtility.ClearDirty(material);
                    }

                    repaired = true;
                    MaterialRepaired?.Invoke(material);
                }

                usesTmpShader |= material.shader != null && s_shaderNames.Contains(material.shader.name);
            }

            // The saved reference does not resolve, so a binding made in memory, here or by another repair, must be kept.
            if (!usesTmpShader) return repaired;

            if (projectOwned)
            {
                Object mainAsset = AssetDatabase.LoadMainAssetAtPath(path);
                EditorUtility.SetDirty(mainAsset);
                AssetDatabase.SaveAssetIfDirty(mainAsset);
            }
            else
            {
                // Nothing is saved, so the binding is needed again after Unity unloads the asset.
                AddUnsavedPath(path);
            }

            return repaired;
        }

        static bool IsShaderMissing(Material material)
        {
            Shader shader = material.shader;
            return shader == null || string.Equals(shader.name, ErrorShaderName, StringComparison.Ordinal);
        }

        static bool HasShaderDependency(string path)
        {
            string[] dependencies = AssetDatabase.GetDependencies(path, false);
            for (int i = 0; i < dependencies.Length; i++)
            {
                if (dependencies[i].EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                    dependencies[i].EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static void AddCandidates(string[] folders)
        {
            // An included folder can be deleted later in the session.
            folders = Array.FindAll(folders, AssetDatabase.IsValidFolder);
            if (folders.Length == 0) return;

            for (int i = 0; i < s_candidateFilters.Length; i++)
            {
                foreach (string guid in AssetDatabase.FindAssets(s_candidateFilters[i], folders))
                    s_pendingPaths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        static bool AddPendingPaths(IEnumerable<string> paths)
        {
            if (paths == null) return false;

            bool added = false;
            foreach (string path in paths)
            {
                if (!string.IsNullOrEmpty(path))
                    added |= s_pendingPaths.Add(path);
            }

            return added;
        }

        static List<string> Roots()
        {
            if (s_roots != null) return s_roots;

            s_roots = new List<string>(s_includedFolders);

            foreach (string guid in AssetDatabase.FindAssets("package t:TextAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/package.json", StringComparison.OrdinalIgnoreCase)) continue;

                TextAsset manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                if (manifest == null || !IsFirstPartyManifest(manifest.text)) continue;

                s_roots.Add(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            }

            return s_roots;
        }

        static bool IsFirstPartyManifest(string json)
        {
            try
            {
                PackageManifest manifest = JsonUtility.FromJson<PackageManifest>(json);
                return manifest?.name != null && manifest.name.StartsWith(PackagePrefix, StringComparison.Ordinal);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        static bool IsUnderRoot(string path, List<string> roots)
        {
            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (path.Length > root.Length && path[root.Length] == '/' &&
                    path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool ContainsManifest(string[] paths)
        {
            if (paths == null) return false;

            for (int i = 0; i < paths.Length; i++)
            {
                if (paths[i].EndsWith("/package.json", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static List<string> LoadUnsavedPaths()
        {
            string value = SessionState.GetString(UnsavedKey, string.Empty);
            return value.Length == 0 ? new List<string>() : new List<string>(value.Split('\n'));
        }

        static void AddUnsavedPath(string path)
        {
            List<string> paths = LoadUnsavedPaths();
            if (paths.Contains(path)) return;

            paths.Add(path);
            SessionState.SetString(UnsavedKey, string.Join("\n", paths));
        }

        static void RemoveUnsavedPath(string path)
        {
            List<string> paths = LoadUnsavedPaths();
            if (paths.Remove(path))
                SessionState.SetString(UnsavedKey, string.Join("\n", paths));
        }

        static Dictionary<string, string> CreateShaderNamesByGuid()
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < s_shaderGuids.Length; i++)
            {
                for (int j = 1; j < s_shaderGuids[i].Length; j++)
                    names.Add(s_shaderGuids[i][j], s_shaderGuids[i][0]);
            }

            return names;
        }

        [Serializable]
        sealed class PackageManifest
        {
            public string name;
        }
    }

    sealed class TmpShaderReferenceRepairPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            TmpShaderReferenceRepair.HandleAssetChanges(importedAssets, deletedAssets, movedAssets);
        }
    }
}
