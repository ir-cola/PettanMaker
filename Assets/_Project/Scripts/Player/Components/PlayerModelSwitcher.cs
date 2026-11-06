/**===========================================================================

 File   : プレイヤー服切り替えコンポーネント [PlayerModelSwitcher.cs]

 Author : Hayato Otsuki
 Date   : 2026/5/29
 -----------------------------------------------------------------------------
 服シール (Cloth_Default など) をプレイヤーに「着せる」(モジュラーキャラ方式)。
 服 FBX はプレイヤーと同一リグにスキニング済みなので、服 prefab を子に置くのでは
 なく、服の SkinnedMeshRenderer を共有骨格へ再バインドして装着する。こうすると
 服も本体と同じ Animator に追従する。素体(肌)を残すため冗長な肌メッシュ
 (skipClothMeshNames) は装着せず、服が覆う素体メッシュは貫通防止のため非表示にする
 (clothHideRules)。
 ===========================================================================**/

using System.Collections.Generic;
using UnityEngine;

public class PlayerModelSwitcher : MonoBehaviour {

    /// <summary>いま着ている服 prefab (シール帳 UI が装備状態の表示・重複装着回避に使う)。未装着なら null。</summary>
    public GameObject CurrentClothPrefab { get; private set; }

    [Header("デフォルト服")]
    [Tooltip("ゲーム開始時に自動で着せる服 prefab/FBX (Cloth_Default)")]
    [SerializeField] private GameObject defaultClothPrefab;

    [Header("骨格")]
    [Tooltip("プレイヤー共有骨格の根ボーン名 (通常 'Root')。自身の配下から検索")]
    [SerializeField] private string skeletonRootName = "Root";

    [Header("ロード時プリキャッシュ")]
    [Tooltip("起動時に一度着せて凹ませ計算(メッシュ判定)を先に済ませ、以後の着せ替えを軽くする服。" +
             "デフォルト服＋シール帳CHANGEカタログの全服は自動対象。ここは追加の手動指定(任意)。" +
             "ロード画面(SkinInset.IsBusy)で覆われる。")]
    [SerializeField] private GameObject[] precacheCloths;
    [Tooltip("プリキャッシュを1着あたり最大この秒数まで待つ(凹ませ完了待ち)。" +
             "別スレッド化で通常は一瞬。遅い環境の保険。")]
    [SerializeField] private float precachePerClothTimeout = 12f;
    [Tooltip("プリキャッシュ全体の絶対上限秒(必ずこの時間で切り上げてロード画面を解放)。" +
             "服が増えても全種類焼けるよう十分大きく。")]
    [SerializeField] private float precacheMaxSeconds = 25f;

    [Header("服から除外するメッシュ")]
    [Tooltip("装着しない服側メッシュの名前。素体(肌)を残して隙間から見せるため、" +
             "服が持つ冗長な肌メッシュ (既定 \"Body\") は装着しない。大文字小文字は無視。")]
    [SerializeField] private string[] skipClothMeshNames = { "Body" };

    [Header("服が覆う素体メッシュを隠す")]
    [Tooltip("素体と服がほぼ同位置で重なり貫通するのを防ぐため、各服が覆う素体の" +
             "レンダラー (肌の胴体など) を非表示にする規則。服を脱ぐ/着替えると再表示。\n" +
             "素体メッシュ名は Maya 既定名 (pCylinder5 等) で分かりにくいので、" +
             "logBodyMeshNamesOnStart を ON にすると起動時に一覧をログ出力する。")]
    [SerializeField] private ClothHideRule[] clothHideRules;

    [Tooltip("起動時に素体メッシュ (レンダラー) 名の一覧を Debug.Log へ出す。" +
             "clothHideRules に入れる名前を調べる用。")]
    [SerializeField] private bool logBodyMeshNamesOnStart = true;

    /// <summary>服 1 着 → その服を着たとき隠す素体メッシュ名の対応。</summary>
    [System.Serializable]
    private struct ClothHideRule {
        [Tooltip("対象の服 prefab/FBX (EquipCloth に渡すものと同じ参照)")]
        public GameObject clothPrefab;
        [Tooltip("この服を着たとき非表示にする素体メッシュ (レンダラー) 名。完全一致・大文字小文字無視")]
        public string[] hideBodyMeshNames;
    }

