/**===========================================================================

 File   : ロード画面素材参照 [SceneLoaderAssets.cs]

 Author : Hayato Otsuki
 Date   : 2026/07/02
 -----------------------------------------------------------------------------

   コード生成の SceneLoader からスプライト/フォント/シェーダーを参照するための
   ScriptableObject。Resources 直下に SceneLoaderAssets.asset として置く。

 ===========================================================================**/
using UnityEngine;

[CreateAssetMenu(fileName = "SceneLoaderAssets", menuName = "Custom/Scene Loader Assets")]
public class SceneLoaderAssets : ScriptableObject
{
    public Sprite background;                 // load.png（黄色パネル＋青枠）
    public Sprite character;                  // Special_Queen.png（中央キャラ）
    public TMPro.TMP_FontAsset font;          // RabbitandFullMoon SDF
    public Shader polkaShader;                // UI/PolkaDotWave

    [Header("シーン遷移SE (StickerTransition が使用)")]
    public AudioClip transitionPeelSe;        // 画面を剥がす音 (Sticker_PeelOff_0)
    public AudioClip transitionPasteSe;       // 画面を貼る音 (Sticker_Paste)
    [Tooltip("遷移SE の音量。1 超は増幅 (上げすぎると音割れ)")]
    [Range(0f, 2f)] public float transitionSeVolume = 1.2f;
}
