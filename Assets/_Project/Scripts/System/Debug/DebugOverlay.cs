/**===========================================================================

 File   : デバッグオーバーレイ [DebugOverlay.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 F1 で切り替える画面上のデバッグ表示。各所から Register/Unregister した
 セクション(ヘッダ+動的な内容)を IMGUI で一覧描画するシングルトン。
 ===========================================================================**/
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DebugOverlay : MonoBehaviour {
    public static DebugOverlay Instance { get; private set; }

    [SerializeField] private bool visible = true;

    public bool Visible { get => visible; set => visible = value; }

    private readonly Dictionary<int, (string header, Func<string> content)> sections = new();

    private void Awake() => Instance = this;
    private void OnDestroy() => Instance = null;

    private void Update() {
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            visible = !visible;
    }

    public static void Register(int id, string header, Func<string> content) {
        if (Instance != null) Instance.sections[id] = (header, content);
    }

    public static void Unregister(int id)
        => Instance?.sections.Remove(id);

    private void OnGUI() {
        if (!visible) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, Screen.height - 20));
        GUILayout.BeginVertical("box");
        GUILayout.Label("[ DEBUG OVERLAY ]");

        foreach (var kvp in sections) {
            string header  = kvp.Value.header;
            string content = kvp.Value.content();
            GUILayout.Space(4);
            GUILayout.Label($"[ {header} ]");
            GUILayout.Label(content);
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
