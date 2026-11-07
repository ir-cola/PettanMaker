/**===========================================================================

 File   : シールインベントリクラス [StickerInventory.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22
 -----------------------------------------------------------------------------
 回収したシールを種別ごとに管理するシングルトンインベントリ。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/**===========================================================================
  シールインベントリクラス（シングルトン）
  回収したシールを種別ごとに管理する
===========================================================================**/
public class StickerInventory : MonoBehaviour {

    // シングルトンインスタンス
    public static StickerInventory Instance { get; private set; }

    // 種別ごとのシールリスト
    private Dictionary<StickerType, List<StickerData>> stickerMap;

    // シール追加時に通知するイベント
    public UnityEvent<StickerData> onStickerAdded;

    // シール削除時に通知するイベント（アイテム使用などで消費されたとき）
    public UnityEvent<StickerData> onStickerRemoved;

    /**=======================================================================
      初期化
    =======================================================================**/
    void Awake() {
        // シングルトン設定
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // UnityEvent はランタイム生成時に null のことがあるため保険で初期化
        if (onStickerAdded == null)   onStickerAdded   = new UnityEvent<StickerData>();
        if (onStickerRemoved == null) onStickerRemoved = new UnityEvent<StickerData>();

        InitializeDictionary();
    }

    /**=======================================================================
      辞書の初期化
    =======================================================================**/
    private void InitializeDictionary() {
        stickerMap = new Dictionary<StickerType, List<StickerData>>();

        foreach (StickerType type in System.Enum.GetValues(typeof(StickerType))) {
            stickerMap[type] = new List<StickerData>();
        }
    }

    // 各シールの表示用スプライトを保持（thumbnail 未設定の場合に使う）
    private Dictionary<StickerData, Sprite> displaySprites = new Dictionary<StickerData, Sprite>();

    /**=======================================================================
      シールを追加する（表示スプライトも一緒に登録できる）
    =======================================================================**/
    public void AddSticker(StickerData data, Sprite displaySprite = null) {
        if (data == null) return;

        stickerMap[data.stickerType].Add(data);

        if (displaySprite != null) displaySprites[data] = displaySprite;

        onStickerAdded?.Invoke(data);

        Debug.Log($"[StickerInventory] 追加：{data.stickerName}（{data.stickerType}）");
    }

    /**=======================================================================
      シールの表示用スプライトを取得する
      thumbnail > 登録スプライト > decalTexture の順で優先する
    =======================================================================**/
    public Sprite GetDisplaySprite(StickerData data) {
        if (data == null) return null;
        if (data.thumbnail != null) return data.thumbnail;
        if (displaySprites.TryGetValue(data, out Sprite sp)) return sp;
        if (data.decalTexture != null) {
            // 生成したスプライトはキャッシュして毎回 Sprite.Create しない（呼び出し毎のリークを防ぐ）
            var created = Sprite.Create(
                data.decalTexture,
                new Rect(0, 0, data.decalTexture.width, data.decalTexture.height),
                new Vector2(0.5f, 0.5f)
            );
            displaySprites[data] = created;
            return created;
        }
        return null;
    }

    /**=======================================================================
      シールを1枚削除する（アイテム使用で消費する用途）
      対象 StickerData が存在すれば1つ取り除いて true を返す
    =======================================================================**/
    public bool RemoveSticker(StickerData data) {
        if (data == null) return false;
        if (!stickerMap.TryGetValue(data.stickerType, out List<StickerData> list)) return false;

        int idx = list.IndexOf(data);
        if (idx < 0) return false;

        list.RemoveAt(idx);
        onStickerRemoved?.Invoke(data);

        Debug.Log($"[StickerInventory] 消費：{data.stickerName}（{data.stickerType}）");
        return true;
    }

    /**=======================================================================
      種別ごとのシールリストを取得する
    =======================================================================**/
    public List<StickerData> GetStickers(StickerType type) {
        if (stickerMap.TryGetValue(type, out List<StickerData> list)) {
            return list;
        }
        return new List<StickerData>();
    }

    /**=======================================================================
      全シールを取得する
    =======================================================================**/
    public List<StickerData> GetAllStickers() {
        List<StickerData> all = new List<StickerData>();
        foreach (var list in stickerMap.Values) {
            all.AddRange(list);
        }
        return all;
    }
}
