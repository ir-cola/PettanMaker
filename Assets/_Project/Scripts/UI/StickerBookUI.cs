/**===========================================================================

 File   : シール帳UIクラス [StickerBookUI.cs]

 Author : Hayato Otsuki
 Date   : 2026/4/22  (2026/7/8 リメイク：本デザイン対応)
 -----------------------------------------------------------------------------

 TABキーでシール帳（見開きの本）を開閉する。
   左ページ：カテゴリ内容（CHANGE=衣装カタログ / ITEM・SKILL=所持シール）を
             2×2 の横スワイプページで表示（→ ClothesSwipeUI に委譲）
   左端    ：CHANGE / ITEM / SKILL の3タブ（on/off スプライトで切替）
   右ページ：説明テキストボックス＋「Tab で戻る」ヒント

 このスクリプトは Canvas に直接アタッチすること
   （BookPanel にアタッチすると SetActive で自分ごと止まる）

===========================================================================**/

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/**===========================================================================
  ページビューへ渡す1枚分の表示データ
===========================================================================**/
public struct BookItem {
    public Sprite      sprite;       // 表示スプライト
    public string      name;         // 名前（説明ボックス1行目）
    public string      description;  // 説明文
    public StickerData data;         // 元データ（カタログ表示では null 可）
    public GameObject  equipPrefab;  // 服カテゴリ：着せ替えするメッシュプレハブ（Clothes のみ使用・null 可）
    public Sprite      descriptionSprite; // 説明画像（右ページに画像で表示。null ならテキストにフォールバック）
}

/**===========================================================================
  衣装カタログの1エントリ（CHANGE タブ用）
  所持シールに依らず固定表示する着せ替え候補
===========================================================================**/
[Serializable]
public class CostumeEntry {
    public string             displayName = "";
    [TextArea(2, 4)] public string description = "";
    public Sprite             sprite;         // カタログ表示スプライト（Sticker_Clothes_xx など）
    public GameObject         equipPrefab;    // 着せ替えするメッシュプレハブ（Cloth_Default 等）。null なら見た目カタログのみ
    public StickerData        stickerData;    // 任意：装備反映に使う元データ（equipPrefab 未設定時のフォールバック）
    public Sprite             descriptionSprite; // 右ページの説明画像（テキストの代わりに表示）。null ならテキスト表示
    [Tooltip("最初から解放済みか。未チェックの服は入手する（UnlockCostume）までCHANGEタブに表示されない")]
    public bool               unlockedByDefault = false;
}

/**===========================================================================
  1つのカテゴリタブ（on/off スプライトを持つボタン）
===========================================================================**/
[Serializable]
public class CategoryTab {
    public StickerType category;
    public Button      button;
    public Image       image;
    public Sprite      onSprite;    // 選択中
    public Sprite      offSprite;   // 非選択
}

/**===========================================================================
  シール帳UIクラス
  Canvas にアタッチ。BookPanel は別オブジェクト。
===========================================================================**/
public class StickerBookUI : MonoBehaviour {

    [Header("パネル（Canvas の子オブジェクト）")]
    [SerializeField] private GameObject bookPanel;               // 開閉する見開きパネル（Canvas の子）

    [Header("カテゴリタブ（左端・CHANGE / ITEM / SKILL）")]
    [SerializeField] private List<CategoryTab> tabs = new List<CategoryTab>();

    [Header("左ページ：ページビュー")]
    [Tooltip("2×2 横スワイプでシールを並べるページビュー")]
    [SerializeField] private ClothesSwipeUI pageView;
    [Tooltip("CHANGE カテゴリはハンガー枠付きで表示する")]
    [SerializeField] private bool useHangerForClothes = true;

    [Header("右ページ：説明テキストボックス")]
    [SerializeField] private TMP_Text descriptionText;
    [Tooltip("何も選択していないときに表示する文言（空欄可）")]
    [TextArea(1, 3)]
    [SerializeField] private string emptyDescription = "";
    [Tooltip("説明を画像で表示する Image（DescBox に重ねる）。選択に応じて各アイテムの descriptionSprite を差し替える。\n" +
             "descriptionSprite が設定されているアイテムは画像で表示し、テキストは隠す。未設定ならテキストにフォールバック。")]
    [SerializeField] private Image descriptionImage;

    [Header("CHANGE：衣装カタログ")]
    [Tooltip("CHANGE タブに固定表示する衣装候補")]
    [SerializeField] private List<CostumeEntry> costumeCatalog = new List<CostumeEntry>();

