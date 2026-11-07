using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// テールアタック着地で発生する、ドーナッツ状に外へ広がるダメージの波。
/// 中心の穴(InnerRadius)から MaxRadius まで波面が拡大し、波面が通過した瞬間に
/// 各対象へ一度だけダメージを与える。穴(InnerRadius より内側)にいる対象は当たらない安全地帯。
///
/// 見た目は地面に寝かせたリング(アニュラス)メッシュをプロシージャル生成し、波面に同期して
/// 半径を更新する。マテリアルは外部から差し替え可能（未設定なら URP 系シェーダにフォールバック）。
/// 頂点カラーで先端→内側のフェードと寿命フェードを乗せるので、頂点カラー対応の
/// アンリット/パーティクル系マテリアルならそのまま波として馴染む。
///
/// Spawn() を1回呼ぶだけでよい。B1_ShockwaveDistortion と同じ自己完結・自己破棄パターン。
/// </summary>
[DisallowMultipleComponent]
public sealed class B1_DamageWave : MonoBehaviour
{
    /// <summary>1波ぶんのパラメータ。</summary>
    public struct Params
    {
        public float duration;      // 波面が InnerRadius→MaxRadius に広がりきる時間(秒)
        public float innerRadius;   // ドーナッツの穴半径(波の開始半径)。ここより内側は安全
        public float maxRadius;     // 波面が到達する最大半径
        public float thickness;     // 見た目リング(帯)の幅(m)
        public int   damage;        // 波面通過時に与えるダメージ
        public DamageContext effects;
        public IHealth alreadyDamagedTarget; // 着弾時などに同じ攻撃で既に被弾済みの対象（二重ダメージ防止）
        public Color color;         // リングの発光色(頂点カラーで乗る)
        public Material material;    // リングに貼るマテリアル。null なら URP 系にフォールバック
        public float jumpDodgeHeight; // これより高く跳んで(波の地面より上に)いる対象は波が下を通り抜ける。0 で高さ無視(従来どおり必ず当たる)
        public float height;          // 波の見た目の高さ(m)。山なりに立ち上げて「跳べば越えられる壁」に見せる。通常は jumpDodgeHeight と同値。0 で従来の平面
        public Transform screenDistortTarget; // 波面がこの対象(通常プレイヤー)を通過する瞬間に衝撃波を出す。null で無効。表示位置もこの対象のローカル軸を基準に解決する
        public bool screenDistortOnPass;      // ↑を有効にするか
        public B1_ShockwaveDistortion.Params screenDistortParams; // 通過時に出す3D衝撃波の見た目(設定は B1_EffectSettings 由来)
        public Vector3 screenDistortOffset;   // screenDistortTarget のローカル軸(x=右,y=上,z=前)での表示オフセット
        public float spikeDepth;              // 円周上のトゲの深さ(thickness に対する倍率)。0 で完全な真円の波面
    }

    // マテリアル未指定時に順に探すシェーダ。専用のチェシャ猫衝撃波を最優先し、無ければ汎用へ。
    // ※ ビルドで確実に出したい場合は Params.material に本シェーダのマテリアルを割り当てること
    //    （Shader.Find だけの参照はビルド時に剥がされることがある）。
    private static readonly string[] ShaderCandidates =
    {
        "PettanMaker/Boss_01/CheshireShockwave",
        "Universal Render Pipeline/Particles/Unlit",
        "Sprites/Default",
        "Unlit/Transparent",
    };

    private const int   Segments       = 64;   // 円周方向の分割数
    private const int   RadialSegments = 6;    // 半径方向(断面)の分割数。大きいほど山なりが滑らか
    private const float GroundOffsetY  = 0.06f; // 地面へのめり込み防止の浮かせ量
    private const float CrestPosition  = 0.7f;  // 山の頂点位置(0=後端,1=先端)。前寄りで「転がって迫る」波に見える
    private const int   SpikeCount     = 16;   // 円周上のトゲの数（ギザギザの衝撃波）。Segments を割り切る値だと先端が鋭く揃う
    // トゲの深さは Params.spikeDepth (thickness に対する倍率、0 で真円) で呼び出し側が指定する。

