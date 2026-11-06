/**===========================================================================

 File   : ボーンリネーマー [BoneRenamer.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 FBX インポート時にボーン名・アニメカーブパスを一括リネーム／指定ノードを
 除去する Editor ツール。ルール設定とプレビューを行う EditorWindow と、
 再インポート時に適用する AssetPostprocessor で構成する。
 ===========================================================================**/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// -------------------------------------------------------
// 設定保持（Postprocessor ⇔ Window 間の共有）
// -------------------------------------------------------
public static class BoneRenameSettings
{
    public static bool         Enabled       = false;
    public static string       FolderFilter  = "";
    public static List<Rule>   Rules         = new();
    public static List<string> CollapseNodes = new(); // このノードを削除し子を親に付け替え

    public enum RuleMode
    {
        Segment,       // ボーン名内の文字列を置換
        SegmentPrefix, // ボーン名の先頭一致で置換
        PathPrefix,    // カーブパスの先頭セグメントを除去
    }

    [Serializable]
    public class Rule
    {
        public string   Find    = "";
        public string   Replace = "";
        public RuleMode Mode    = RuleMode.Segment;
        public bool     IsValid => !string.IsNullOrEmpty(Find);

        public string ApplyToSegment(string name)
        {
            if (!IsValid || Mode == RuleMode.PathPrefix) return name;
            if (Mode == RuleMode.SegmentPrefix)
                return name.StartsWith(Find) ? Replace + name[Find.Length..] : name;
            return name.Replace(Find, Replace);
        }

        public string ApplyToFullPath(string path)
        {
            if (!IsValid || Mode != RuleMode.PathPrefix) return path;
            string p = Find.TrimEnd('/');
            if (path == p)           return Replace;
            if (path.StartsWith(p + "/")) return string.IsNullOrEmpty(Replace)
                ? path[(p.Length + 1)..]
                : Replace + "/" + path[(p.Length + 1)..];
            return path;
        }
    }

    public static string ApplyAllSegment(string name)
    {
        foreach (var r in Rules) name = r.ApplyToSegment(name);
        return name;
    }

    public static string ApplyAllToPath(string path)
    {
        foreach (var r in Rules) path = r.ApplyToFullPath(path);   // PathPrefix 先行
        var segs = path.Split('/');
        for (int i = 0; i < segs.Length; i++) segs[i] = ApplyAllSegment(segs[i]);
        return string.Join("/", segs);
    }
}

// -------------------------------------------------------
// AssetPostprocessor
// -------------------------------------------------------
class BoneRenamePostprocessor : AssetPostprocessor
{
    void OnPostprocessModel(GameObject root)
    {
        if (!ShouldProcess()) return;
        CollapseNodes(root.transform);
        RenameRecursive(root.transform);
    }

    void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!ShouldProcess()) return;
        RebindCurves(clip);
        RebindObjectCurves(clip);
    }

    bool ShouldProcess()
    {
        if (!BoneRenameSettings.Enabled) return false;
        if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(BoneRenameSettings.FolderFilter)
            && !assetPath.StartsWith(BoneRenameSettings.FolderFilter)) return false;
        return true;
    }

    // 指定ノードを取り除いて子を親に直結する
    static void CollapseNodes(Transform root)
    {
        foreach (var nodeName in BoneRenameSettings.CollapseNodes)
        {
            var target = FindDeep(root, nodeName);
            while (target != null)
            {
                var parent   = target.parent ?? root;
                var children = Enumerable.Range(0, target.childCount)
                                         .Select(i => target.GetChild(i)).ToList();
                foreach (var c in children) c.SetParent(parent, true);
                UnityEngine.Object.DestroyImmediate(target.gameObject);
                target = FindDeep(root, nodeName); // 同名ノードが複数あれば繰り返す
            }
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var found = FindDeep(c, name);
            if (found != null) return found;
        }
        return null;
    }

    static void RenameRecursive(Transform t)
    {
        t.name = BoneRenameSettings.ApplyAllSegment(t.name);
        foreach (Transform child in t) RenameRecursive(child);
    }

    static void RebindCurves(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var changes  = new List<(EditorCurveBinding old, EditorCurveBinding next, AnimationCurve curve)>();

        foreach (var b in bindings)
        {
            string newPath = BoneRenameSettings.ApplyAllToPath(b.path);
            if (newPath == b.path) continue;
            var nb = b; nb.path = newPath;
            changes.Add((b, nb, AnimationUtility.GetEditorCurve(clip, b)));
        }
        foreach (var (o, n, c) in changes)
        {
            AnimationUtility.SetEditorCurve(clip, o, null);
            AnimationUtility.SetEditorCurve(clip, n, c);
        }
    }

    static void RebindObjectCurves(AnimationClip clip)
    {
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
        var changes  = new List<(EditorCurveBinding old, EditorCurveBinding next, ObjectReferenceKeyframe[] keys)>();

        foreach (var b in bindings)
        {
            string newPath = BoneRenameSettings.ApplyAllToPath(b.path);
            if (newPath == b.path) continue;
            var nb = b; nb.path = newPath;
            changes.Add((b, nb, AnimationUtility.GetObjectReferenceCurve(clip, b)));
        }
        foreach (var (o, n, k) in changes)
        {
            AnimationUtility.SetObjectReferenceCurve(clip, o, null);
            AnimationUtility.SetObjectReferenceCurve(clip, n, k);
        }
    }
}

