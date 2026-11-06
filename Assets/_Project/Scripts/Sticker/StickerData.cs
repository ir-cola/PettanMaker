/**===========================================================================

 File   : シールデータクラス [StickerData.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 各シールの定義を保持する ScriptableObject と、その種別を表す列挙型。
 ===========================================================================**/

using UnityEngine;

/**===========================================================================
  シール種別
===========================================================================**/
public enum StickerType {
    Clothes,        // 服
    Item,           // アイテム
    Decoration,     // デコレーション
    Special,        // 特殊
    Skill           // スキル（シール帳 SKILL タブ）
}

/**===========================================================================
  シールデータクラス（ScriptableObject）
  各シールのデータ定義。ProjectウィンドウからCreate > Sticker > StickerData で作成できる
===========================================================================**/
[CreateAssetMenu(fileName = "NewSticker", menuName = "Sticker/StickerData")]
public class StickerData : ScriptableObject {

    [Header("基本情報")]
    public string stickerName = "新しいシール";
    public StickerType stickerType = StickerType.Decoration;

    [Header("見た目")]
    public Sprite thumbnail;       // インベントリに表示するサムネイル
    public Texture2D decalTexture;    // デカール投影用テクスチャ（服以外）

    [Header("服シール専用")]
    public GameObject equipPrefab;    // 装備として付け替えるメッシュプレハブ（服のみ使用）

    [Header("説明")]
    [TextArea(2, 4)]
    public string description = "";
    [Tooltip("シール帳の右ページに表示する説明画像（テキストの代わり）。未設定なら description をテキスト表示")]
    public Sprite descriptionSprite;    // シール帳右ページの説明画像（null ならテキスト）
}