    private PlayerPasteManager pasteManager;
    private Transform skeletonRoot;
    private Transform clothContainer;                                    // 服メッシュをまとめる子
    private Dictionary<string, Transform> coreBoneMap;                   // 核骨格 名前→Transform (一度構築)
    private readonly List<GameObject> equippedObjects = new List<GameObject>(); // 着脱で破棄する対象
    private Renderer[] bodyRenderers;                                    // 素体のレンダラー (Awake で一度スナップ。Skinned/Mesh 両対応)
    private readonly List<Renderer> hiddenBodyRenderers = new List<Renderer>(); // 服で隠した素体 (着替えで戻す)

    private void Awake() {
        pasteManager = GetComponent<PlayerPasteManager>();
        BuildCoreBoneMap();
        SnapshotBodyRenderers(); // 服を着せる前 = この時点の SMR が素体
    }

    private void Start() {
        if (defaultClothPrefab != null) EquipCloth(defaultClothPrefab);
        // 全服のプリキャッシュはロード画面の裏(カメラ停止中＝着替えは不可視)で一巡して行う。
        // PushHold でロード画面をプリキャッシュ完了まで保持するのでイントロへ漏れず、
        // ゲーム中の着替えは即時(凹ませ済みキャッシュを適用するだけ)になる。
        StartCoroutine(PrecacheRoutine());
    }

    // プリキャッシュ対象を「全種類の服」で構築する: デフォルト服 ＋ 手動 precacheCloths ＋
    // シール帳CHANGEカタログの全服(=プレイヤーが着られる全服)。重複はプレハブ参照で除外。
    // カタログに服を足すだけで自動でプリキャッシュ対象になる(手動同期不要)。
    private List<GameObject> BuildPrecacheList() {
        var seen = new HashSet<int>();
        var result = new List<GameObject>();
        void Add(GameObject g) {
            if (g == null) return;
            if (!seen.Add(g.GetInstanceID())) return;   // 同一プレハブ参照は一度だけ
            result.Add(g);
        }
        Add(defaultClothPrefab);
        if (precacheCloths != null) foreach (var c in precacheCloths) Add(c);
        var book = FindObjectOfType<StickerBookUI>(true);   // CHANGEカタログ = 全着せ替え候補
        if (book != null) foreach (var p in book.EnumerateClothPrefabs()) Add(p);
        return result;
    }

    // ロード時に各服を一度着せて凹ませ計算をキャッシュへ焼いておく(以後の着せ替えが即時に)。
    // ロード画面が SkinInset.IsBusy を見て覆っている間に一巡する。最後にデフォルトへ戻す。
    private System.Collections.IEnumerator PrecacheRoutine() {
        yield return null;                              // 素体/デフォルトの初期化を待つ
        // このルーチンは全服を順に着せ替えるサイクルを見せないため、その間ずっと
        // SetRenderingHidden(true) でプレイヤーを丸ごと消す。つまりロード画面が
        // 覆っている事が大前提。覆いの無いまま走らせる (SceneLoader を通さず
        // シーンを直接再生した等) と、プレイヤーが数秒間消えたように見えてしまう。
        if (!SceneLoader.IsActive) {
            Debug.Log("[PlayerModelSwitcher] ロード画面が無いのでプリキャッシュを省略した " +
                      "(着せ替えはその場でライブ計算される)");
            yield break;
        }
        var skin = GetComponent<SkinInset>();
        var list = BuildPrecacheList();                 // 全種類の服(デフォルト＋カタログ＋手動)
        float startT = Time.unscaledTime;
        int cached = 0;
        SkinInset.PushHold();       // 全服のプリキャッシュ完了までロード画面を保持(イントロへ漏らさない)
        SetRenderingHidden(true);   // プリキャッシュ中はプレイヤーを完全非表示＝着替えのサイクルを一切見せない
        try {
            // 全服を1着ずつ着せて凹ませ計算をキャッシュへ焼く。服ごとに凹み具合が違うので全種類必要。
            foreach (var c in list) {
                if (c == null) continue;
                if (Time.unscaledTime - startT > precacheMaxSeconds) break;   // 絶対上限(保険。通常は届かない)
                // 服ごとに「実際にキャッシュへ焼けた」ことを確認してから次へ進む。まれなタイミング
                // 競合で焼け損ねると、その服だけ実行時に毎回ライブ凹ませ計算になり、計算中は前の服の
                // 凹みが残って見える(例: デフォルトからアリスに着替えると素体がデフォルトの体型のまま)。
                // 焼け損ねたら着せ直して確実に焼き切る(最大3回)。
                for (int attempt = 0; attempt < 3; attempt++) {
                    EquipCloth(c);
                    SetRenderingHidden(true);           // 着替えで増えた服レンダラーも隠す
                    yield return null;
                    float ct = Time.unscaledTime;
                    while (skin != null && skin.IsRecomputing && Time.unscaledTime - ct < precachePerClothTimeout)
                        yield return null;              // この服の凹ませ完了(=キャッシュ確定)を待つ
                    if (skin == null || SkinInset.IsCached(c)) break;               // 焼けたら次の服へ
                    if (Time.unscaledTime - startT > precacheMaxSeconds) break;     // 絶対上限で打ち切り
                }
                cached++;
            }
            // 見た目はデフォルトへ(既にキャッシュ済みなので即時)
            if (defaultClothPrefab != null) EquipCloth(defaultClothPrefab);
            SetRenderingHidden(true);                   // デフォルト服も隠したまま(素体だけ描画される不具合を防ぐ)
            yield return null;
            float dt = Time.unscaledTime;
            while (skin != null && skin.IsRecomputing && Time.unscaledTime - dt < precachePerClothTimeout)
                yield return null;
        } finally {
            // 必ずデフォルト装備＋表示へ戻す(万一の中断でも非表示のまま固まらせない)
            if (defaultClothPrefab != null && CurrentClothPrefab != defaultClothPrefab) EquipCloth(defaultClothPrefab);
            SetRenderingHidden(false);  // 最後にまとめて表示(素体＋デフォルト服が揃った状態で一度に)
            SkinInset.PopHold();        // その後ロード画面を解放してよい
            Debug.Log("[PlayerModelSwitcher] 全服プリキャッシュ完了: " + cached + "/" + list.Count + " 着");
        }
    }