    private Params    p;
    private Vector3   center;
    private IHealth   ignore;    // ボス自身(自傷防止)
    private Component source;    // ヒットストップ等の発生源
    private float     elapsed;
    private float     front;
    private float     prevFront; // 前フレームの波面位置。帯の後端をここまで広げてすり抜けを防ぐ
    private bool      _screenDistortFired; // 画面歪みを一度だけ発生させるためのフラグ

    private readonly HashSet<IHealth> _damaged = new();
    private static readonly Collider[] _hits = new Collider[64];

    // プロシージャルメッシュ（毎フレーム頂点だけ更新。三角形/UV/法線は生成時に固定）。
    private Mesh          mesh;
    private MeshRenderer  meshRenderer;
    private Material      ownedMaterial;   // フォールバック生成時のみ破棄対象
    private Vector3[]     _verts;
    private Color[]       _colors;
    private float[]       _cosLut;   // 円周方向 cos（生成時に固定）
    private float[]       _sinLut;   // 円周方向 sin
    private float[]       _spikeLut; // 円周方向のトゲ量 0..1（生成時に固定）
    private float[]       _profS;    // 各断面リングの半径パラメータ s (0=後端,1=先端)
    private float[]       _profY;    // 各断面リングの高さ Y（山なり。寿命では変えない）

    /// <summary>指定位置にドーナッツ状のダメージ波を1つ発生させる。</summary>
    public static B1_DamageWave Spawn(Vector3 center, in Params p, IHealth ignore, Component source)
    {
        var go = new GameObject("B1_DamageWave");
        go.hideFlags = HideFlags.DontSave; // 演出用の一時オブジェクト
        go.transform.position = center;
        var self = go.AddComponent<B1_DamageWave>();
        self.Init(center, p, ignore, source);
        return self;
    }

    private void Init(Vector3 c, in Params src, IHealth ig, Component so)
    {
        center = c;
        p      = src;
        ignore = ig;
        source = so;

        _damaged.Clear();
        if (p.alreadyDamagedTarget != null)
            _damaged.Add(p.alreadyDamagedTarget);

        p.duration    = Mathf.Max(0.05f, p.duration);
        p.innerRadius = Mathf.Max(0f, p.innerRadius);
        p.maxRadius   = Mathf.Max(p.innerRadius + 0.01f, p.maxRadius);
        p.thickness   = Mathf.Max(0.01f, p.thickness);
        p.height      = Mathf.Max(0f, p.height);

        elapsed   = 0f;
        front     = p.innerRadius;
        prevFront = p.innerRadius;

        BuildMesh();
        UpdateMesh(0f);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / p.duration);
        front = Mathf.Lerp(p.innerRadius, p.maxRadius, t);

        ApplyBandDamage();
        TryScreenDistortOnPass();
        UpdateMesh(t);

        prevFront = front; // 帯の後端計算用 (フレーム間の波面すり抜け防止)

