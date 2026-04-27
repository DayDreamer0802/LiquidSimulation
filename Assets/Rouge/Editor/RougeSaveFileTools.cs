using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

public static class RougeSaveFileTools
{
    [MenuItem("Rouge/Tools/Open Save Folder")]
    private static void OpenSaveFolder()
    {
        string saveFolder = Application.persistentDataPath;
        if (string.IsNullOrEmpty(saveFolder))
        {
            EditorUtility.DisplayDialog("Open Save Folder", "persistentDataPath is empty.", "OK");
            return;
        }

        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
        }

        EditorUtility.RevealInFinder(saveFolder);
        UnityEngine.Debug.Log($"[RougeSaveFileTools] Save folder: {saveFolder}");
    }

    [MenuItem("Rouge/Tools/Open Save File...")]
    private static void OpenSaveFile()
    {
        string saveFolder = Application.persistentDataPath;
        if (!Directory.Exists(saveFolder))
        {
            Directory.CreateDirectory(saveFolder);
        }

        string selectedPath = EditorUtility.OpenFilePanel("Select save file", saveFolder, string.Empty);
        if (string.IsNullOrEmpty(selectedPath))
        {
            return;
        }

        if (!File.Exists(selectedPath))
        {
            EditorUtility.DisplayDialog("Open Save File", "Selected file does not exist.", "OK");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = selectedPath,
                UseShellExecute = true
            });

            UnityEngine.Debug.Log($"[RougeSaveFileTools] Opened save file: {selectedPath}");
        }
        catch (System.Exception ex)
        {
            EditorUtility.DisplayDialog("Open Save File", $"Failed to open file.\n{ex.Message}", "OK");
        }
    }
}