    // forceRenderingOff は enabled/hide規則に影響せず描画だけ止める(BakeMesh は動くのでキャッシュ計算は継続)。
    private void SetRenderingHidden(bool hidden) {
        var rends = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null) rends[i].forceRenderingOff = hidden;
    }

    /// <summary>PlayerViewer3D 互換: 服シールドロップ時に呼ばれる。</summary>
    public void SwitchModel(GameObject clothPrefab) => EquipCloth(clothPrefab);

    /// <summary>デフォルト服に戻す。</summary>
    public void ResetToDefault() {
        if (defaultClothPrefab != null) EquipCloth(defaultClothPrefab);
        else UnequipCurrent();
    }

    /**=======================================================================
      服を着せる: 服 prefab の SMR をプレイヤー共有骨格に再バインドして装着。
    =======================================================================**/
    public void EquipCloth(GameObject clothPrefab) {
        if (clothPrefab == null) {
            Debug.LogWarning("[PlayerModelSwitcher] clothPrefab が null です。");
            return;
        }
        if (coreBoneMap == null || skeletonRoot == null) BuildCoreBoneMap();
        if (skeletonRoot == null) {
            Debug.LogWarning($"[PlayerModelSwitcher] 骨格根 '{skeletonRootName}' が見つかりません。装着不可。");
            return;
        }

        // 前回装着分を除去 (Destroy は遅延だが、boneMap は核のコピーから作るので干渉しない)
        UnequipCurrent();

        // 今回用ボーン辞書 = 核骨格のコピー (服の追加ボーンをここへ足す)
        var boneMap = new Dictionary<string, Transform>(coreBoneMap);

        // 服を一時生成
        GameObject clothInstance = Instantiate(clothPrefab);
        clothInstance.name = clothPrefab.name;

        // 服固有の追加ボーンをプレイヤー骨格へ移植 (服側の骨格根から辿る)
        Transform clothSkeleton = FindDeep(clothInstance.transform, skeletonRootName);
        if (clothSkeleton != null) TransplantExtraBones(clothSkeleton, boneMap);

        // 服メッシュのコンテナ
        EnsureClothContainer();

        // 各 SMR を共有骨格へ再バインドして装着。素体(肌)を残すため、服側の冗長な
        // 肌メッシュ (skipClothMeshNames) はスキップ = 装着しない (残骸と一緒に破棄)。
        SkinnedMeshRenderer[] smrs = clothInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int equipped = 0, skipped = 0;
        foreach (SkinnedMeshRenderer smr in smrs) {
            if (IsSkippedClothMesh(smr.gameObject.name)) { skipped++; continue; }
            RebindSMR(smr, boneMap);
            smr.transform.SetParent(clothContainer, worldPositionStays: false);
            smr.transform.localPosition = Vector3.zero;
            smr.transform.localRotation = Quaternion.identity;
            smr.transform.localScale    = Vector3.one;
            smr.updateWhenOffscreen = true; // 再バインド後のバウンズずれによるカリング防止
            equippedObjects.Add(smr.gameObject);
            equipped++;
        }

        // 残骸 (複製された核骨格 + スキップした肌メッシュ + ラッパー) を破棄
        Destroy(clothInstance);

        // この服が覆う素体メッシュを非表示に (貫通防止)。脱ぐ/着替え時に戻す。
        int hidden = ApplyBodyHideRule(clothPrefab);

        CurrentClothPrefab = clothPrefab;  // 装備状態を記録 (シール帳 UI が参照)

        pasteManager?.RefreshRenderers(); // 服にもシールを貼れるよう SMR を再検索
        GetComponent<SkinInset>()?.Recompute(); // 素体の凹ませ(貫通防止)を今の服で再計算
        GetComponent<SkirtDynamicBone>()?.Rebuild(); // スカート物理を今の骨で再初期化(着せ替え時のズレ防止)
        Debug.Log($"[PlayerModelSwitcher] 着せ替え完了: {clothPrefab.name} " +
                  $"(装着 {equipped} / スキップ {skipped} / 素体非表示 {hidden})");
    }

    /**=======================================================================
      ヘルパー
    =======================================================================**/

    private void BuildCoreBoneMap() {
        skeletonRoot = FindBone(skeletonRootName);
        if (skeletonRoot == null) return;
        coreBoneMap = new Dictionary<string, Transform>();
        CollectBones(skeletonRoot, coreBoneMap);
    }

    // 装着しない服側メッシュか? (素体の肌を残すための冗長な肌メッシュなど)
    private bool IsSkippedClothMesh(string meshName) {
        if (skipClothMeshNames == null) return false;
        for (int i = 0; i < skipClothMeshNames.Length; i++) {
            if (!string.IsNullOrEmpty(skipClothMeshNames[i]) &&
                string.Equals(meshName, skipClothMeshNames[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private void UnequipCurrent() {
        for (int i = 0; i < equippedObjects.Count; i++)
            if (equippedObjects[i] != null) Destroy(equippedObjects[i]);
        equippedObjects.Clear();
        RestoreHiddenBodyRenderers(); // 前の服で隠した素体を戻す
        CurrentClothPrefab = null;    // 脱いだ状態 (EquipCloth なら直後に上書きされる)
    }

    /**=======================================================================
      素体メッシュの表示制御 (服による貫通防止)
    =======================================================================**/

    // 服を着せる前のレンダラー (= 素体) を控える。ClothLayer はまだ無いので素体のみ。
    // 素体が SkinnedMeshRenderer か MeshRenderer かは資産依存なので Renderer 全般を捕捉。
    private void SnapshotBodyRenderers() {
        bodyRenderers = GetComponentsInChildren<Renderer>(true);
        if (!logBodyMeshNamesOnStart || bodyRenderers == null) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("[PlayerModelSwitcher] 素体メッシュ (レンダラー) 一覧 ")
          .Append(bodyRenderers.Length).Append(" 件:");
        for (int i = 0; i < bodyRenderers.Length; i++)
            sb.Append("\n  - ").Append(bodyRenderers[i].name);
        Debug.Log(sb.ToString());
    }

    // clothPrefab に対応する規則の素体メッシュを非表示にする。隠した数を返す。
    private int ApplyBodyHideRule(GameObject clothPrefab) {
        if (clothHideRules == null || bodyRenderers == null) return 0;
        string[] names = null;
        for (int i = 0; i < clothHideRules.Length; i++) {
            if (IsSameCloth(clothHideRules[i].clothPrefab, clothPrefab)) {
                names = clothHideRules[i].hideBodyMeshNames;
                break;
            }
        }
        if (names == null || names.Length == 0) return 0;

        int hidden = 0;
        for (int i = 0; i < bodyRenderers.Length; i++) {
            Renderer r = bodyRenderers[i];
            if (r == null || !r.enabled || !MatchesAny(r.name, names)) continue;
            r.enabled = false;
            hiddenBodyRenderers.Add(r);
            hidden++;
        }
        return hidden;
    }

    private void RestoreHiddenBodyRenderers() {
        for (int i = 0; i < hiddenBodyRenderers.Count; i++)
            if (hiddenBodyRenderers[i] != null) hiddenBodyRenderers[i].enabled = true;
        hiddenBodyRenderers.Clear();
    }

    // 規則の服 prefab と装着 prefab を同一視 (参照一致、ダメなら名前一致)
    private static bool IsSameCloth(GameObject a, GameObject b) {
        if (a == null || b == null) return false;
        if (a == b) return true;
        return string.Equals(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAny(string name, string[] names) {
        for (int i = 0; i < names.Length; i++)
            if (!string.IsNullOrEmpty(names[i]) &&
                string.Equals(name, names[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private void EnsureClothContainer() {
        if (clothContainer != null) return;
        clothContainer = new GameObject("ClothLayer").transform;
        clothContainer.SetParent(transform, worldPositionStays: false);
        clothContainer.localPosition = Vector3.zero;
        clothContainer.localRotation = Quaternion.identity;
        clothContainer.localScale    = Vector3.one;
    }

    // SMR の bones[]/rootBone を共有骨格の同名ボーンへ貼り替え
    private void RebindSMR(SkinnedMeshRenderer smr, Dictionary<string, Transform> boneMap) {
        Transform[] oldBones = smr.bones;
        Transform[] newBones = new Transform[oldBones.Length];
        for (int i = 0; i < oldBones.Length; i++) {
            if (oldBones[i] != null && TryBone(boneMap, oldBones[i].name, out Transform b))
                newBones[i] = b;
            else
                newBones[i] = oldBones[i]; // 同名が見つからなければそのまま (基本起きない)
        }
        smr.bones = newBones;

        if (smr.rootBone != null && TryBone(boneMap, smr.rootBone.name, out Transform rb))
            smr.rootBone = rb;
        else
            smr.rootBone = skeletonRoot;
    }

    // 服骨格を上から辿り、プレイヤーに無いボーンを「親名一致」でプレイヤー骨格へ移植
    private void TransplantExtraBones(Transform clothBone, Dictionary<string, Transform> boneMap) {
        if (!TryBone(boneMap, clothBone.name, out _)) {          // 下線無視でも本体骨格に無い = 追加ボーン
            Transform clothParent = clothBone.parent;
            if (clothParent != null && TryBone(boneMap, clothParent.name, out Transform playerParent)) {
                clothBone.SetParent(playerParent, worldPositionStays: false); // bind ローカルを保持
                CollectBones(clothBone, boneMap);            // 追加 subtree を辞書登録
                equippedObjects.Add(clothBone.gameObject);   // 着脱で破棄
                return;                                       // この subtree は移植済み
            }
            // 親が見つからない追加ボーンはスキップ (まれ)
        }
        // 核ボーン or 未対応ボーン: 子を辿る (反復中の再ペアレントに備えコピー)
        var children = new List<Transform>(clothBone.childCount);
        for (int i = 0; i < clothBone.childCount; i++) children.Add(clothBone.GetChild(i));
        foreach (Transform c in children) TransplantExtraBones(c, boneMap);
    }

    private static void CollectBones(Transform root, Dictionary<string, Transform> map) {
        if (!map.ContainsKey(root.name)) map[root.name] = root;
        string norm = Norm(root.name);                          // 下線無視キーも登録
        if (!map.ContainsKey(norm)) map[norm] = root;
        for (int i = 0; i < root.childCount; i++) CollectBones(root.GetChild(i), map);
    }

    // 下線と大小文字を無視した名前キー。服=LegL/SkirtBackCenter1 と 本体=Leg_L/Skirt_Back_Center_1 を一致させる。
    private static string Norm(string s) => s.Replace("_", "").ToLowerInvariant();

    private static bool TryBone(Dictionary<string, Transform> map, string name, out Transform t) {
        if (map.TryGetValue(name, out t)) return true;          // まず完全一致
        return map.TryGetValue(Norm(name), out t);              // 次に下線無視
    }

    private Transform FindBone(string boneName) {
        Transform t = transform.Find(boneName);             // 直下 (Player_Model 直下の Root)
        return t != null ? t : FindDeep(transform, boneName); // 深い場合は再帰
    }

    private static Transform FindDeep(Transform root, string name) {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
