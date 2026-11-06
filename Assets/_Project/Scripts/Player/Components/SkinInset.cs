/**===========================================================================

 File   : 素体インセット [SkinInset.cs]

 Author : Hayato Otsuki
 Date   : 2026/6/30
 -----------------------------------------------------------------------------

   素体(全身1枚メッシュ)を法線方向に内側へ凹ませ、上の服から素肌が突き抜ける
   のを防ぐ。着せ替え後の服メッシュをワールドにベイクし、各素体頂点について服
   サーフェスへの最近点距離・面内/裾外の別・貫通の符号を測り、「服に覆われた
   頂点」だけを凹ませる(裾の外へはみ出た素肌や奥に隠れた素肌は凹ませない)。
   起動時と着せ替え(EquipCloth)ごとに素の複製メッシュから再計算するので、服を
   替えても前の凹みが残らず多重に凹むこともない(PlayerModelSwitcher が Recompute を呼ぶ)。

 ===========================================================================**/
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SkinInset : MonoBehaviour {

    [Tooltip("内側へ凹ませる素体メッシュ名 (全身メッシュ)")]
    [SerializeField] private string targetMeshName = "pPlane10";

    [Tooltip("法線方向に内側へ縮める最大量(m)。覆われた部位のみに適用される")]
    [SerializeField] private float inset = 0.04f;

    /** ボーン名(前方一致)で指定する部位別の凹ませ倍率 */
    [System.Serializable]
    public class BoneInsetOverride {
        [Tooltip("対象ボーン名の前方一致 (大文字小文字無視)。'Leg_' で Leg_L/Leg_R 両方に効く")]
        public string bonePrefix = "";
        [Tooltip("この部位の凹ませ倍率 (0=凹ませない, 1=標準, 2=倍)")]
        [Range(0f, 2f)] public float multiplier = 1f;
    }

    [Header("部位別の凹ませ倍率 (ボーンウェイトでブレンド)")]
    [Tooltip("指定ボーンにウェイトされた頂点ほど、その倍率で凹ませ量が変わる。\n" +
             "太もも=Leg_ / ふくらはぎ=Knee_ / 足=Foot_。未指定の部位は倍率1。")]
    [SerializeField] private BoneInsetOverride[] boneOverrides = {
        new BoneInsetOverride { bonePrefix = "Leg_",  multiplier = 0.25f },   // 太もも: ほぼ凹ませない
        new BoneInsetOverride { bonePrefix = "Knee_", multiplier = 1f },      // ふくらはぎ: 標準
    };

    [Tooltip("服サーフェスより内側にこの深さまでの素肌は完全に凹ませる(m)。" +
             "面上〜外側(貫通)は深さに依らず常に完全に凹ませる。")]
    [SerializeField] private float coverRadius = 0.03f;

    [Tooltip("覆い判定(垂直)のフェード幅(m)。これより深く服の内側にある素肌へ" +
             "滑らかに0へ近づける(奥に隠れた素肌は凹ませない)。")]
    [SerializeField] private float coverBlend = 0.025f;

    [Tooltip("服の裾(縁)ぎわの横フェード幅(m)。服フットプリントから横に" +
             "これだけはみ出すと素肌扱い。小さいほど隙間の境目がくっきり判定される。")]
    [SerializeField] private float hemFeather = 0.012f;

    [Tooltip("服の法線が内向き(反転)しているFBXの場合にON。貫通側の判定が反転する。")]
    [SerializeField] private bool flipClothNormals = false;

    [Tooltip("着せ替え完了を待つ最大フレーム数")]
    [SerializeField] private int maxWaitFrames = 12;

    [Tooltip("凹ませ計算に1フレームで費やす最大ミリ秒。小さいほどロード中が滑らかになる")]
    [SerializeField] private float insetMillisPerFrame = 1.5f;

    private Mesh restMesh;          // 素(未加工)の素体メッシュ。毎回ここから複製して凹ませる
    private Mesh generatedMesh;     // 現在適用中の凹ませ複製(再計算時に破棄)
    private GameObject generatedClothKey;   // generatedMesh がどの服の凹みか(別の服の凹みを流用表示しないため)
    private Coroutine routine;      // 実行中の着せ替え待ちコルーチン
    private System.Threading.CancellationTokenSource _cts;   // 別スレッド計算の中断用(着替え直しで前回をキャンセル)

    // ロード画面(SceneLoader)が「初期化フリーズ中か」を判定するための実行中カウンタ。
    // 凹ませ計算をフレーム分割している間 IsBusy=true を保ち、完了で false になるので、
    // ロード画面は "この重い処理が終わるまで" 覆いつつアニメを止めずに回せる。
    private static int s_pending;
    public static bool IsBusy => s_pending > 0;
    private bool _counted;          // このインスタンスが今カウンタを1つ保持しているか

    // この個体が今 凹ませ計算中か。プリキャッシュの「服ごとの完了待ち」に使う
    // (下の PushHold で IsBusy を張っている間も、個々の服の完了はこれで判定できる)。
    private bool _recomputing;
    public bool IsRecomputing => _recomputing;

    // ロード画面などが「全服のプリキャッシュ完了まで」覆い続けるための外部ホールド。
    // これを積んでいる間は IsBusy=true のままになり、ロード画面が途中で閉じない。
    private static int s_holdCount;
    /// <summary>プリキャッシュ等でロード保持中か。ロード画面はこの間、通常のタイムアウトで閉じない。</summary>
    public static bool HoldActive => s_holdCount > 0;
    public static void PushHold() { s_pending++; s_holdCount++; }
    public static void PopHold()  { if (s_pending > 0) s_pending--; if (s_holdCount > 0) s_holdCount--; }

    // 服ごとの凹ませ結果(素体メッシュ頂点)をキャッシュ。着せ替えの度に重い最近点探索を
    // 走らせず、一度計算した服は即適用する(ロード時にプリキャッシュしておけば実質ゼロコスト)。
    private static readonly Dictionary<GameObject, Vector3[]> s_cache = new Dictionary<GameObject, Vector3[]>();

    /// <summary>指定の服の凹み結果が既にキャッシュ済みか。プリキャッシュが本当に焼けたかの確認に使う
    /// (焼け損ねを検知して焼き直すため。焼けていないと実行時に毎回ライブ計算＝前の服の凹みが残る)。</summary>
    public static bool IsCached(GameObject cloth) => cloth != null && s_cache.ContainsKey(cloth);

    // >0 の間はこの値(ms)をフレーム予算に使う。ロード画面で覆われている間は大きくして
    // 一気に凹ませ、カクつく時間そのものを短縮する（通常の着せ替えは細かい予算のまま）。
    public static float HoldBudgetMs = 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetBusy() { s_pending = 0; s_holdCount = 0; HoldBudgetMs = 0f; s_cache.Clear(); }   // Play開始毎に確実にリセット

    private void Start() {
        Schedule();
    }

    /// <summary>着せ替え後に呼ぶ。素の素体メッシュから今の服に合わせて凹みを再計算する。</summary>
    public void Recompute() {
        Schedule();
    }

    private void Schedule() {
        if (inset <= 0f) return;
        if (!isActiveAndEnabled) return;
        _recomputing = true;                               // この服の計算中フラグ
        if (!_counted) { _counted = true; s_pending++; }   // 作業開始 = 覆っておくべき状態
        if (routine != null) StopCoroutine(routine);       // 前回分を止めても _counted は保持
        routine = StartCoroutine(ApplyWhenDressed());
    }

    // 作業完了/中断時にカウンタを1つ戻す(多重減算しない)
    private void EndWork() {
        _recomputing = false;
        if (_counted) { _counted = false; s_pending--; }
    }

    // キャッシュ済みの凹み結果があれば「同期で」素体へ適用する(適用したら true)。
    // 着替え反映待ちの前に確定させることで、着替え直後に前の服/デフォルトの素体形状が
    // 数フレーム残って見える(=デフォルト形状を引き継ぐ)問題を防ぐ。
    // restMesh 未設定(初回)や未キャッシュなら false を返し、通常の非同期計算へ回す。
    private bool TryApplyCache() {
        if (restMesh == null) return false;
        var switcher = GetComponent<PlayerModelSwitcher>();
        GameObject clothKey = switcher != null ? switcher.CurrentClothPrefab : null;
        if (clothKey == null) return false;
        if (!s_cache.TryGetValue(clothKey, out var cachedVerts) || cachedVerts == null
            || cachedVerts.Length != restMesh.vertexCount) return false;

        SkinnedMeshRenderer body = null;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null &&
                string.Equals(smr.name, targetMeshName, System.StringComparison.OrdinalIgnoreCase)) { body = smr; break; }
        if (body == null) return false;

        Mesh cm = Instantiate(restMesh);
        cm.vertices = cachedVerts;
        cm.RecalculateBounds();
        if (generatedMesh != null) Object.Destroy(generatedMesh);
        generatedMesh = cm;
        generatedClothKey = clothKey;   // この凹みはこの服のもの
        body.sharedMesh = cm;
        return true;
    }

    private IEnumerator ApplyWhenDressed() {
        // キャッシュ済みなら着せ替え反映(ClothLayer)を待たずに「今フレームで」即適用する。
        // 待つと、その数フレームだけ前の服/デフォルトの素体形状が残って見える(=着替え時に
        // デフォルト形状を引き継いで見える)ため、キャッシュがある服はここで確定させる。
        if (TryApplyCache()) { routine = null; EndWork(); yield break; }

        // PlayerModelSwitcher の着せ替え(ClothLayer 生成)完了を待つ
        Transform cloth = null;
        for (int f = 0; f < maxWaitFrames; f++) {
            cloth = transform.Find("ClothLayer");
            if (cloth != null && cloth.childCount > 0) break;
            yield return null;
        }
        yield return null;              // 服のスキニング反映を1フレーム待つ
        yield return DoInset();         // フレーム分割で凹ませ(ロード画面を固まらせない)
        routine = null;
        EndWork();
    }

    private IEnumerator DoInset() {
        var all = GetComponentsInChildren<SkinnedMeshRenderer>(true);

        // 素体(全身メッシュ)を探す
        SkinnedMeshRenderer body = null;
        foreach (var smr in all) {
            if (smr.sharedMesh == null) continue;
            if (string.Equals(smr.name, targetMeshName, System.StringComparison.OrdinalIgnoreCase)) { body = smr; break; }
        }
        if (body == null) { Debug.LogWarning("[SkinInset] 素体メッシュ '" + targetMeshName + "' が見つからない"); yield break; }
        if (restMesh == null && !body.sharedMesh.isReadable) {
            Debug.LogWarning("[SkinInset] '" + targetMeshName + "' は Read/Write 無効のため凹ませ不可");
            yield break;
        }
        // 初回に素(未加工)メッシュを控える。以後は毎回ここから複製するので、
        // 服を替えても凹みが多重に蓄積しない(共有資産は不変)。
        if (restMesh == null) restMesh = body.sharedMesh;

        // キャッシュ命中なら即適用して終了(重い最近点探索を丸ごとスキップ)。
        // ※初回は上の ApplyWhenDressed 冒頭では restMesh 未設定で拾えないためここで拾う。
        if (TryApplyCache()) yield break;

        // 凹み結果のキャッシュキー(計算完了後にここへ結果を焼く)
        var switcher = GetComponent<PlayerModelSwitcher>();
        GameObject clothKey = switcher != null ? switcher.CurrentClothPrefab : null;

        // 近傍探索半径。覆いフェード幅(coverRadius+coverBlend)とは別に、実際の
        // 貫通(素肌が服の外へ突き出た深さ)も拾えるよう inset の 2 倍まで広げる。
        // (coverRadius/coverBlend は垂直フェードの形にのみ使い、探索範囲は縛らない)
        float searchR = Mathf.Max(0.005f, Mathf.Max(coverRadius + coverBlend, inset * 2f));
        float cell = searchR;                       // 3x3x3 近傍で searchR 以内を網羅するため cell>=searchR
        float feather = Mathf.Max(1e-4f, hemFeather);
        float nSign = flipClothNormals ? -1f : 1f;

        // --- 1) 服メッシュを「三角形スープ(ワールド)」にベイク(メインスレッド: BakeMesh は Unity API) ---
        //     グリッド構築と最近点探索は重いので後段で別スレッドへ回す。ここは配列を用意するだけ。
        var triA = new List<Vector3>(); var triB = new List<Vector3>(); var triC = new List<Vector3>();
        var nrmA = new List<Vector3>(); var nrmB = new List<Vector3>(); var nrmC = new List<Vector3>();
        var bake = new Mesh();

        foreach (var smr in all) {
            if (smr == body || smr.sharedMesh == null) continue;
            if (!IsCloth(smr)) continue;            // ClothLayer 配下のみ服とみなす
            smr.BakeMesh(bake);
            Vector3[] cv = bake.vertices;
            Vector3[] cn = bake.normals;
            int[] ct = bake.triangles;
            if (cv.Length == 0 || ct.Length == 0) continue;
            bool hasN = cn != null && cn.Length == cv.Length;
            Matrix4x4 l2w = smr.localToWorldMatrix;

            // 頂点/法線をワールドへ(一括変換)
            Vector3[] wv = new Vector3[cv.Length];
            Vector3[] wn = hasN ? new Vector3[cv.Length] : null;
            for (int i = 0; i < cv.Length; i++) {
                wv[i] = l2w.MultiplyPoint3x4(cv[i]);
                if (hasN) wn[i] = (nSign * l2w.MultiplyVector(cn[i])).normalized;
            }
            for (int t = 0; t + 2 < ct.Length; t += 3) {
                int i0 = ct[t], i1 = ct[t + 1], i2 = ct[t + 2];
                Vector3 a = wv[i0], b = wv[i1], c = wv[i2];
                triA.Add(a); triB.Add(b); triC.Add(c);
                if (hasN) { nrmA.Add(wn[i0]); nrmB.Add(wn[i1]); nrmC.Add(wn[i2]); }
                else {
                    Vector3 fn = (nSign * Vector3.Cross(b - a, c - a)).normalized;   // 法線欠落時は面法線
                    nrmA.Add(fn); nrmB.Add(fn); nrmC.Add(fn);
                }
            }
        }
        if (triA.Count == 0) {
            // 服が無い(全スキップ等)なら凹みを解除して素の素体へ戻す
            Object.Destroy(bake);
            if (generatedMesh != null) {
                body.sharedMesh = restMesh;
                Object.Destroy(generatedMesh);
                generatedMesh = null;
                generatedClothKey = null;
            }
            yield break;
        }

        // --- 2) 素体をベイクして頂点ワールド位置を得る(覆い判定用) ---
        // 覆い判定は必ず「素の姿勢」で行う。再計算時に前回の凹みが残ったメッシュで
        // 判定すると、凹んだ分だけ服から遠ざかり覆い判定が甘くなって徐々に痩せるため、
        // ベイク前に素メッシュへ戻す(最後に凹ませた複製を割り当て直す)。
        body.sharedMesh = restMesh;
        body.BakeMesh(bake);
        Vector3[] bw = bake.vertices;
        Matrix4x4 bl2w = body.localToWorldMatrix;
        // ベイク(素姿勢)は済んだので、計算中の見た目は「前回の凹ませ結果(無ければ素体)」へ
        // 戻す。凹ませをフレーム分割している間に素メッシュのまま描画されると、着せ替え時に
        // 一瞬素肌が服から飛び出て見えるため。完成した複製 m は最後にまとめて割り当てる。
        // 計算中の見た目: 「今と同じ服の前回凹み」なら流用してよいが、別の服の凹みを流用すると
        // 誤った凹み(例: デフォルトの体型のままアリスに見える)が残るので、素体(rest)を見せる。
        body.sharedMesh = (generatedMesh != null && generatedClothKey == clothKey) ? generatedMesh : restMesh;

        // --- 3) 素の素体メッシュ頂点を取得(複製メッシュは計算完了後の適用段で作る) ---
        //     複製を待機の前に作ると、着替え直し/破棄で待機中コルーチンが止まった際に
        //     どの Destroy 分岐も通らずネイティブメッシュがリークする。yield を挟まない
        //     適用段でだけ Instantiate すればこの窓が消える(restMesh は Read/Write 有効)。
        Vector3[] v = restMesh.vertices;
        Vector3[] n = restMesh.normals;
        if (n == null || n.Length != v.Length || bw.Length != v.Length) {
            Debug.LogWarning("[SkinInset] 法線/ベイク頂点数が不一致。中止");
            Object.Destroy(bake); yield break;
        }

        // 部位別倍率: ボーンウェイトから頂点ごとの倍率を作る (オーバーライド無しなら null)
        float[] vertMul = BuildVertexMultipliers(body, restMesh);

        // 素体頂点をワールド化(ここまでがメインスレッドの Unity API 依存部)
        Vector3[] bwWorld = new Vector3[bw.Length];
        for (int i = 0; i < bw.Length; i++) bwWorld[i] = bl2w.MultiplyPoint3x4(bw[i]);
        Object.Destroy(bake);   // ベイク用メッシュは用済み

        // --- 4) 重い「グリッド構築＋最近点探索＋凹ませ」を別スレッドで実行 ---
        //     ここから先は Unity API を一切呼ばず、配列と構造体(Vector3/Matrix4x4)の純計算のみ。
        //     ~85ms の探索がメインスレッドから外れ、着替え/プリキャッシュがカクつかなくなる。
        var tA = triA.ToArray(); var tB = triB.ToArray(); var tC = triC.ToArray();
        var nA = nrmA.ToArray(); var nB = nrmB.ToArray(); var nC = nrmC.ToArray();

        var myCts = new System.Threading.CancellationTokenSource();
        var prevCts = _cts; _cts = myCts;                 // 前回の計算があれば中断(着替え直し対策)
        if (prevCts != null) { try { prevCts.Cancel(); } catch { } }   // Dispose はしない(前タスクがまだ読む可能性)
        var token = myCts.Token;
        // パラメータはローカルへ退避(スレッドから this のフィールドを触らせない)
        float pCell = cell, pSearchR2 = searchR * searchR, pFeather = feather;
        float pInset = inset, pCoverRadius = coverRadius, pInvBlend = 1f / Mathf.Max(1e-4f, coverBlend);
        Vector3[] outV = v;                               // スレッドが直接書き換える(完了までメインは触らない)

        var task = System.Threading.Tasks.Task.Run(() =>
            ComputeInsetOffThread(bwWorld, outV, n, vertMul,
                tA, tB, tC, nA, nB, nC,
                pCell, pSearchR2, pFeather, pInset, pCoverRadius, pInvBlend, token));

        while (!task.IsCompleted) yield return null;      // 待つ間メインは自由(=カクつかない)

        if (token.IsCancellationRequested) yield break;   // 途中で着替え直された(複製未生成なので破棄不要)
        if (task.IsFaulted) {
            Debug.LogWarning("[SkinInset] 別スレッド計算で例外: " + task.Exception?.GetBaseException().Message);
            yield break;
        }

        // --- 5) 結果をメインスレッドで適用(ここは yield 無し=中断で複製メッシュが漏れない) ---
        Mesh m = Instantiate(restMesh);
        m.vertices = outV;
        m.RecalculateBounds();
        body.sharedMesh = m;
        if (generatedMesh != null) Object.Destroy(generatedMesh);   // 前回の凹ませ複製を解放
        generatedMesh = m;
        generatedClothKey = clothKey;                               // この凹みはこの服のもの

        if (clothKey != null) s_cache[clothKey] = (Vector3[])outV.Clone();   // この服の結果をキャッシュ
        if (_cts == myCts) { myCts.Dispose(); _cts = null; }        // 自分が最新なら後始末(タスク完了済=安全)
        Debug.Log("[SkinInset] 覆い部のみ凹ませ完了(別スレッド): " + outV.Length +
                  " 頂点 (服三角形 " + tA.Length + ", inset " + pInset + ")");
    }

    /**=======================================================================
      別スレッドで走る純計算部。グリッド(空間ハッシュ)構築 → 各素体頂点の最近服
      三角形を探索 → 覆われた頂点だけ法線方向へ凹ませて outV を書き換える。
      Unity API は一切呼ばない(BakeMesh/Instantiate 等はメインで済ませ配列で受け取る)。
      Mathf/Vector3/Dictionary は純関数・純データなのでスレッドセーフ。
    =======================================================================**/
    private static void ComputeInsetOffThread(
        Vector3[] bwWorld, Vector3[] outV, Vector3[] n, float[] vertMul,
        Vector3[] tA, Vector3[] tB, Vector3[] tC, Vector3[] nA, Vector3[] nB, Vector3[] nC,
        float cell, float searchR2, float feather, float inset, float coverRadius, float invBlend,
        System.Threading.CancellationToken token) {

        int nTri = tA.Length;
        var grid = new Dictionary<long, List<int>>();
        for (int idx = 0; idx < nTri; idx++) RegisterTri(grid, tA[idx], tB[idx], tC[idx], cell, idx);

        // 各素体頂点の最近服三角形探索は互いに独立 (grid は構築後 読み取り専用・outV[i] は
        // その頂点だけが書く)。頂点ループを全コアへ並列化し、凹ませ計算 (高ポリ服で数秒) を短縮する。
        // stamp (近傍の三角形重複判定) はスレッドごとに保持し、マーカーは一意な (i+1) を使う
        // (同一スレッドの stamp 配列内で i は重複しないのでクリア不要)。結果は逐次版と同一。
        System.Threading.Tasks.Parallel.For(0, outV.Length,
            () => new int[nTri],                                // スレッドローカルの stamp
            (i, state, stamp) => {
                if ((i & 1023) == 0 && token.IsCancellationRequested) state.Stop();   // 着替え直しで打ち切り
                else ComputeVertexInset(i, bwWorld, outV, n, vertMul, tA, tB, tC, nA, nB, nC,
                                        grid, stamp, cell, searchR2, feather, inset, coverRadius, invBlend);
                return stamp;
            },
            stampLocal => { });
    }

    // 素体頂点 i を、近傍セルの服三角形との最近点で「覆われていれば」法線方向へ凹ませる。
    // ComputeInsetOffThread の並列ループ本体 (頂点ごとに独立)。grid は読み取り専用、
    // stamp はスレッドローカル (マーカー = i+1 で毎クエリ一意なのでクリア不要)。
    private static void ComputeVertexInset(int i,
        Vector3[] bwWorld, Vector3[] outV, Vector3[] n, float[] vertMul,
        Vector3[] tA, Vector3[] tB, Vector3[] tC, Vector3[] nA, Vector3[] nB, Vector3[] nC,
        Dictionary<long, List<int>> grid, int[] stamp,
        float cell, float searchR2, float feather, float inset, float coverRadius, float invBlend) {

        Vector3 w = bwWorld[i];
        int queryId = i + 1;                         // 同一スレッド内で頂点ごとに一意 (stamp クリア不要)

        int cx = Mathf.FloorToInt(w.x / cell), cy = Mathf.FloorToInt(w.y / cell), cz = Mathf.FloorToInt(w.z / cell);
        float bestD2 = searchR2; int bestTri = -1;
        Vector3 bestCp = Vector3.zero; float bu = 0f, bv = 0f, bwc = 0f;
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++) {
                    List<int> bucket;
                    if (!grid.TryGetValue(CellKey(cx + dx, cy + dy, cz + dz), out bucket)) continue;
                    for (int k = 0; k < bucket.Count; k++) {
                        int ti = bucket[k];
                        if (stamp[ti] == queryId) continue;
                        stamp[ti] = queryId;
                        float u, vv, ww;
                        Vector3 cp = ClosestPtTri(w, tA[ti], tB[ti], tC[ti], out u, out vv, out ww);
                        float d2 = (cp - w).sqrMagnitude;
                        if (d2 < bestD2) { bestD2 = d2; bestTri = ti; bestCp = cp; bu = u; bv = vv; bwc = ww; }
                    }
                }
        if (bestTri < 0) return;                    // 近傍に服なし = 露出素肌

        Vector3 nrm = bu * nA[bestTri] + bv * nB[bestTri] + bwc * nC[bestTri];
        Vector3 rel = w - bestCp;
        float perp, tang;
        if (nrm.sqrMagnitude > 1e-10f) {
            nrm.Normalize();
            perp = Vector3.Dot(rel, nrm);
            tang = (rel - perp * nrm).magnitude;
        } else {
            perp = -rel.magnitude; tang = 0f;
        }

        float footprint = 1f - Mathf.Clamp01(tang / feather);
        if (footprint <= 0f) return;
        float depthInside = -perp;
        float perpFactor = 1f - Mathf.Clamp01((depthInside - coverRadius) * invBlend);
        if (perpFactor <= 0f) return;

        float weight = footprint * perpFactor;
        float regionMul = (vertMul != null) ? vertMul[i] : 1f;
        if (regionMul <= 0.001f) return;
        outV[i] -= n[i] * inset * weight * regionMul;
    }

    private void OnDestroy() {
        if (_cts != null) { try { _cts.Cancel(); } catch { } }      // 別スレッド計算を打ち切る(破棄後にthisを触らせない)
        if (generatedMesh != null) Object.Destroy(generatedMesh);   // 動的生成メッシュを解放
        EndWork();                                                  // 作業中に破棄されてもカウンタを戻す
    }

    /**=======================================================================
      ボーンウェイトから頂点ごとの凹ませ倍率を作る。
      各頂点の倍率 = Σ(ボーンウェイト × そのボーンの倍率) / Σ(ウェイト)。
      境目 (太もも⇔ひざ等) はスキンウェイトのなじみで滑らかに補間される。
      オーバーライド対象ボーンが1本も無ければ null (全頂点 倍率1)。
    =======================================================================**/
    private float[] BuildVertexMultipliers(SkinnedMeshRenderer body, Mesh mesh) {
        if (boneOverrides == null || boneOverrides.Length == 0) return null;

        Transform[] bones = body.bones;
        var boneMul = new float[bones.Length];
        bool any = false;
        for (int i = 0; i < bones.Length; i++) {
            boneMul[i] = 1f;
            if (bones[i] == null) continue;
            for (int o = 0; o < boneOverrides.Length; o++) {
                var ov = boneOverrides[o];
                if (ov == null || string.IsNullOrEmpty(ov.bonePrefix)) continue;
                if (bones[i].name.StartsWith(ov.bonePrefix, System.StringComparison.OrdinalIgnoreCase)) {
                    boneMul[i] = ov.multiplier;
                    any = true;
                    break;
                }
            }
        }
        if (!any) return null;

        BoneWeight[] bw = mesh.boneWeights;
        if (bw == null || bw.Length != mesh.vertexCount) return null;

        var mul = new float[bw.Length];
        for (int i = 0; i < bw.Length; i++) {
            BoneWeight w = bw[i];
            float m = 0f, wsum = 0f;
            if (w.weight0 > 0f) { m += w.weight0 * boneMul[w.boneIndex0]; wsum += w.weight0; }
            if (w.weight1 > 0f) { m += w.weight1 * boneMul[w.boneIndex1]; wsum += w.weight1; }
            if (w.weight2 > 0f) { m += w.weight2 * boneMul[w.boneIndex2]; wsum += w.weight2; }
            if (w.weight3 > 0f) { m += w.weight3 * boneMul[w.boneIndex3]; wsum += w.weight3; }
            mul[i] = (wsum > 1e-4f) ? m / wsum : 1f;
        }
        return mul;
    }

    // ClothLayer 配下なら服とみなす
    private bool IsCloth(SkinnedMeshRenderer smr) {
        for (Transform t = smr.transform; t != null; t = t.parent)
            if (t.name == "ClothLayer") return true;
        return false;
    }

    // 三角形をワールドAABBが重なる全セルへ登録
    private static void RegisterTri(Dictionary<long, List<int>> grid, Vector3 a, Vector3 b, Vector3 c, float cell, int idx) {
        float minx = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), maxx = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
        float miny = Mathf.Min(a.y, Mathf.Min(b.y, c.y)), maxy = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
        float minz = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), maxz = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
        int x0 = Mathf.FloorToInt(minx / cell), x1 = Mathf.FloorToInt(maxx / cell);
        int y0 = Mathf.FloorToInt(miny / cell), y1 = Mathf.FloorToInt(maxy / cell);
        int z0 = Mathf.FloorToInt(minz / cell), z1 = Mathf.FloorToInt(maxz / cell);
        // 退化した巨大三角形によるセル爆発を防ぐ安全クランプ(服の三角形は通常1〜2セル)
        x1 = Mathf.Min(x1, x0 + 16); y1 = Mathf.Min(y1, y0 + 16); z1 = Mathf.Min(z1, z0 + 16);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++) {
                    long key = CellKey(x, y, z);
                    List<int> bucket;
                    if (!grid.TryGetValue(key, out bucket)) { bucket = new List<int>(4); grid[key] = bucket; }
                    bucket.Add(idx);
                }
    }

    private static long CellKey(int x, int y, int z) {
        return ((long)x * 73856093L) ^ ((long)y * 19349663L) ^ ((long)z * 83492791L);
    }

    // 点 p から三角形 abc への最近点。重心座標 (u,v,w) も返す (Ericson, RTCD)。
    private static Vector3 ClosestPtTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c,
                                        out float u, out float v, out float w) {
        Vector3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) { u = 1f; v = 0f; w = 0f; return a; }               // 頂点 a 領域

        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) { u = 0f; v = 1f; w = 0f; return b; }                // 頂点 b 領域

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f) {                                        // 辺 ab
            float t = d1 / (d1 - d3);
            u = 1f - t; v = t; w = 0f; return a + t * ab;
        }

        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) { u = 0f; v = 0f; w = 1f; return c; }                // 頂点 c 領域

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f) {                                        // 辺 ac
            float t = d2 / (d2 - d6);
            u = 1f - t; v = 0f; w = t; return a + t * ac;
        }

        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f) {                          // 辺 bc
            float t = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            u = 0f; v = 1f - t; w = t; return b + t * (c - b);
        }

        float denom = 1f / (va + vb + vc);                                            // 面内
        float vN = vb * denom, wN = vc * denom;
        u = 1f - vN - wN; v = vN; w = wN;
        return a + ab * vN + ac * wN;
    }
}
