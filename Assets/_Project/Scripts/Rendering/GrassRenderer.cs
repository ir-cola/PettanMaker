/**===========================================================================

 File   : 草インスタンシング描画システム [GrassRenderer.cs]

 Author : Hayato Otsuki
 -----------------------------------------------------------------------------
 Raycast でグラウンドメッシュ上に草をランダム配置し、Burst + Jobs で変形・
 カリングを並列処理して DrawMeshInstancedIndirect で GPU 描画する。
 BendGrass() で草を踏み倒せる。
 ===========================================================================**/
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Random = UnityEngine.Random;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class GrassRenderer : MonoBehaviour
{
    // CPU 側の草1本分のデータ（踏み倒し状態を含む）
    struct GrassBlade
    {
        public float3 position;
        public float4 baseQuat;      // 初期姿勢（ヨー回転 + 地形法線整合）
        public float  width;
        public float  height;
        public float3 bendDirection; // 踏み倒し方向
        public float  bendUntil;     // 踏み倒しが終わる時刻（Time.time）
    }

    // GPU へ送るコンパクトなインスタンスデータ
    // float4x4(64B) → posAndWidth(16B) + quaternion(16B) + height(4B) = 36B/blade
    struct GrassInstanceData
    {
        public float4 posAndWidth; // xyz=position, w=width
        public float4 quaternion;
        public float  height;
    }

    // 全ブレードの変形行列と暗化値を並列計算するジョブ
    // 踏み倒しが有効な場合は baseQuat に追加の回転クォータニオンを合成する
    [BurstCompile]
    struct ComputeGrassMatricesJob : IJobParallelFor
    {
        [ReadOnly]  public NativeArray<GrassBlade>        Blades;
        [WriteOnly] public NativeArray<GrassInstanceData> Instances;
        [WriteOnly] public NativeArray<float>             DarknessValues;
        public float Time;
        public float BendAngle;
        public float BendDuration;

        public void Execute(int i)
        {
            GrassBlade blade = Blades[i];

            // bendUntil までの残り時間を [0,1] に正規化して踏み倒し強度を得る
            float bendWeight = 0f;
            if (blade.bendUntil > Time)
                bendWeight = math.saturate((blade.bendUntil - Time) / BendDuration);

            float4 q = blade.baseQuat;
            if (bendWeight > 0f)
            {
                // 踏み倒し方向と上方向の外積 = 回転軸（地面に平行）
                float  angle = BendAngle * math.smoothstep(0f, 1f, bendWeight);
                float3 axis  = math.cross(new float3(0, 1, 0), blade.bendDirection);
                float  lenSq = math.lengthsq(axis);
                if (lenSq > 0.0001f)
                    q = MulQuat(AngleAxisQuat(angle, axis * math.rsqrt(lenSq)), q);
            }

            Instances[i] = new GrassInstanceData
            {
                posAndWidth = new float4(blade.position, blade.width),
                quaternion  = q,
                height      = blade.height
            };
            // 暗化値はシェーダーの _DarknessBuffer に渡り、踏み倒し量に比例して草を暗くする
            DarknessValues[i] = math.smoothstep(0f, 1f, bendWeight);
        }

        static float4 MulQuat(float4 a, float4 b) => new float4(
            a.w*b.x + a.x*b.w + a.y*b.z - a.z*b.y,
            a.w*b.y - a.x*b.z + a.y*b.w + a.z*b.x,
            a.w*b.z + a.x*b.y - a.y*b.x + a.z*b.w,
            a.w*b.w - a.x*b.x - a.y*b.y - a.z*b.z);

        static float4 AngleAxisQuat(float angle, float3 axis)
        {
            float s = math.sin(angle * 0.5f);
            float c = math.cos(angle * 0.5f);
            return new float4(axis.x*s, axis.y*s, axis.z*s, c);
        }
    }

    // 距離カリング + フラスタムカリングを並列実行し、可視ブレードだけを出力リストに詰め込むジョブ
    // NativeList.ParallelWriter を使うことでロックなしのスレッドセーフな書き込みを実現する
    [BurstCompile]
    struct CullAndCompactJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<GrassBlade>        Blades;
        [ReadOnly] public NativeArray<GrassInstanceData> Instances;
        [ReadOnly] public NativeArray<float>             Darkness;
        [ReadOnly] public NativeArray<float4>            FrustumPlanes;
        public int    DoFrustumCull;
        public float3 CameraPosition;
        public float  MaxDistanceSqr; // 0 = 無効（距離カリングをスキップ）

        public NativeList<GrassInstanceData>.ParallelWriter OutInstances;
        public NativeList<float>.ParallelWriter             OutDarkness;

        public void Execute(int i)
        {
            float3 pos = Blades[i].position;

            // 距離カリング: drawDistance の二乗と比較することで sqrt を省略
            if (MaxDistanceSqr > 0f)
            {
                float dx = pos.x - CameraPosition.x;
                float dy = pos.y - CameraPosition.y;
                float dz = pos.z - CameraPosition.z;
                if (dx*dx + dy*dy + dz*dz > MaxDistanceSqr) return;
            }

            // フラスタムカリング: 6平面それぞれに対して草の高さ分の半径でテスト
            if (DoFrustumCull == 1)
            {
                float rad = Blades[i].height;
                for (int p = 0; p < 6; p++)
                {
                    float4 pl = FrustumPlanes[p];
                    if (pl.x*pos.x + pl.y*pos.y + pl.z*pos.z + pl.w < -rad)
                        return;
                }
            }

            OutInstances.AddNoResize(Instances[i]);
            OutDarkness.AddNoResize(Darkness[i]);
        }
    }

    static readonly int WindDirectionId  = Shader.PropertyToID("_WindDirection");
    static readonly int WindStrengthId   = Shader.PropertyToID("_WindStrength");
    static readonly int WindSpeedId      = Shader.PropertyToID("_WindSpeed");
    static readonly int WindFrequencyId  = Shader.PropertyToID("_WindFrequency");
    static readonly int InstanceDataId   = Shader.PropertyToID("_InstanceData");
    static readonly int DarknessBufferId = Shader.PropertyToID("_DarknessBuffer");

    [Header("Rendering")]
    [SerializeField] Material grassMaterial;

    [Header("Wind")]
    [SerializeField] WindZone windZone;
    [SerializeField] bool useAttachedWindZone = true;
    [SerializeField, Min(0f)] float windStrengthScale  = 0.08f;
    [SerializeField, Min(0f)] float windSpeedScale     = 2f;
    [SerializeField, Min(0f)] float windFrequencyScale = 2f;

    [Header("Placement")]
    [SerializeField, Min(0.1f)] float densityPerSquareMeter = 2.5f;
    [SerializeField, Min(1)]    int   maxGrassCount          = 5000;
    [SerializeField]            LayerMask groundLayer;
    [SerializeField, Min(0.01f)] float surfaceOffset = 0.02f;
    [SerializeField]            bool  alignToGroundNormal = true;
    [SerializeField]            int   randomSeed = 12345;

    [Header("Blade")]
    [SerializeField] Vector2 widthRange  = new Vector2(0.18f, 0.32f);
    [SerializeField] Vector2 heightRange = new Vector2(0.45f, 0.75f);
    [SerializeField] bool    randomYaw   = true;

    [Header("Bend")]
    [SerializeField, Range(0f, 120f)] float bendAngle    = 55f;
    [SerializeField, Min(0.01f)]      float bendDuration = 1.2f;

    [Header("LOD")]
    [SerializeField, Min(1f)] float drawDistance = 50f;

    NativeArray<GrassBlade>        _blades;
    NativeArray<GrassInstanceData> _outputInstances;
    NativeArray<float>             _outputDarkness;
    NativeList<GrassInstanceData>  _visibleInstances;
    NativeList<float>              _visibleDarkness;
    NativeArray<float4>            _frustumPlanesNA;

    ComputeBuffer         _instanceBuffer;
    ComputeBuffer         _darknessBuffer;
    ComputeBuffer         _argsBuffer;
    readonly uint[]       _argsData = new uint[5];

    readonly List<Collider> _ownColliders  = new List<Collider>(8);
    readonly Plane[]        _frustumPlanes = new Plane[6];

    Mesh                  _crossQuadMesh;
    MaterialPropertyBlock _propertyBlock;
    bool                  _dirty = true;

    public float BendDuration => bendDuration;

    void Reset()
    {
        groundLayer = LayerMask.GetMask("Ground");
        windZone    = GetComponent<WindZone>();
    }

    void OnEnable()
    {
        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");
        ResolveWindZone();
        _dirty = true;
    }

    void OnValidate()
    {
        widthRange  = SortAndClamp(widthRange, 0.01f);
        heightRange = SortAndClamp(heightRange, 0.01f);
        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");
        ResolveWindZone();
        _dirty = true;
    }

    void OnDisable()
    {
        DisposeAll();
        DestroyRuntimeObject(_crossQuadMesh);
        _crossQuadMesh = null;
    }

    void Update()
    {
        if (_dirty)
            Rebuild();

        if (_crossQuadMesh == null || grassMaterial == null ||
            !_blades.IsCreated || _blades.Length == 0)
            return;

        DrawVisibleGrass();
    }

    public void Rebuild()
    {
        _dirty = false;
        BuildCrossQuadMesh();
        BuildGrassBlades();

        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();
    }

    public int BendGrass(Vector3 worldPosition, float radius, Vector3 direction)
    {
        if (!_blades.IsCreated || _blades.Length == 0 || radius <= 0f)
            return 0;

        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) return 0;
        direction.Normalize();

        float  now       = GetTime();
        float  radiusSqr = radius * radius;
        float3 dir3      = new float3(direction.x, direction.y, direction.z);
        int    bent      = 0;

        for (int i = 0; i < _blades.Length; i++)
        {
            GrassBlade blade = _blades[i];
            float dx = blade.position.x - worldPosition.x;
            float dz = blade.position.z - worldPosition.z;
            if (dx*dx + dz*dz > radiusSqr) continue;

            blade.bendDirection = dir3;
            blade.bendUntil     = now + bendDuration;
            _blades[i]          = blade;
            bent++;
        }

        return bent;
    }

    public bool ContainsHorizontalPosition(Vector3 worldPosition)
    {
        GetComponentsInChildren(false, _ownColliders);
        for (int i = 0; i < _ownColliders.Count; i++)
        {
            Collider col = _ownColliders[i];
            if (col == null || !col.enabled) continue;
            Bounds b = col.bounds;
            if (worldPosition.x >= b.min.x && worldPosition.x <= b.max.x &&
                worldPosition.z >= b.min.z && worldPosition.z <= b.max.z)
                return true;
        }
        Renderer r = GetComponent<Renderer>();
        if (r == null) return false;
        Bounds rb = r.bounds;
        return worldPosition.x >= rb.min.x && worldPosition.x <= rb.max.x &&
               worldPosition.z >= rb.min.z && worldPosition.z <= rb.max.z;
    }

    // 草1本を表す「十字クワッド」メッシュを生成する
    // 2枚の板ポリを90°交差させることで、どの視点からも草に見えるようにする
    void BuildCrossQuadMesh()
    {
        DestroyRuntimeObject(_crossQuadMesh);
        _crossQuadMesh = new Mesh { name = "Grass Cross Quad", hideFlags = HideFlags.DontSave };

        Vector3[] vertices =
        {
            new Vector3(-0.5f, 0f, 0f), new Vector3( 0.5f, 0f, 0f),
            new Vector3( 0.5f, 1f, 0f), new Vector3(-0.5f, 1f, 0f),
            new Vector3(0f, 0f, -0.5f), new Vector3(0f, 0f,  0.5f),
            new Vector3(0f, 1f,  0.5f), new Vector3(0f, 1f, -0.5f)
        };
        Vector2[] uvs =
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f),
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
        };
        _crossQuadMesh.vertices  = vertices;
        _crossQuadMesh.uv        = uvs;
        _crossQuadMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
        _crossQuadMesh.RecalculateNormals();
        _crossQuadMesh.RecalculateBounds();
    }

    // コライダーまたはレンダラーの AABB 範囲内にランダムにレイキャストして草を配置する
    // randomSeed + InstanceID で同じシーンに複数の GrassRenderer を置いても配置が重ならない
    void BuildGrassBlades()
    {
        DisposeAll();

        Bounds bounds;
        if (!TryGetPlacementBounds(out bounds))
            return;

        float area      = Mathf.Max(1f, bounds.size.x * bounds.size.z);
        int targetCount = Mathf.Clamp(Mathf.RoundToInt(area * densityPerSquareMeter), 1, maxGrassCount);
        int maxAttempts = targetCount * 8; // 地形が複雑な場合でも目標数に到達できるよう試行回数を多めに確保

        var temp = new List<GrassBlade>(targetCount);

        Random.State prev = Random.state;
        Random.InitState(randomSeed + GetInstanceID());

        for (int attempt = 0; attempt < maxAttempts && temp.Count < targetCount; attempt++)
        {
            Vector3 sample = new Vector3(
                Random.Range(bounds.min.x, bounds.max.x),
                bounds.max.y + 2f,
                Random.Range(bounds.min.z, bounds.max.z));

            if (!TryFindGround(sample, bounds.size.y + 4f, out RaycastHit hit))
                continue;

            float   yaw = randomYaw ? Random.Range(0f, 360f) : 0f;
            float   w   = Random.Range(widthRange.x, widthRange.y);
            float   h   = Random.Range(heightRange.x, heightRange.y);
            Vector3 pos = hit.point + hit.normal * surfaceOffset;

            Quaternion baseQuat = Quaternion.Euler(0f, yaw, 0f);
            if (alignToGroundNormal)
                baseQuat = Quaternion.FromToRotation(Vector3.up, hit.normal) * baseQuat;

            temp.Add(new GrassBlade
            {
                position      = new float3(pos.x, pos.y, pos.z),
                baseQuat      = new float4(baseQuat.x, baseQuat.y, baseQuat.z, baseQuat.w),
                width         = w,
                height        = h,
                bendDirection = float3.zero,
                bendUntil     = 0f
            });
        }

        Random.state = prev;

        if (temp.Count == 0)
            return;

        int n = temp.Count;

        _blades           = new NativeArray<GrassBlade>       (n, Allocator.Persistent);
        _outputInstances  = new NativeArray<GrassInstanceData>(n, Allocator.Persistent);
        _outputDarkness   = new NativeArray<float>            (n, Allocator.Persistent);
        _visibleInstances = new NativeList<GrassInstanceData> (n, Allocator.Persistent);
        _visibleDarkness  = new NativeList<float>             (n, Allocator.Persistent);
        _frustumPlanesNA  = new NativeArray<float4>           (6, Allocator.Persistent);

        for (int i = 0; i < n; i++)
            _blades[i] = temp[i];

        // stride = 36B: posAndWidth(float4=16) + quaternion(float4=16) + height(float=4)
        // ComputeBuffer をシェーダー側の StructuredBuffer<GrassInstance> と一致させる必要がある
        _instanceBuffer = new ComputeBuffer(n, 36, ComputeBufferType.Default);
        _darknessBuffer = new ComputeBuffer(n, 4,  ComputeBufferType.Default);
        _argsBuffer     = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);

        _argsData[0] = _crossQuadMesh != null ? _crossQuadMesh.GetIndexCount(0) : 12u;
        _argsData[1] = 0;
        _argsData[2] = 0;
        _argsData[3] = 0;
        _argsData[4] = 0;
        _argsBuffer.SetData(_argsData);
    }

    // フレームごとに呼ばれる描画メイン処理
    // Job1（変形計算）→ Job2（カリング・コンパクト）→ GPU バッファ更新 → DrawMeshInstancedIndirect の順で実行する
    void DrawVisibleGrass()
    {
        var job1 = new ComputeGrassMatricesJob
        {
            Blades         = _blades,
            Instances      = _outputInstances,
            DarknessValues = _outputDarkness,
            Time           = GetTime(),
            BendAngle      = bendAngle * Mathf.Deg2Rad,
            BendDuration   = bendDuration
        };
        JobHandle h1 = job1.Schedule(_blades.Length, 64);

        Camera cam     = Camera.main;
        bool doCulling = Application.isPlaying && cam != null;

        float3 camPos      = float3.zero;
        float  maxDistSqr  = 0f;
        if (cam != null)
        {
            Vector3 cp = cam.transform.position;
            camPos     = new float3(cp.x, cp.y, cp.z);
            maxDistSqr = drawDistance * drawDistance;
        }

        if (doCulling)
        {
            Matrix4x4 vp = cam.projectionMatrix * cam.worldToCameraMatrix;
            GeometryUtility.CalculateFrustumPlanes(vp, _frustumPlanes);
            for (int i = 0; i < 6; i++)
                _frustumPlanesNA[i] = new float4(
                    _frustumPlanes[i].normal.x, _frustumPlanes[i].normal.y,
                    _frustumPlanes[i].normal.z, _frustumPlanes[i].distance);
        }

        _visibleInstances.Clear();
        _visibleDarkness.Clear();
        var job2 = new CullAndCompactJob
        {
            Blades         = _blades,
            Instances      = _outputInstances,
            Darkness       = _outputDarkness,
            FrustumPlanes  = _frustumPlanesNA,
            DoFrustumCull  = doCulling ? 1 : 0,
            CameraPosition = camPos,
            MaxDistanceSqr = maxDistSqr,
            OutInstances   = _visibleInstances.AsParallelWriter(),
            OutDarkness    = _visibleDarkness.AsParallelWriter()
        };
        job2.Schedule(_blades.Length, 64, h1).Complete();

        int count = _visibleInstances.Length;
        if (count == 0) return;

        _instanceBuffer.SetData(_visibleInstances.AsArray(), 0, 0, count);
        _darknessBuffer.SetData(_visibleDarkness.AsArray(),  0, 0, count);
        _argsData[1] = (uint)count;
        _argsBuffer.SetData(_argsData);

        PushWindProperties();
        _propertyBlock.SetBuffer(InstanceDataId,   _instanceBuffer);
        _propertyBlock.SetBuffer(DarknessBufferId, _darknessBuffer);

        Bounds drawBounds;
        if (!TryGetPlacementBounds(out drawBounds))
            drawBounds = new Bounds(transform.position, Vector3.one * 500f);
        drawBounds.Expand(2f);

        Graphics.DrawMeshInstancedIndirect(
            _crossQuadMesh, 0, grassMaterial,
            drawBounds, _argsBuffer, 0, _propertyBlock);
    }

    void DisposeAll()
    {
        if (_blades.IsCreated)            _blades.Dispose();
        if (_outputInstances.IsCreated)   _outputInstances.Dispose();
        if (_outputDarkness.IsCreated)    _outputDarkness.Dispose();
        if (_visibleInstances.IsCreated)  _visibleInstances.Dispose();
        if (_visibleDarkness.IsCreated)   _visibleDarkness.Dispose();
        if (_frustumPlanesNA.IsCreated)   _frustumPlanesNA.Dispose();
        _instanceBuffer?.Release();  _instanceBuffer = null;
        _darknessBuffer?.Release();  _darknessBuffer = null;
        _argsBuffer?.Release();      _argsBuffer     = null;
    }

    // useAttachedWindZone が有効かつ windZone 未設定の場合、同一 GameObject の WindZone を自動取得する
    void ResolveWindZone()
    {
        if (!useAttachedWindZone || windZone != null) return;
        windZone = GetComponent<WindZone>();
    }

    // WindZone のパラメータをシェーダーの MaterialPropertyBlock に書き込む
    // windZone が null の場合は何もしない（風なし状態）
    void PushWindProperties()
    {
        if (_propertyBlock == null)
            _propertyBlock = new MaterialPropertyBlock();
        _propertyBlock.Clear();

        if (windZone == null) return;

        Vector3 dir = Vector3.ProjectOnPlane(windZone.transform.forward, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
        dir.Normalize();

        float strength  = Mathf.Max(0f, windZone.windMain) * windStrengthScale;
        float speed     = Mathf.Max(0f, windZone.windMain + windZone.windPulseMagnitude) * windSpeedScale;
        float frequency = Mathf.Max(0.01f, 1f + windZone.windTurbulence) * windFrequencyScale;

        _propertyBlock.SetVector(WindDirectionId, new Vector4(dir.x, 0f, dir.z, 0f));
        _propertyBlock.SetFloat (WindStrengthId,  strength);
        _propertyBlock.SetFloat (WindSpeedId,     speed);
        _propertyBlock.SetFloat (WindFrequencyId, frequency);
    }

    bool TryGetPlacementBounds(out Bounds bounds)
    {
        GetComponentsInChildren(false, _ownColliders);
        bool hasBounds = false;
        bounds = default;
        for (int i = 0; i < _ownColliders.Count; i++)
        {
            Collider col = _ownColliders[i];
            if (col == null || !col.enabled) continue;
            if (!hasBounds) { bounds = col.bounds; hasBounds = true; }
            else            { bounds.Encapsulate(col.bounds); }
        }
        if (hasBounds) return true;
        Renderer r = GetComponent<Renderer>();
        if (r == null) return false;
        bounds = r.bounds;
        return true;
    }

    // 指定地点から真下にレイを飛ばし、この GrassRenderer のコライダーを探す
    // まず groundLayer マスクで高速検索し、見つからなければ全レイキャストで補完する
    bool TryFindGround(Vector3 rayOrigin, float distance, out RaycastHit hit)
    {
        int mask = groundLayer.value != 0 ? groundLayer.value : LayerMask.GetMask("Ground");
        if (Physics.Raycast(rayOrigin, Vector3.down, out hit, distance, mask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<GrassRenderer>() == this)
                return true;
        }

        // groundLayer マスクでヒットしなかった場合、全レイヤーを対象に RaycastAll で再探索し
        // 自身の GrassRenderer を持つコライダーのうち最も近いものを返す
        RaycastHit[] hits    = Physics.RaycastAll(rayOrigin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
        float        nearest = float.PositiveInfinity;
        bool         found   = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider.GetComponentInParent<GrassRenderer>() != this) continue;
            if (hits[i].distance >= nearest) continue;
            nearest = hits[i].distance;
            hit     = hits[i];
            found   = true;
        }
        if (found) return true;
        hit = default;
        return false;
    }

    static Vector2 SortAndClamp(Vector2 v, float minimum)
    {
        v.x = Mathf.Max(minimum, v.x);
        v.y = Mathf.Max(minimum, v.y);
        if (v.x > v.y) (v.x, v.y) = (v.y, v.x);
        return v;
    }

    static float GetTime() =>
        Application.isPlaying ? Time.time : Time.realtimeSinceStartup;

    static void DestroyRuntimeObject(Object target)
    {
        if (target == null) return;
        if (Application.isPlaying) Destroy(target);
        else DestroyImmediate(target);
    }

    void OnDrawGizmosSelected()
    {
        if (!TryGetPlacementBounds(out Bounds bounds)) return;
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.25f);
        Gizmos.DrawCube(bounds.center, bounds.size);
    }
}
