/**===========================================================================

 File   : コンポーネントコピー [ComponentCopier.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 コピー元 GameObject の全コンポーネントを、リフレクションでフィールド/
 プロパティごとコピー先へ複製するエディタ拡張ユーティリティ。
 ===========================================================================**/
using UnityEngine;
using System;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ComponentCopier : MonoBehaviour {
    public GameObject sourceObject;   // コピー元
    public GameObject targetObject;   // コピー先

    [Header("オプション")]
    public bool overwriteTransform = false;  // Transformを上書きするか
    public bool overwriteExisting = false;   // 既存コンポーネントを上書きするか
}

#if UNITY_EDITOR
[CustomEditor(typeof(ComponentCopier))]
public class ComponentCopierEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();

        ComponentCopier copier = (ComponentCopier)target;

        if (GUILayout.Button("コンポーネントをコピー")) {
            if (copier.sourceObject == null || copier.targetObject == null) {
                Debug.LogError("コピー元またはコピー先が設定されていません！");
                return;
            }

            CopyComponents(copier);
        }
    }

    private void CopyComponents(ComponentCopier copier) {
        GameObject source = copier.sourceObject;
        GameObject target = copier.targetObject;

        foreach (var comp in source.GetComponents<Component>()) {
            if (comp is Transform) {
                if (copier.overwriteTransform) {
                    Undo.RecordObject(target.transform, "Copy Transform");
                    target.transform.position = source.transform.position;
                    target.transform.rotation = source.transform.rotation;
                    target.transform.localScale = source.transform.localScale;
                }
                continue;
            }

            Type type = comp.GetType();
            Component targetComp = target.GetComponent(type);

            if (targetComp != null) {
                if (copier.overwriteExisting) {
                    CopyComponentValues(comp, targetComp);
                }
            }
            else {
                Component newComp = Undo.AddComponent(target, type);
                CopyComponentValues(comp, newComp);
            }
        }

        Debug.Log("コンポーネントコピー完了！");
    }

    private void CopyComponentValues(Component source, Component destination) {
        Type type = source.GetType();
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var field in type.GetFields(flags)) {
            if (field.IsStatic) continue;
            field.SetValue(destination, field.GetValue(source));
        }

        foreach (var prop in type.GetProperties(flags)) {
            if (!prop.CanWrite || !prop.CanRead || prop.Name == "name") continue;
            try {
                prop.SetValue(destination, prop.GetValue(source, null), null);
            }
            catch { }
        }
    }
}
#endif
