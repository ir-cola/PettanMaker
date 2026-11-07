/**===========================================================================

 File   : BGM 定義アセット [BGMAssets.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/4
 -----------------------------------------------------------------------------
 シーン別 BGM のクリップ割り当てと音量設定。
 Assets/_Project/Resources/BGMAssets.asset として置き、BGMManager が
 Resources.Load で読む (SceneLoaderAssets と同じ方式)。
 ===========================================================================**/

using UnityEngine;

[CreateAssetMenu(fileName = "BGMAssets", menuName = "PettanMaker/BGM Assets")]
public class BGMAssets : ScriptableObject {

    [Header("シーン別 BGM")]
    public AudioClip title;         // Title シーン
    public AudioClip stageSelect;   // StageSelect シーン
    public AudioClip loading;       // ロード画面表示中 (SceneLoader.IsActive)
    public AudioClip stage;         // Stage_* シーン (インゲーム)

    [Header("再生設定")]
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("BGM 切り替え時のクロスフェード時間 (秒)")]
    [Min(0.05f)] public float fadeTime = 0.8f;
}
