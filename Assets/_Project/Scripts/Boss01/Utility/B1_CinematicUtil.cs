using UnityEngine;

/// <summary>
/// カメラ演出まわりの実行時ヘルパ。
/// ContextMenu などで Edit モードから演出を再生した際、Destroy は使えず
/// DestroyImmediate が必要になるため、モードに応じて安全に破棄する。
/// </summary>
public static class B1_CinematicUtil
{
    public static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