    [Header("プレイヤー操作（UI表示中に無効化）")]
    [SerializeField] private PlayerLifetimeScope player;
    [SerializeField] private TPSCameraController cameraController;
    [Tooltip("着せ替え反映先。未設定なら Player タグから自動取得")]
    [SerializeField] private PlayerModelSwitcher modelSwitcher;

    [Header("右ページ：3Dプレビュー（ピンクパネル）")]
    [Tooltip("シール帳を開いている間、右のピンクパネルにプレイヤーを描画するビューア。未設定なら自動取得")]
    [SerializeField] private PlayerViewer3D playerViewer;

    [Header("SE - 開き中ループ")]
    [Tooltip("シール帳を開いている間ループ再生するSE。未設定なら再生しない。")]
    [SerializeField] private AudioClip openLoopSE;
    [Range(0f, 1f)]
    [SerializeField] private float openLoopVolume = 1f;
    private AudioSource openLoopSource;

    [Header("SE - 開閉")]
    [Tooltip("シール帳を開いた瞬間に鳴らすSE。")]
    [SerializeField] private AudioClip openSE;
    [Tooltip("シール帳を閉じた瞬間に鳴らすSE。")]
    [SerializeField] private AudioClip closeSE;
    [Range(0f, 1f)]
    [SerializeField] private float openCloseVolume = 1f;
    private AudioSource uiOneShotSource;

    // 内部
    private bool        isOpen = false;
    private bool        pinPlayer = false;         // UI表示中プレイヤーを開いた瞬間の位置へ固定
    private Vector3     pinnedPlayerPos;
    private StickerType currentCategory = StickerType.Clothes;
    private readonly HashSet<GameObject> runtimeUnlocked = new HashSet<GameObject>();  // 実行時に解放した服
    private bool        lockOpen = false;          // チュートリアル中など閉じ操作を禁止する

    /// <summary>CHANGE カタログに登録された全着せ替えプレハブ (null 除外)。
    /// ロード時プリキャッシュ (PlayerModelSwitcher) が「全種類の服」を漏れなく焼くのに使う。
    /// カタログに服を足せば自動でプリキャッシュ対象にもなる。</summary>
    public IEnumerable<GameObject> EnumerateClothPrefabs() {
        if (costumeCatalog == null) yield break;
        foreach (var c in costumeCatalog) {
            if (c == null) continue;
            GameObject p = c.equipPrefab != null ? c.equipPrefab
                         : (c.stickerData != null ? c.stickerData.equipPrefab : null);
            if (p != null) yield return p;
        }
    }

    // =======================================================================
    //  解放（アンロック）管理 ＋ 演出用アクセサ
    // =======================================================================

    /// <summary>その衣装が解放済みか（デフォルト解放 or 実行時 UnlockCostume 済み）</summary>
    private bool IsUnlocked(CostumeEntry c) {
        if (c == null) return false;
        if (c.unlockedByDefault) return true;
        GameObject p = c.equipPrefab != null ? c.equipPrefab
                     : (c.stickerData != null ? c.stickerData.equipPrefab : null);
        return p != null && runtimeUnlocked.Contains(p);
    }

    /// <summary>指定の服を実行時に解放する（チュートリアル／ドロップ入手で使用）。
    /// EnumerateClothPrefabs は解放状態に関わらず全服を返すのでプリキャッシュには影響しない。</summary>
    public void UnlockCostume(GameObject equipPrefab, bool refreshIfOpen = true) {
        if (equipPrefab == null) return;
        runtimeUnlocked.Add(equipPrefab);
        ClothUnlockState.Unlocked.Add(equipPrefab);
        if (refreshIfOpen && isOpen && currentCategory == StickerType.Clothes) RefreshCategory();
    }

    /// <summary>その服が解放済みか（プレハブ参照で判定）</summary>
    public bool IsUnlocked(GameObject equipPrefab)
        => equipPrefab != null && (runtimeUnlocked.Contains(equipPrefab)
           || costumeCatalog.Exists(c => c != null && c.unlockedByDefault && c.equipPrefab == equipPrefab));

    /// <summary>CHANGE タブの現在の表示並びでの添字（未表示なら -1）。解放直後の演出対象セル特定に使う。</summary>
    public int VisibleIndexOf(GameObject equipPrefab) {
        var items = BuildItems(StickerType.Clothes);
        for (int i = 0; i < items.Count; i++)
            if (items[i].equipPrefab == equipPrefab) return i;
        return -1;
    }

