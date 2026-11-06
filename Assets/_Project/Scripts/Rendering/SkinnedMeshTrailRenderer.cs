using System.Collections.Generic;
using UnityEngine;

/**===========================================================================

 File   : スキンドメッシュ残像レンダラ [SkinnedMeshTrailRenderer.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 SkinnedMeshRenderer のポーズをフレームごとにベイクし、残像として描画する。
 BakeMesh したメッシュをプールで使い回して GC アロケを最小化する。
 1スナップショット = 全ソース (素体+服の全パーツ) を CombineMeshes で「1枚の
 メッシュ」に結合して保持。パーツ別描画だと上限あふれで一部欠け (足が無い残像)、
 半透明同士の重なりで濃度ムラが出るため。
 IsEmitting = true で放出開始、false で停止（既存エントリはフェードアウト）。
 ===========================================================================**/
[DisallowMultipleComponent]
public sealed class SkinnedMeshTrailRenderer : MonoBehaviour
{
    struct TrailEntry
    {
        public Mesh      mesh;
        public Matrix4x4 matrix;
        public float     spawnTime;
    }

    static readonly int ColorId     = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Header("Source")]
    [SerializeField] bool                  autoCollectFromChildren = false;
    [SerializeField] SkinnedMeshRenderer[] sources;

    [Header("Rendering")]
    [SerializeField] Material trailMaterial;
    [SerializeField] Color startColor = new Color(1f, 1f, 1f, 0.6f);
    [SerializeField] Color endColor   = new Color(1f, 1f, 1f, 0f);
    [SerializeField] int   renderLayer = 0;

    [Header("Trail")]
    [SerializeField, Min(0.01f)] float lifeTime     = 0.4f;
    [SerializeField, Min(0.01f)] float interval     = 0.05f;
    [SerializeField, Min(1)]    int   maxTrailCount = 30;

    [Header("Control")]
    [SerializeField] bool emitOnEnable = false;

    public bool IsEmitting { get; set; }

    readonly List<TrailEntry> _entries  = new List<TrailEntry>(30);
    readonly Queue<Mesh>      _meshPool = new Queue<Mesh>();
    MaterialPropertyBlock     _propertyBlock;
    float                     _nextEmitTime;

    void OnValidate()
    {
        if (autoCollectFromChildren)
            CollectFromChildren();
    }

    void OnEnable()
    {
        if (autoCollectFromChildren)
            CollectFromChildren();
        IsEmitting    = emitOnEnable;
        _nextEmitTime = 0f;
    }

    void OnDisable()
    {
        ClearEntries();
    }

    [ContextMenu("Collect From Children")]
    public void CollectFromChildren()
    {
        sources = GetComponentsInChildren<SkinnedMeshRenderer>(true);
    }

    // ランタイム装着用の一括設定（プレイヤー回避残像などコードから組み立てるケース）。
    // 着せ替えで SkinnedMeshRenderer が入れ替わることがあるため、放出開始のたびに
    // sources を渡し直して呼んでよい。
    public void Configure(SkinnedMeshRenderer[] src, Material mat, Color start, Color end,
                          float life, float emitInterval, int maxCount)
    {
        sources       = src;
        trailMaterial = mat;
        startColor    = start;
        endColor      = end;
        lifeTime      = Mathf.Max(0.01f, life);
        interval      = Mathf.Max(0.01f, emitInterval);
        maxTrailCount = Mathf.Max(1, maxCount);
    }

    void OnDestroy()
    {
        foreach (Mesh m in _meshPool)
            Destroy(m);
        _meshPool.Clear();
    }

    void LateUpdate()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();

        float now = Time.time;

        if (IsEmitting && now >= _nextEmitTime)
        {
            _nextEmitTime = now + interval;
            Snapshot(now);
        }

        DrawAndExpire(now);
    }

    // 外部から任意のタイミングで残像を1枚スポーンする
    public void EmitOnce()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();
        Snapshot(Time.time);
    }

    readonly List<CombineInstance> _combine     = new List<CombineInstance>(64);
    readonly List<Mesh>            _combineTemp = new List<Mesh>(64);

    void Snapshot(float now)
    {
        if (sources == null || trailMaterial == null) return;

        // 全ソース (素体+服の全パーツ) をベイクして「1枚のメッシュ」へ結合する。
        // パーツ別に持つと上限あふれで一部パーツだけ欠けた残像になり、
        // 半透明同士の重なりで濃度ムラも出るため、着衣状態の全身を丸ごと1枚にする。
        _combine.Clear();
        _combineTemp.Clear();
        for (int s = 0; s < sources.Length; s++)
        {
            SkinnedMeshRenderer smr = sources[s];
            if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;

            Mesh m = RentMesh();
            smr.BakeMesh(m);
            // CombineInstance は既定でサブメッシュ0しか取り込まないため、
            // マルチマテリアルのメッシュ (素体 pPlane10 / ブーツ等) は
            // 全サブメッシュを1つずつ登録しないとパーツが欠けた残像になる
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                _combine.Add(new CombineInstance
                {
                    mesh         = m,
                    subMeshIndex = sub,
                    transform    = smr.transform.localToWorldMatrix,
                });
            }
            _combineTemp.Add(m);
        }
        if (_combine.Count == 0) return;

        while (_entries.Count + 1 > maxTrailCount && _entries.Count > 0)
        {
            ReturnMesh(_entries[0].mesh);   // 先頭 = 最古
            _entries.RemoveAt(0);
        }

        Mesh combined = RentMesh();
        combined.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;  // 全身合計65k頂点超対応
        combined.CombineMeshes(_combine.ToArray(), true, true);           // ワールドへ焼き込み

        _entries.Add(new TrailEntry
        {
            mesh      = combined,
            matrix    = Matrix4x4.identity,   // 頂点は結合時にワールドへ変換済み
            spawnTime = now,
        });

        for (int i = 0; i < _combineTemp.Count; i++)
            ReturnMesh(_combineTemp[i]);      // ベイク用の一時メッシュはすぐプールへ返す
        _combineTemp.Clear();
    }

    void DrawAndExpire(float now)
    {
        if (trailMaterial == null) return;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            TrailEntry e = _entries[i];
            float age = now - e.spawnTime;

            if (age >= lifeTime)
            {
                ReturnMesh(e.mesh);
                _entries.RemoveAt(i);
                continue;
            }

            float t = age / lifeTime;
            Color c = Color.Lerp(startColor, endColor, t);
            _propertyBlock.SetColor(ColorId,     c);
            _propertyBlock.SetColor(BaseColorId, c);

            Graphics.DrawMesh(e.mesh, e.matrix, trailMaterial, renderLayer, null, 0, _propertyBlock);
        }
    }

    void ClearEntries()
    {
        for (int i = 0; i < _entries.Count; i++)
            ReturnMesh(_entries[i].mesh);
        _entries.Clear();
    }

    Mesh RentMesh()
    {
        if (_meshPool.Count > 0)
            return _meshPool.Dequeue();
        return new Mesh { name = "SkinnedMeshTrail", hideFlags = HideFlags.DontSave };
    }

    void ReturnMesh(Mesh m)
    {
        if (m == null) return;
        m.Clear();
        _meshPool.Enqueue(m);
    }

    void OnDrawGizmosSelected()
    {
        if (sources == null) return;
        Gizmos.color = new Color(0.8f, 0.4f, 1f, 0.4f);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null) continue;
            Bounds b = sources[i].bounds;
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
