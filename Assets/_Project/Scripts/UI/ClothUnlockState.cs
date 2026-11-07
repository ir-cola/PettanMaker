/**===========================================================================

 File   : 衣装アンロック状態（セッション保持）[ClothUnlockState.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/10
 -----------------------------------------------------------------------------

 CHANGE タブの服の「解放済み」状態をセッション内で保持する軽量ストア。
   ・シーンを再ロードしても解放状態を引き継ぐ（static はシーンをまたいで残る）
   ・ドメインリロード（再生停止／再コンパイル）で消える＝セーブ機能ではない
   ・StickerBookUI が Start でここから解放済みを取り込み、UnlockCostume で書き戻す
   ・AliceUnlockTutorial はチュートリアル済みフラグの保持にも使う

 保存機能を入れる場合はここを PlayerPrefs 等に差し替えれば良い（他は無改修）。

===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public static class ClothUnlockState {

    /// <summary>実行時に解放された服プレハブ（プレハブはアセット参照なのでシーンをまたいでも有効）</summary>
    public static readonly HashSet<GameObject> Unlocked = new HashSet<GameObject>();

    /// <summary>アリス入手チュートリアルを再生済みか（多重再生防止）</summary>
    public static bool AliceTutorialDone = false;

    /// <summary>指定プレハブが解放済みか</summary>
    public static bool IsUnlocked(GameObject prefab) => prefab != null && Unlocked.Contains(prefab);

    /// <summary>全状態を初期化（デバッグ用）</summary>
    public static void ResetAll() { Unlocked.Clear(); AliceTutorialDone = false; }
}