    // 演出（AliceUnlockTutorial 等）から参照するアクセサ
    public bool                IsBookOpen      => isOpen;
    public ClothesSwipeUI      PageView        => pageView;
    public PlayerModelSwitcher ModelSwitcher   => modelSwitcher;
    public GameObject          BookPanelObject => bookPanel;
    public PlayerViewer3D      PlayerViewer    => playerViewer;

    /// <summary>チュートリアル中に本を閉じさせない（true）／解除（false）</summary>
    public void SetLockOpen(bool v) { lockOpen = v; }
    public void OpenBook()  { if (!isOpen) SetOpen(true); }
    public void CloseBook() { lockOpen = false; if (isOpen) SetOpen(false); }

    /// <summary>CHANGE 以外のタブ(ITEM/SKILL)を押せなくする（true）／戻す（false）。チュートリアル中の固定用。</summary>
    public void LockNonClothesTabs(bool locked) {
        foreach (var tab in tabs) {
            if (tab == null || tab.button == null) continue;
            if (tab.category != StickerType.Clothes) tab.button.interactable = !locked;
        }
        if (locked) SelectCategory(StickerType.Clothes);   // CHANGE に固定
    }

    /**=======================================================================
      開始処理
      全オブジェクトの Awake() が終わった後に BookPanel を非表示にする
    =======================================================================**/
    void Start() {
        if (player == null)
            player = FindObjectOfType<PlayerLifetimeScope>();
        if (cameraController == null)
            cameraController = FindObjectOfType<TPSCameraController>();
        if (modelSwitcher == null) {
            GameObject playerGo = GameObject.FindWithTag("Player");
            if (playerGo != null) modelSwitcher = playerGo.GetComponentInChildren<PlayerModelSwitcher>();
            if (modelSwitcher == null) modelSwitcher = FindObjectOfType<PlayerModelSwitcher>();
        }
        if (playerViewer == null)
            playerViewer = FindObjectOfType<PlayerViewer3D>(true);

        // セッション内で解放済みの服を引き継ぐ（シーン再入場で消えないように）
        foreach (var p in ClothUnlockState.Unlocked) if (p != null) runtimeUnlocked.Add(p);

        if (bookPanel != null) bookPanel.SetActive(false);

        // タブのクリックを登録
        foreach (var tab in tabs) {
            if (tab == null || tab.button == null) continue;
            StickerType captured = tab.category;
            tab.button.onClick.AddListener(() => SelectCategory(captured));
        }

        // ページビューの選択通知を受ける（クリック＝説明のみ／ドロップ＝着せ替え）
        if (pageView != null) {
            pageView.OnSelected = OnItemSelected;
            pageView.OnEquipRequested = OnItemEquip;
            pageView.OnHoverExit = ShowEquippedDescription;   // カーソルを離したら着用中の説明へ戻す
        }

        // 所持シールが増減したら現在カテゴリを再表示
        if (StickerInventory.Instance != null) {
            StickerInventory.Instance.onStickerAdded.AddListener(HandleInventoryChanged);
            StickerInventory.Instance.onStickerRemoved.AddListener(HandleInventoryChanged);
        }

        UpdateTabAppearance();
    }

    // 所持シール増減の通知ハンドラ（購読解除できるよう名前付きにする）
    private void HandleInventoryChanged(StickerData _) { if (isOpen) RefreshCategory(); }

    void OnDestroy() {
        // StickerInventory は DontDestroyOnLoad で持続する。解除しないと
        // ステージ再入のたび破棄済み UI へ RefreshCategory が飛んで例外＋購読蓄積になる
        if (StickerInventory.Instance != null) {
            StickerInventory.Instance.onStickerAdded.RemoveListener(HandleInventoryChanged);
            StickerInventory.Instance.onStickerRemoved.RemoveListener(HandleInventoryChanged);
        }
    }

    /**=======================================================================
      更新処理：TABで開閉（常にアクティブなので止まらない）
    =======================================================================**/
    void Update() {
        // ESC: 開いているシール帳を閉じる (#1 メニューESCで閉じる)
        if (isOpen && (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)) {
            CloseBook();
            return;
        }

        bool pressed = (Keyboard.current?.tabKey.wasPressedThisFrame ?? false)
                    || (Gamepad.current?.selectButton.wasPressedThisFrame ?? false);   // View ボタン
        if (!pressed) return;

        // 冒頭演出中は開かせない (#12/#13: 冒頭動画中にTabで開くと破綻する)。
        // すでに開いている場合は閉じる操作のみ許可。
        if (!isOpen && StageIntroSequence.InProgress) return;

        ToggleBook();
    }