        if (t >= 1f)
            Destroy(gameObject);
    }

    // 波面のリング帯 [front - thickness, front] にいる対象へ一度だけダメージ。
    // リングが既に通過した対象は以後安全 (見た目のリングと判定を一致させる)。
    // 低フレームレートで帯を一気に飛び越えないよう、後端は前フレームの波面位置まで広げる。
    private void ApplyBandDamage()
    {
        int n = Physics.OverlapSphereNonAlloc(
            center, front, _hits, Physics.AllLayers, QueryTriggerInteraction.Collide);

        for (int i = 0; i < n; i++)
        {
            Collider hit = _hits[i];
            IHealth health = hit.GetComponent<IHealth>() ?? hit.GetComponentInParent<IHealth>();
            if (health == null || health.IsDead) continue;
            if (ReferenceEquals(health, ignore)) continue;
            if (_damaged.Contains(health)) continue;

            // 水平距離でドーナッツ帯を判定(高さは無視して地面リングとして扱う)。
            Vector3 tp = (health as MonoBehaviour) != null
                ? ((MonoBehaviour)health).transform.position
                : hit.transform.position;

            float planar = PlanarDist(center, tp);
            if (planar < p.innerRadius) continue; // 穴の中は安全
            if (planar > front) continue;         // まだ波面が届いていない

            // リング後端より内側 = 波は既に通過済み。以後この対象は安全 (再判定しない)。
            float tail = Mathf.Max(p.innerRadius, Mathf.Min(prevFront, front - p.thickness));
            if (planar < tail)
            {
                _damaged.Add(health);
                continue;
            }

            // ジャンプ回避：波の地面(center.y)より jumpDodgeHeight 以上 上にいる対象は波が下を通過する。
            // (帯の中に居ても空中なら無傷。リングが通過しきれば上の判定で安全確定する)
            if (p.jumpDodgeHeight > 0f && (tp.y - center.y) > p.jumpDodgeHeight) continue;

            _damaged.Add(health);
            CombatSystem.Instance.Damage(health, p.damage, p.effects, source);
        }
    }

    // 波面が対象(通常プレイヤー)の位置を通過した瞬間、画面向きの衝撃波歪みを一度だけ出す。
    // 当たり判定とは独立（跳んで回避した対象の頭上を波が抜けるときも画面は歪む）。
    private void TryScreenDistortOnPass()
    {
        if (_screenDistortFired || !p.screenDistortOnPass || p.screenDistortTarget == null) return;
        Vector3 tp = p.screenDistortTarget.position;
        if (PlanarDist(center, tp) > front) return; // まだ波面が届いていない

        _screenDistortFired = true;
        Vector3 spawnPos = tp + p.screenDistortTarget.TransformVector(p.screenDistortOffset);
        B1_ShockwaveDistortion.Spawn(spawnPos, p.screenDistortParams);
    }

    // --- プロシージャルメッシュ生成 -------------------------------------------------

    // 三角形・UV・法線を1度だけ確定する。頂点座標と色は UpdateMesh で毎フレーム流し込む。
    // 断面(半径方向)を RadialSegments 段に分け、後端→頂点→先端の山なりプロファイルで立ち上げる。
    private void BuildMesh()
    {
        int ringVerts    = Segments + 1;         // 円周方向(シームを閉じるため +1)
        int profileRings = RadialSegments + 1;   // 断面方向(後端→頂点→先端)
        int vertCount    = ringVerts * profileRings;

        _verts  = new Vector3[vertCount];
        _colors = new Color[vertCount];
        var uv      = new Vector2[vertCount];
        var uv2     = new Vector2[vertCount]; // x=正規化した高さ(0=接地,1=頂点)。シェーダの山陰影・稜線発光に使う
        var normals = new Vector3[vertCount];
        var tris    = new int[Segments * RadialSegments * 6];

        // 円周方向の cos/sin とトゲ量は毎フレーム同じなので LUT 化。
        _cosLut   = new float[ringVerts];
        _sinLut   = new float[ringVerts];
        _spikeLut = new float[ringVerts];
        for (int i = 0; i <= Segments; i++)
        {
            float u   = i / (float)Segments;
            float ang = u * Mathf.PI * 2f;
            _cosLut[i]   = Mathf.Cos(ang);
            _sinLut[i]   = Mathf.Sin(ang);
            _spikeLut[i] = Tri(u * SpikeCount); // 三角波でトゲ（先端が鋭い）
        }

        // 断面プロファイル：半径パラメータ s と高さ Y は寿命で変わらないのでここで確定。
        _profS = new float[profileRings];
        _profY = new float[profileRings];
        for (int j = 0; j <= RadialSegments; j++)
        {
            float s  = j / (float)RadialSegments; // 0=後端 1=先端(波面)
            float hN = CrestHeight01(s);          // 山なりの正規化高さ 0..1
            _profS[j] = s;
            _profY[j] = GroundOffsetY + p.height * hN;

            float heightUV = p.height > 0.0001f ? hN : 0f; // 平面(height=0)のときは山陰影を無効化
            for (int i = 0; i <= Segments; i++)
            {
                int idx = j * ringVerts + i;
                uv[idx]      = new Vector2(i / (float)Segments, s); // U=円周, V=半径(0=後端,1=先端)
                uv2[idx]     = new Vector2(heightUV, 0f);
                normals[idx] = Vector3.up;
            }
        }

        // 隣り合う断面リング間を帯で貼る（後側 j, 前側 j+1）。
        for (int j = 0; j < RadialSegments; j++)
        {
            for (int i = 0; i < Segments; i++)
            {
                int a = j * ringVerts + i;           // 後側 i
                int b = j * ringVerts + i + 1;       // 後側 i+1
                int c = (j + 1) * ringVerts + i;     // 前側 i
                int d = (j + 1) * ringVerts + i + 1; // 前側 i+1
                int t = (j * Segments + i) * 6;
                tris[t + 0] = a; tris[t + 1] = b; tris[t + 2] = c;
                tris[t + 3] = b; tris[t + 4] = d; tris[t + 5] = c;
            }
        }

        mesh = new Mesh { name = "B1_DamageWaveRing" };
        mesh.MarkDynamic();
        mesh.vertices  = _verts;   // 仮。UpdateMesh で確定
        mesh.uv        = uv;
        mesh.uv2       = uv2;
        mesh.normals   = normals;
        mesh.triangles = tris;

        var mf = gameObject.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows    = false;
        meshRenderer.lightProbeUsage   = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.sharedMaterial    = ResolveMaterial();
    }

    // 波面半径に合わせて各断面リングを再配置し、頂点カラーでフェードを乗せる。
    // 高さ Y は生成時に確定済み（_profY）なので、ここでは XZ 半径と色だけを更新する。
    private void UpdateMesh(float t)
    {
        if (mesh == null) return;

        int ringVerts = Segments + 1;
        float outer = front;                              // 帯の先端 = 波面(ダメージ判定と一致)
        float inner = Mathf.Max(0f, front - p.thickness); // 帯の後端
        float band  = outer - inner;

        float life = 1f - t; // 寿命 t が進むほど全体をフェード

        for (int j = 0; j <= RadialSegments; j++)
        {
            float s       = _profS[j];
            float y       = _profY[j];
            float radBase = inner + band * s;
            float spikeMag = Mathf.Max(0f, p.spikeDepth) * p.thickness * s; // 後端(s=0)は真円、先端へ向けてトゲが伸びる。spikeDepth=0 なら常に真円

            // 後端(s=0)は透明、先端(s=1)へ向けて不透明。寿命でも全体をフェード。
            Color col = p.color;
            col.a     = p.color.a * life * s;

            int rowBase = j * ringVerts;
            for (int i = 0; i <= Segments; i++)
            {
                int idx = rowBase + i;
                float rad = radBase + spikeMag * _spikeLut[i];
                _verts[idx]  = new Vector3(_cosLut[i] * rad, y, _sinLut[i] * rad);
                _colors[idx] = col;
            }
        }

        mesh.vertices = _verts;
        mesh.colors   = _colors;
        mesh.RecalculateBounds();
    }

    // 三角波 0..1（周期1。frac=0.5 で頂点=トゲの先が鋭く尖る）。
    private static float Tri(float x)
    {
        float f = x - Mathf.Floor(x);
        return 1f - Mathf.Abs(2f * f - 1f);
    }

    // 断面の山なり高さ(0..1)。後端(0)と先端(1)は接地、CrestPosition で頂点になる凸カーブ。
    private static float CrestHeight01(float s)
    {
        s = Mathf.Clamp01(s);
        return s <= CrestPosition
            ? Mathf.SmoothStep(0f, 1f, s / CrestPosition)
            : Mathf.SmoothStep(0f, 1f, (1f - s) / (1f - CrestPosition));
    }

    // マテリアル未指定時は URP 系シェーダで簡易マテリアルを生成（頂点カラー透過）。
    private Material ResolveMaterial()
    {
        if (p.material != null) return p.material; // 差し替えマテリアルはそのまま共有利用

        Shader sh = null;
        foreach (var name in ShaderCandidates)
        {
            sh = Shader.Find(name);
            if (sh != null) break;
        }
        if (sh == null)
        {
            Debug.LogWarning("[B1_DamageWave] リング用シェーダが見つかりません。ダメージのみ適用します。");
            if (meshRenderer != null) meshRenderer.enabled = false;
            return null;
        }

        ownedMaterial = new Material(sh);
        return ownedMaterial;
    }

    private static float PlanarDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void OnDestroy()
    {
        if (mesh != null)          { B1_CinematicUtil.SafeDestroy(mesh); mesh = null; }
        if (ownedMaterial != null) { B1_CinematicUtil.SafeDestroy(ownedMaterial); ownedMaterial = null; }
    }
}
