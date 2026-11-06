/**===========================================================================

 File   : FBXアニメーション修復ツール [FbxAnimationFixer.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 FBXのアニメーションインポート設定を診断・修復するエディタウィンドウ。
 AnimationTypeをGenericに戻し、FBX内蔵のデフォルトClipを復元して
 pasted__Root系のAvatarエラーを回避する。
 ===========================================================================**/
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public class FbxAnimationFixer : EditorWindow
{
    private Object _animFolder;
    private Vector2 _scroll;
    private readonly List<string> _log = new();

    [MenuItem("Tools/FBX Animation Fixer")]
    public static void Open() => GetWindow<FbxAnimationFixer>("FBX Animation Fixer");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("FBX Animation Fixer", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _animFolder = EditorGUILayout.ObjectField(
            "Animation FBX Folder", _animFolder, typeof(DefaultAsset), false);

        EditorGUILayout.Space(8);

        using (new EditorGUI.DisabledScope(_animFolder == null))
        {
            if (GUILayout.Button("Diagnose", GUILayout.Height(28)))
                Diagnose();

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Generic に戻してFBXからClipを復元します。\n" +
                "pasted__Root エラーが出た場合はこちらを使用してください。",
                MessageType.Info);

            if (GUILayout.Button("Revert to Generic + Restore Clips", GUILayout.Height(32)))
                RevertToGenericAndRestoreClips();
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(200));
        foreach (var line in _log)
            EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();

        if (_log.Count > 0 && GUILayout.Button("Clear Log"))
            _log.Clear();
    }

    private void Diagnose()
    {
        _log.Clear();
        var animPaths = CollectAnimPaths();
        if (animPaths == null) return;

        foreach (var path in animPaths)
        {
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;

            int clipCount = imp.clipAnimations.Length;
            int defaultCount = imp.defaultClipAnimations.Length;
            Log($"  {Path.GetFileName(path)}"
              + $" | type={imp.animationType}"
              + $" | clips={clipCount} (default={defaultCount})");
        }
    }

    private void RevertToGenericAndRestoreClips()
    {
        _log.Clear();
        var animPaths = CollectAnimPaths();
        if (animPaths == null) return;

        int fixed_ = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in animPaths)
            {
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;

                bool changed = false;

                if (imp.animationType != ModelImporterAnimationType.Generic)
                {
                    imp.animationType = ModelImporterAnimationType.Generic;
                    changed = true;
                }
                if (imp.avatarSetup != ModelImporterAvatarSetup.NoAvatar)
                {
                    imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                    changed = true;
                }

                // clipAnimations が空の場合、FBX内蔵のデフォルトclipで復元
                if (imp.clipAnimations.Length == 0)
                {
                    var defaults = imp.defaultClipAnimations;
                    if (defaults.Length > 0)
                    {
                        imp.clipAnimations = defaults;
                        changed = true;
                    }
                }

                if (changed)
                {
                    imp.SaveAndReimport();
                    Log($"[Fixed] {Path.GetFileName(path)}"
                      + $" → clips={imp.clipAnimations.Length}");
                    fixed_++;
                }
                else
                {
                    Log($"[Skip]  {Path.GetFileName(path)} (already OK)");
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        Log($"--- Done: {fixed_} file(s) fixed ---");
        Log("");
        Log("[注意] モデル(Player_Model.fbx)にも Rig > Animation Type: Generic を設定してください。");
        Log("  pasted__Root は FBX のボーン名が壊れているため Humanoid Avatar が使えません。");
        AssetDatabase.Refresh();
    }

    private List<string> CollectAnimPaths()
    {
        if (_animFolder == null)
        {
            Log("[Error] Animation FBX Folder が未設定");
            return null;
        }

        string folderPath = AssetDatabase.GetAssetPath(_animFolder);
        string[] guids = AssetDatabase.FindAssets("t:GameObject", new[] { folderPath });

        var paths = new List<string>();
        foreach (var guid in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                paths.Add(p);
        }

        Log($"対象 FBX: {paths.Count} 件");
        return paths;
    }

    private void Log(string msg)
    {
        _log.Add(msg);
        Debug.Log($"[FbxFixer] {msg}");
        Repaint();
    }
}