    // UI表示中のプレイヤー位置固定。Animator 適用後 (LateUpdate) に上書きして効かせる。
    void LateUpdate() {
        if (pinPlayer && player != null) player.transform.position = pinnedPlayerPos;
    }

    /**=======================================================================
      シール帳の開閉
    =======================================================================**/
    private void ToggleBook() {
        if (isOpen && lockOpen) return;   // ロック中（チュートリアル）は閉じさせない
        SetOpen(!isOpen);
    }

    /// <summary>シール帳の開閉を明示的に設定する（演出から直接開く場合にも使う）</summary>
    public void SetOpen(bool open) {
        isOpen = open;
        if (bookPanel != null) bookPanel.SetActive(isOpen);

        // プレイヤー操作の有効・無効を切り替え（SetUIMode で入力のみ止める。
        // enabled=false や applyRootMotion=false は Generic リグが浮くため使わない）
        if (player != null) player.SetUIMode(isOpen);
        else Debug.LogWarning("[StickerBookUI] player が未設定です。");

        if (cameraController != null) cameraController.enabled = !isOpen;

        pinPlayer = isOpen && player != null;
        if (pinPlayer) pinnedPlayerPos = player.transform.position;

        if (isOpen) SelectCategory(currentCategory);

        // 右ページのピンクパネルに3Dプレイヤーを描画（開いている間だけ）
        if (playerViewer != null) {
            if (isOpen) playerViewer.Activate();
            else        playerViewer.Deactivate();
        }

        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible   = isOpen;

        PlayUiOneShot(isOpen ? openSE : closeSE);
        UpdateOpenLoopSE();
    }

    /// <summary>本の開閉など、UIの一発SEを2Dで鳴らす。</summary>
    private void PlayUiOneShot(AudioClip clip) {
        if (clip == null) return;
        if (uiOneShotSource == null) {
            uiOneShotSource = gameObject.AddComponent<AudioSource>();
            uiOneShotSource.playOnAwake = false;
            uiOneShotSource.spatialBlend = 0f;
        }
        uiOneShotSource.PlayOneShot(clip, openCloseVolume);
    }

    /**=======================================================================
      開き中ループSEの再生/停止
    =======================================================================**/
    private void UpdateOpenLoopSE() {
        if (openLoopSE == null) return;
        if (openLoopSource == null) {
            openLoopSource = gameObject.AddComponent<AudioSource>();
            openLoopSource.playOnAwake = false;
            openLoopSource.loop = true;
            openLoopSource.spatialBlend = 0f;
            openLoopSource.clip = openLoopSE;
        }
        openLoopSource.volume = openLoopVolume;
        if (isOpen) { if (!openLoopSource.isPlaying) openLoopSource.Play(); }
        else openLoopSource.Stop();
    }

    /**=======================================================================
      カテゴリタブ選択
    =======================================================================**/
    public void SelectCategory(StickerType type) {
        currentCategory = type;
        UpdateTabAppearance();
        RefreshCategory();
    }

    /**=======================================================================
      現在カテゴリの内容をページビューへ流し込む
    =======================================================================**/
    private void RefreshCategory() {
        List<BookItem> items = BuildItems(currentCategory);
        bool useHanger = (currentCategory == StickerType.Clothes) && useHangerForClothes;

        if (pageView != null) pageView.Show(items, useHanger);

        // CHANGE は「いま着ている服」を初期選択（説明表示＋ハイライト）。
        // ここでは装備を変更しない（開いた瞬間に見た目が勝手に戻らないように）。
        if (currentCategory == StickerType.Clothes && items.Count > 0) {
            int equipped = FindEquippedIndex(items);
            if (equipped < 0) equipped = 0;
            SetDescription(items[equipped]);
            if (pageView != null) pageView.SetEquippedIndex(equipped);
        } else {
            SetDescription(null);   // ITEM / SKILL は選択なし＝空欄
        }
    }

    /**=======================================================================
      説明を「いま着ている服」の説明へ戻す（Clothes 以外は空）。
      ホバー解除時に使う。ページは再構築せず、右ページの説明だけ差し替える。
    =======================================================================**/
    private void ShowEquippedDescription() {
        if (currentCategory == StickerType.Clothes) {
            List<BookItem> items = BuildItems(currentCategory);
            if (items.Count > 0) {
                int equipped = FindEquippedIndex(items);
                if (equipped < 0) equipped = 0;
                SetDescription(items[equipped]);
                return;
            }
        }
        SetDescription(null);   // ITEM / SKILL などは選択なし＝空欄へ
    }

