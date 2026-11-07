using System.Collections.Generic;
using UnityEngine;

// MeshFilter を持つ静的メッシュの残像を描画するコンポーネント。
// メッシュ自体は sharedMesh を使い回すため、BakeMesh コストは発生しない。
// IsEmitting = true でトレイル放出開始、false で停止（既存エントリはフェードアウト）。
[DisallowMultipleComponent]
public sealed class MeshTrailRenderer : MonoBehaviour
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
    [SerializeField] bool         autoCollectFromChildren = false;
    [SerializeField] MeshFilter[] sources;

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

    readonly List<TrailEntry> _entries       = new List<TrailEntry>(30);
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
        _entries.Clear();
    }

    [ContextMenu("Collect From Children")]
    public void CollectFromChildren()
    {
        sources = GetComponentsInChildren<MeshFilter>(true);
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

    void Snapshot(float now)
    {
        if (sources == null || trailMaterial == null) return;

        for (int s = 0; s < sources.Length; s++)
        {
            MeshFilter mf = sources[s];
            if (mf == null || mf.sharedMesh == null) continue;
            if (!mf.gameObject.activeInHierarchy)    continue;

            if (_entries.Count >= maxTrailCount) return;

            _entries.Add(new TrailEntry
            {
                mesh      = mf.sharedMesh,
                matrix    = mf.transform.localToWorldMatrix,
                spawnTime = now,
            });
        }
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

    void OnDrawGizmosSelected()
    {
        if (sources == null) return;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == null || sources[i].sharedMesh == null) continue;
            Bounds b = sources[i].sharedMesh.bounds;
            b.center = sources[i].transform.TransformPoint(b.center);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