// -------------------------------------------------------
// EditorWindow
// -------------------------------------------------------
public class BoneRenamerWindow : EditorWindow
{
    private Vector2 _scrollRules;
    private Vector2 _scrollCollapse;
    private Vector2 _scrollPreview;
    private string  _previewText = "FBXを選択して「スキャン」を押してください";

    private static readonly string[] ModeLabels = { "セグメント", "先頭一致", "パスPfx除去" };

    [MenuItem("Tools/Bone Renamer")]
    public static void Open() => GetWindow<BoneRenamerWindow>("Bone Renamer");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Bone Renamer", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        EditorGUILayout.LabelField("適用フォルダ (空=全FBX対象)", EditorStyles.miniLabel);
        BoneRenameSettings.FolderFilter = EditorGUILayout.TextField(BoneRenameSettings.FolderFilter);

        EditorGUILayout.Space(6);
        DrawRulesSection();
        EditorGUILayout.Space(4);
        DrawCollapseSection();
        EditorGUILayout.Space(8);
        DrawPreviewSection();
        EditorGUILayout.Space(6);
        DrawApplyButton();
    }

    // ---- ルール ----
    private void DrawRulesSection()
    {
        EditorGUILayout.LabelField("リネームルール", EditorStyles.boldLabel);
        _scrollRules = EditorGUILayout.BeginScrollView(_scrollRules, GUILayout.MaxHeight(160));
        for (int i = 0; i < BoneRenameSettings.Rules.Count; i++)
        {
            var r = BoneRenameSettings.Rules[i];
            using (new EditorGUILayout.HorizontalScope())
            {
                r.Find    = EditorGUILayout.TextField(r.Find,    GUILayout.Width(110));
                GUILayout.Label("→", GUILayout.Width(16));
                r.Replace = EditorGUILayout.TextField(r.Replace, GUILayout.Width(110));
                r.Mode    = (BoneRenameSettings.RuleMode)EditorGUILayout.Popup(
                    (int)r.Mode, ModeLabels, GUILayout.Width(90));
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                { BoneRenameSettings.Rules.RemoveAt(i); break; }
            }
        }
        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("追加")) BoneRenameSettings.Rules.Add(new BoneRenameSettings.Rule());
            if (GUILayout.Button("pasted__ 除去"))
                BoneRenameSettings.Rules.Add(new BoneRenameSettings.Rule
                    { Find = "pasted__", Replace = "", Mode = BoneRenameSettings.RuleMode.SegmentPrefix });
            if (GUILayout.Button("group/ パスPfx除去"))
                BoneRenameSettings.Rules.Add(new BoneRenameSettings.Rule
                    { Find = "group", Replace = "", Mode = BoneRenameSettings.RuleMode.PathPrefix });
        }
    }

    // ---- ノード除去 ----
    private void DrawCollapseSection()
    {
        EditorGUILayout.LabelField("ノード除去（子を親に直結）", EditorStyles.boldLabel);
        _scrollCollapse = EditorGUILayout.BeginScrollView(_scrollCollapse, GUILayout.MaxHeight(70));
        for (int i = 0; i < BoneRenameSettings.CollapseNodes.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                BoneRenameSettings.CollapseNodes[i] =
                    EditorGUILayout.TextField(BoneRenameSettings.CollapseNodes[i]);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                { BoneRenameSettings.CollapseNodes.RemoveAt(i); break; }
            }
        }
        EditorGUILayout.EndScrollView();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("追加"))   BoneRenameSettings.CollapseNodes.Add("");
            if (GUILayout.Button("group4 を追加")) BoneRenameSettings.CollapseNodes.Add("group4");
        }
    }

    // ---- プレビュー ----
    private void DrawPreviewSection()
    {
        if (GUILayout.Button("選択FBXをスキャン", GUILayout.Height(26))) ScanSelected();
        _scrollPreview = EditorGUILayout.BeginScrollView(_scrollPreview, GUILayout.MinHeight(120));
        EditorGUILayout.TextArea(_previewText, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void DrawApplyButton()
    {
        var prev = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("選択FBXにルールを適用して再インポート", GUILayout.Height(32)))
            ApplyToSelected();
        GUI.backgroundColor = prev;
    }

    // --------------------------------------------------

    private void ScanSelected()
    {
        var paths = GetSelectedFbxPaths();
        if (paths.Count == 0) { _previewText = "FBXが選択されていません"; return; }

        var lines = new List<string>();
        foreach (var path in paths)
        {
            lines.Add($"=== {Path.GetFileName(path)} ===");
            lines.Add(ScanFbxDetail(path));
            lines.Add("");
        }
        _previewText = string.Join("\n", lines);
    }

    private void ApplyToSelected()
    {
        var paths = GetSelectedFbxPaths();
        if (paths.Count == 0) { Debug.LogWarning("[BoneRenamer] FBXが選択されていません"); return; }

        BoneRenameSettings.Enabled = true;
        try
        {
            foreach (var path in paths)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
        finally { BoneRenameSettings.Enabled = false; }

        AssetDatabase.Refresh();
        Debug.Log($"[BoneRenamer] {paths.Count} 件を再インポートしました");
        ScanSelected();
    }

    // --------------------------------------------------

    private static List<string> GetSelectedFbxPaths() =>
        Selection.objects
            .Select(AssetDatabase.GetAssetPath)
            .Where(p => p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static string ScanFbxDetail(string assetPath)
    {
        var lines     = new List<string>();
        var allAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);

        foreach (var asset in allAssets)
        {
            if (asset is not GameObject go || go.transform.parent != null) continue;
            lines.Add("[Transform 階層]");
            AppendTransformTree(go.transform, lines, 0);
            lines.Add("");
        }

        var pathsBefore = new SortedSet<string>();
        foreach (var asset in allAssets)
        {
            if (asset is not AnimationClip clip) continue;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
                if (!string.IsNullOrEmpty(b.path)) pathsBefore.Add(b.path);
            foreach (var b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                if (!string.IsNullOrEmpty(b.path)) pathsBefore.Add(b.path);
        }

        if (pathsBefore.Count > 0)
        {
            lines.Add("[カーブ パス]");
            foreach (var p in pathsBefore)
            {
                string after = BoneRenameSettings.ApplyAllToPath(p);
                lines.Add(after != p ? $"  {p}\n    → {after}" : $"  {p}");
            }
        }

        return string.Join("\n", lines);
    }

    static void AppendTransformTree(Transform t, List<string> lines, int depth)
    {
        string indent  = new string(' ', depth * 2);
        string renamed = BoneRenameSettings.ApplyAllSegment(t.name);
        string mark    = renamed != t.name ? $"  → {renamed}" : "";
        // Collapse 対象は [DEL] 表示
        string del     = BoneRenameSettings.CollapseNodes.Contains(t.name) ? " [DEL]" : "";
        lines.Add($"{indent}{t.name}{del}{mark}");
        foreach (Transform child in t) AppendTransformTree(child, lines, depth + 1);
    }
}
