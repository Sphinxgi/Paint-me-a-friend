using UnityEditor;
using UnityEngine;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Version-safe wrappers for Object identity and other editor APIs deprecated in Unity 6.3+.
    /// Companion to <see cref="UnityUtils"/> (runtime) for editor code.
    /// </summary>
    public static class UnityEditorCompat
    {
        /// <summary>
        /// Version-safe wrapper for AssetPreview.IsLoadingAssetPreview.
        /// Uses the EntityId overload on Unity 6.3+ and the int overload on older versions.
        /// </summary>
        public static bool IsLoadingPreview(Object obj)
        {
#if UNITY_6000_3_OR_NEWER
            return AssetPreview.IsLoadingAssetPreview(obj.GetEntityId());
#else
            return AssetPreview.IsLoadingAssetPreview(obj.GetInstanceID());
#endif
        }

        /// <summary>
        /// Returns the asset GUID an object reference property stores, also when that asset is missing from the project.
        /// Uses the EntityId APIs on Unity 6.4+ and the instance ID APIs on older versions.
        /// </summary>
        public static bool TryGetReferencedAssetGuid(SerializedProperty property, out string guid)
        {
#if UNITY_6000_4_OR_NEWER
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceEntityIdValue, out guid, out long _);
#else
#pragma warning disable CS0618
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceInstanceIDValue, out guid, out long _);
#pragma warning restore CS0618
#endif
        }

        /// <summary>
        /// Version-safe wrapper for importing a Unity package.
        /// Uses the AssetPackage API on Unity 6.6+ and AssetDatabase on older versions.
        /// </summary>
        public static void ImportPackage(string packagePath, bool interactive)
        {
#if UNITY_6000_6_OR_NEWER
            UnityEditor.AssetPackage.Package.Import(packagePath, interactive);
#else
            AssetDatabase.ImportPackage(packagePath, interactive);
#endif
        }
    }
}
