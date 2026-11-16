using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ImpossibleRobert.Common.Editor.Packages
{
    /// <summary>
    /// Keeps glyphs that laying out text adds to a package's dynamic TextMesh Pro fonts in memory instead of saving them
    /// into the shipped font files, which would then show as modified in version control. Text keeps using the glyphs in
    /// memory. Tools that curate a shipped font's glyph set save it with <see cref="SaveWithAddedGlyphs"/>.
    /// </summary>
    /// <remarks>
    /// Common does not reference TextMesh Pro, so the font tables are read through their serialized fields.
    /// </remarks>
#if UNITY_6000_7_OR_NEWER
    // Curated saves in progress are editor-session state.
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class ShippedFontGlyphs
    {
        static readonly HashSet<string> s_curatedSaves = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Returns <paramref name="paths"/> without the fonts listed in <paramref name="fontGuids"/> that hold glyphs their
        /// saved file lacks; those fonts, their atlas textures and their material keep their changes in memory.
        /// Call it from <c>AssetModificationProcessor.OnWillSaveAssets</c>.
        /// </summary>
        public static string[] KeepAddedGlyphsInMemory(string[] paths, ICollection<string> fontGuids)
        {
            List<string> saved = null;
            for (int i = 0; i < paths.Length; i++)
            {
                if (fontGuids.Contains(AssetDatabase.AssetPathToGUID(paths[i])) && HoldsAddedGlyphs(paths[i]))
                {
                    if (saved == null)
                    {
                        saved = new List<string>(paths.Length);
                        for (int j = 0; j < i; j++) saved.Add(paths[j]);
                    }

                    foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(paths[i]))
                    {
                        if (asset != null) EditorUtility.ClearDirty(asset);
                    }

                    continue;
                }

                saved?.Add(paths[i]);
            }

            return saved != null ? saved.ToArray() : paths;
        }

        /// <summary>
        /// True when the dynamic TextMesh Pro font at <paramref name="path"/> holds more characters, glyphs, atlas textures or font feature records, or larger atlas textures, than its saved file. TextMesh Pro's Clear Dynamic Data, which also runs before builds, keeps the feature records, so a font text used stays held back until the editor reloads it. Static fonts and binary-serialized files are never held back.
        /// </summary>
        public static bool HoldsAddedGlyphs(string path)
        {
            if (string.IsNullOrEmpty(path) || s_curatedSaves.Contains(path)) return false;

            Object font = AssetDatabase.LoadMainAssetAtPath(path);
            if (font == null) return false;

            SerializedObject serialized = new SerializedObject(font);
            SerializedProperty mode = serialized.FindProperty("m_AtlasPopulationMode");
            SerializedProperty characters = serialized.FindProperty("m_CharacterTable");
            SerializedProperty glyphs = serialized.FindProperty("m_GlyphTable");
            SerializedProperty atlases = serialized.FindProperty("m_AtlasTextures");
            // TextMesh Pro's AtlasPopulationMode.Static is 0.
            if (mode == null || mode.intValue == 0 || characters == null || glyphs == null || atlases == null) return false;

            return TryReadSavedTables(path, out int savedCharacters, out int savedGlyphs, out int savedAtlases, out int savedFeatures, out List<Vector2Int> savedAtlasSizes) &&
                   (characters.arraySize > savedCharacters || glyphs.arraySize > savedGlyphs || atlases.arraySize > savedAtlases ||
                    CountFeatureRecords(serialized) > savedFeatures || AtlasTexturesHaveGrown(atlases, savedAtlasSizes));
        }

        static bool AtlasTexturesHaveGrown(SerializedProperty atlases, List<Vector2Int> savedAtlasSizes)
        {
            for (int i = 0; i < atlases.arraySize && i < savedAtlasSizes.Count; i++)
            {
                Texture texture = atlases.GetArrayElementAtIndex(i).objectReferenceValue as Texture;
                Vector2Int savedSize = savedAtlasSizes[i];
                if (texture != null && savedSize.x > 0 && savedSize.y > 0 &&
                    (texture.width > savedSize.x || texture.height > savedSize.y))
                    return true;
            }
            return false;
        }

        // Kerning, mark and ligature records, which TextMesh Pro reads from the font file for each glyph text adds.
        static int CountFeatureRecords(SerializedObject font)
        {
            SerializedProperty table = font.FindProperty("m_FontFeatureTable");
            if (table == null) return 0;

            int records = 0;
            SerializedProperty end = table.GetEndProperty();
            SerializedProperty field = table.Copy();
            for (bool more = field.Next(true); more && !SerializedProperty.EqualContents(field, end); more = field.Next(false))
            {
                if (field.isArray && field.propertyType == SerializedPropertyType.Generic) records += field.arraySize;
            }

            return records;
        }

        /// <summary>Saves <paramref name="font"/> with the glyphs and atlas data added since its last save, for tools that curate its glyph set.</summary>
        public static void SaveWithAddedGlyphs(Object font)
        {
            string path = AssetDatabase.GetAssetPath(font);
            if (string.IsNullOrEmpty(path)) return;

            s_curatedSaves.Add(path);
            try
            {
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset != null) EditorUtility.SetDirty(asset);
                }

                AssetDatabase.SaveAssetIfDirty(font);
            }
            finally
            {
                s_curatedSaves.Remove(path);
            }
        }

        // Read only the font's saved tables and referenced atlas dimensions; binary files are not held back.
        static bool TryReadSavedTables(string path, out int characters, out int glyphs, out int atlases, out int features, out List<Vector2Int> atlasSizes)
        {
            characters = glyphs = atlases = features = 0;
            atlasSizes = new List<Vector2Int>();
            List<long> atlasIds = new List<long>();
            Dictionary<long, Vector2Int> textureSizes = new Dictionary<long, Vector2Int>();
            long textureId = 0;
            string file = FileUtil.GetPhysicalPath(path);
            if (!File.Exists(file)) return false;

            bool first = true;
            bool featureTable = false;
            int table = 0;
            foreach (string line in File.ReadLines(file))
            {
                if (first)
                {
                    if (!line.StartsWith("%YAML", StringComparison.Ordinal)) return false;
                    first = false;
                    continue;
                }

                if (line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    textureId = line.StartsWith("--- !u!28 &", StringComparison.Ordinal) && TryReadFileId(line, out long headerId) ? headerId : 0;
                    table = 0;
                    featureTable = false;
                    continue;
                }

                if (textureId != 0)
                {
                    textureSizes.TryGetValue(textureId, out Vector2Int textureSize);
                    if (line.StartsWith("  m_Width: ", StringComparison.Ordinal) && int.TryParse(line.Substring(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width))
                        textureSize.x = width;
                    else if (line.StartsWith("  m_Height: ", StringComparison.Ordinal) && int.TryParse(line.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
                        textureSize.y = height;
                    textureSizes[textureId] = textureSize;
                }

                // The feature table's record lists sit four spaces deep, with their entries at that depth.
                if (featureTable && line.Length > 4 && line[4] == '-' && line.StartsWith("    ", StringComparison.Ordinal))
                {
                    features++;
                    continue;
                }

                // Serialized fields sit two spaces deep; list entries at that depth start with a dash.
                if (line.Length < 3 || line[0] != ' ' || line[1] != ' ' || line[2] == ' ') continue;

                if (line[2] != '-')
                {
                    table = line == "  m_CharacterTable:" ? 1 : line == "  m_GlyphTable:" ? 2 : line == "  m_AtlasTextures:" ? 3 : 0;
                    featureTable = line == "  m_FontFeatureTable:";
                    continue;
                }

                if (table == 1) characters++;
                else if (table == 2) glyphs++;
                else if (table == 3)
                {
                    atlases++;
                    atlasIds.Add(line.IndexOf("guid:", StringComparison.Ordinal) < 0 && TryReadFileId(line, out long referenceId) ? referenceId : 0);
                }
            }

            foreach (long atlasId in atlasIds)
                atlasSizes.Add(textureSizes.TryGetValue(atlasId, out Vector2Int savedSize) ? savedSize : Vector2Int.zero);
            return true;
        }

        static bool TryReadFileId(string line, out long id)
        {
            int start = line.IndexOf("fileID: ", StringComparison.Ordinal);
            if (start >= 0)
                start += 8;
            else
            {
                start = line.IndexOf('&');
                if (start < 0)
                {
                    id = 0;
                    return false;
                }
                start++;
            }
            int end = start;
            if (end < line.Length && line[end] == '-') end++;
            while (end < line.Length && line[end] >= '0' && line[end] <= '9') end++;
            return long.TryParse(line.Substring(start, end - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
        }
    }
}
