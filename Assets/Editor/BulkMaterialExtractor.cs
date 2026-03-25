using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// Extracts materials from every FBX/OBJ in the selected folder (or individual selected models).
/// Right-click any folder in the Project window → "Extract All Materials Here"
/// </summary>
public static class BulkMaterialExtractor
{
    [MenuItem("Assets/Extract All Materials Here", false, 30)]
    private static void ExtractAll()
    {
        // Collect all model GUIDs under every selected path
        string[] guids = AssetDatabase.FindAssets("t:Model", GetSelectedFolders());

        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Bulk Material Extractor",
                "No model files found in the selected folder(s).", "OK");
            return;
        }

        int extracted = 0;
        int failed    = 0;

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
                if (importer == null) continue;

                // Put materials next to the model in a "Materials" sub-folder
                string modelDir      = Path.GetDirectoryName(assetPath);
                string materialsDir  = Path.Combine(modelDir, "Materials").Replace('\\', '/');

                if (!AssetDatabase.IsValidFolder(materialsDir))
                    AssetDatabase.CreateFolder(modelDir.Replace('\\', '/'), "Materials");

                // Extract embedded materials
                var errors = importer.ExtractTextures(materialsDir);
                importer.materialLocation = ModelImporterMaterialLocation.External;
                importer.materialName     = ModelImporterMaterialName.BasedOnMaterialName;
                importer.materialSearch   = ModelImporterMaterialSearch.Everywhere;

                var mapping = new System.Collections.Generic.Dictionary<AssetImporter.SourceAssetIdentifier, Object>();
                foreach (var entry in importer.GetExternalObjectMap())
                    mapping[entry.Key] = entry.Value;

                // Write materials to disk and remap
                foreach (var entry in mapping)
                {
                    if (!(entry.Value is Material)) continue;

                    string matPath = Path.Combine(materialsDir, $"{entry.Key.name}.mat").Replace('\\', '/');
                    if (!File.Exists(matPath))
                    {
                        var mat = new Material(entry.Value as Material);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }

                    importer.AddRemap(entry.Key, AssetDatabase.LoadAssetAtPath<Material>(matPath));
                }

                AssetDatabase.WriteImportSettingsIfDirty(assetPath);
                importer.SaveAndReimport();
                extracted++;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BulkMaterialExtractor] Error: {e.Message}");
            failed++;
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog("Bulk Material Extractor",
            $"Done!\n\nExtracted: {extracted} model(s)\nFailed: {failed}", "OK");
    }

    // Only show the menu when a folder is selected
    [MenuItem("Assets/Extract All Materials Here", true)]
    private static bool ExtractAllValidate()
    {
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsValidFolder(path)) return true;
        }
        return false;
    }

    private static string[] GetSelectedFolders()
    {
        var folders = new System.Collections.Generic.List<string>();
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (AssetDatabase.IsValidFolder(path))
                folders.Add(path);
        }
        return folders.ToArray();
    }
}
