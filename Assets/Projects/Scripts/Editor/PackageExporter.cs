using UnityEditor;
using UnityEngine;
using System.IO;

namespace BoardGameKit.Editor
{
    /// <summary>
    /// VRC-BoardGameKitの配布用 .unitypackage をワンクリックで書き出すエディタ拡張。
    /// </summary>
    public static class PackageExporter
    {
        [MenuItem("Tools/VRC-BoardGameKit/Utilities/.unitypackage を書き出し (Export Package)", false, 31)]
        public static void Export()
        {
            string exportPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../VRC-BoardGameKit.unitypackage"));
            string assetPath = "Assets/Projects";

            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                Debug.LogError($"[PackageExporter] 対象フォルダが見つかりません: {assetPath}");
                return;
            }

            AssetDatabase.ExportPackage(
                assetPath,
                exportPath,
                ExportPackageOptions.Recurse | ExportPackageOptions.IncludeDependencies
            );

            Debug.Log($"<color=#00FF00>[PackageExporter] .unitypackage のエクスポートが完了しました！</color>\n出力先: {exportPath}");
            EditorUtility.RevealInFinder(exportPath);
        }
    }
}
