using UnityEditor.PackageManager;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ImpossibleRobert.Common.Editor.Packages
{
    /// <summary>Decides whether an editor repair of a package asset may be saved to disk.</summary>
    public static class PackageAssetOwnership
    {
        /// <summary>
        /// True for project assets and embedded packages. A local file package can be shared with projects on other Unity
        /// versions, and registry, Git, tarball and built-in packages are read-only, so their repairs stay in memory.
        /// </summary>
        public static bool IsProjectOwned(string assetPath)
        {
            PackageInfo package = PackageInfo.FindForAssetPath(assetPath);
            return package == null || package.source == PackageSource.Embedded;
        }
    }
}