    /**=======================================================================
      いま着ている服がカタログの何番目かを返す（見つからなければ -1）
    =======================================================================**/
    private int FindEquippedIndex(List<BookItem> items) {
        GameObject cur = (modelSwitcher != null) ? modelSwitcher.CurrentClothPrefab : null;
        if (cur == null) return -1;
        for (int i = 0; i < items.Count; i++)
            if (items[i].equipPrefab == cur) return i;
        return -1;
    }

    /**=======================================================================
      カテゴリ→表示アイテム一覧を作る
        Clothes : 衣装カタログ（固定）
        その他  : 所持シール（インベントリ）
    =======================================================================**/
    private List<BookItem> BuildItems(StickerType type) {
        var list = new List<BookItem>();

        if (type == StickerType.Clothes) {
            foreach (var c in costumeCatalog) {
                if (c == null || !IsUnlocked(c)) continue;   // 未解放の服は表示しない
                list.Add(new BookItem {
                    sprite      = c.sprite != null ? c.sprite
                                : (c.stickerData != null ? StickerInventory.Instance?.GetDisplaySprite(c.stickerData) : null),
                    name        = c.displayName,
                    description = c.description,
                    data        = c.stickerData,
                    equipPrefab = c.equipPrefab != null ? c.equipPrefab
                                : (c.stickerData != null ? c.stickerData.equipPrefab : null),
                    descriptionSprite = c.descriptionSprite != null ? c.descriptionSprite
                                : (c.stickerData != null ? c.stickerData.descriptionSprite : null)
                });
            }
            return list;
        }

        // ITEM / SKILL / その他：所持シール
        if (StickerInventory.Instance != null) {
            foreach (var s in StickerInventory.Instance.GetStickers(type)) {
                if (s == null) continue;
                list.Add(new BookItem {
                    sprite      = StickerInventory.Instance.GetDisplaySprite(s),
                    name        = s.stickerName,
                    description = s.description,
                    data        = s,
                    equipPrefab = s.equipPrefab,
                    descriptionSprite = s.descriptionSprite
                });
            }
        }
        return list;
    }

    /**=======================================================================
      アイテム選択時：説明テキストを更新する
    =======================================================================**/
    // クリック時：説明の更新のみ。着せ替えはしない（D&D専用）。
    private void OnItemSelected(int index, BookItem item) {
        SetDescription(item);
    }

    // ドロップ時（右のプレイヤーへD&D）：実際に着せ替える。
    private void OnItemEquip(int index, BookItem item) {
        SetDescription(item);
        if (currentCategory != StickerType.Clothes || item.equipPrefab == null) return;
        if (modelSwitcher == null) {
            Debug.LogWarning("[StickerBookUI] PlayerModelSwitcher 未設定のため着せ替えできません。");
            return;
        }
        if (item.equipPrefab != modelSwitcher.CurrentClothPrefab) {
            modelSwitcher.EquipCloth(item.equipPrefab);
            if (pageView != null) pageView.SetEquippedIndex(index);
            // 着せ替えで増えた衣装メッシュもプレビューレイヤーへ追従させる
            if (playerViewer != null) playerViewer.RefreshPreviewLayer();
        }
    }

    private void SetDescription(BookItem? item) {
        Sprite descSprite = item.HasValue ? item.Value.descriptionSprite : null;

        // 説明画像が設定されていれば画像で表示し、テキストは隠す（画像優先）。
        // 画像が無いアイテム（未設定）は従来どおりテキストにフォールバックする。
        if (descriptionImage != null) {
            descriptionImage.sprite  = descSprite;
            descriptionImage.enabled = (descSprite != null);
        }

        if (descriptionText == null) return;
        if (descSprite != null) {
            descriptionText.text = "";              // 画像使用時はテキストを消す
        } else if (item == null) {
            descriptionText.text = emptyDescription;
        } else {
            BookItem it = item.Value;
            string body = string.IsNullOrEmpty(it.description) ? "" : ("\n" + it.description);
            descriptionText.text = it.name + body;
        }
    }

    /**=======================================================================
      タブの見た目を現在カテゴリに合わせて更新する（on/off スプライト切替）
    =======================================================================**/
    private void UpdateTabAppearance() {
        foreach (var tab in tabs) {
            if (tab == null || tab.image == null) continue;
            bool active = (tab.category == currentCategory);
            Sprite sp = active ? tab.onSprite : tab.offSprite;
            if (sp != null) tab.image.sprite = sp;
        }
    }
}
