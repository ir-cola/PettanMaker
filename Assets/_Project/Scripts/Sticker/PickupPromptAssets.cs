/**===========================================================================

 File   : シール回収プロンプト用アセット参照 [PickupPromptAssets.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/6
 -----------------------------------------------------------------------------

 服シール回収プロンプト (StickerPickupPrompt) が実行時に使うアセット参照。
 シールは実行時生成 (EnemyDeath → StickerPickup) でシーン配線ができないため、
 Resources/PickupPromptAssets.asset から読み込む。

===========================================================================**/

using UnityEngine;

/**===========================================================================
  シール回収プロンプト用アセット参照
===========================================================================**/
public class PickupPromptAssets : ScriptableObject {

    [Tooltip("マウス操作時のアイコン (右クリック)")]
    public Sprite mouseIcon;
    [Tooltip("ゲームパッド操作時のアイコン (Xボタン)")]
    public Sprite padIcon;
    [Tooltip("背景パネルのスプライト")]
    public Sprite backSprite;
    [Tooltip("ラベルフォント")]
    public TMPro.TMP_FontAsset font;
    [Tooltip("アイコン用グローマテリアル (任意)")]
    public Material iconMaterial;
    [Tooltip("ラベル用グローマテリアル (任意)")]
    public Material labelMaterial;
}
