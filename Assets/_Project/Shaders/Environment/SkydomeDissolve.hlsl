#ifndef SKYDOME_DISSOLVE_INCLUDED
#define SKYDOME_DISSOLVE_INCLUDED

// スカイドーム・ディゾルブ クロスフェード用の共通ヘルパー（豪華版）。
//
// 退出側ドームを「地平線 → 天頂」へ有機的に溶かし、溶け際に
//   ・白熱コア（細く鋭い光の芯）
//   ・二色の炎本体
//   ・空へにじむ外側ハロー
//   ・きらめく火の粉（エンバー）
// を HDR 加算で重ねた「燃えるウェーブフロント」を出す。Bloom と組み合わせると豪華。
// 不透明シェーダーのまま、異なるシェーダー同士でもクロスフェードできる。
//
// 各マテリアルに必要なプロパティ:
//   float _Dissolve                 … 0=通常, 1=完全に溶けた（各シェーダー側で宣言・SkydomeBlendSystem が制御）
// グローバル（SkydomeBlendSystem が Shader.SetGlobal* で設定。ここで宣言）:
//   float4 _SkydomeDissolveColor    … 炎本体・ハローの主色（HDR）
//   float4 _SkydomeDissolveColor2   … 白熱コア・火の粉の副色（HDR）
//   float  _SkydomeDissolveEdge     … エッジ帯の幅
//   float  _SkydomeDissolveScale    … 有機ノイズのスケール

float4 _SkydomeDissolveColor;
float4 _SkydomeDissolveColor2;
float  _SkydomeDissolveEdge;
float  _SkydomeDissolveScale;

// パターン切り替え（SkydomeBlendSystem 派生が設定）
float  _SkydomeDissolveMode;        // 0 = Noise, 1 = Tile, 2 = WonderTile
float  _SkydomeDissolveTiles;       // Tile: 縦方向のタイル数
float  _SkydomeDissolveTileRandom;  // Tile/WonderTile: 遷移順のランダムばらつき [0,1]

// WonderTile（Mode=2）: WonderSkydome のチェッカー計算に一致させるためのパラメータ。
// SkydomeBlendSystem_ToWonder が対象 Wonder マテリアルから読み取って設定する。
float  _SkydomeWonderWarpScale;
float  _SkydomeWonderDistortStrength;
float  _SkydomeWonderDistortSpeed;
float  _SkydomeWonderCheckScale;

float _SkydomeHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float _SkydomeNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(_SkydomeHash(i),                _SkydomeHash(i + float2(1, 0)), u.x),
                lerp(_SkydomeHash(i + float2(0, 1)),  _SkydomeHash(i + float2(1, 1)), u.x), u.y);
}

float _SkydomeFbm(float2 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * _SkydomeNoise(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

// 視線方向を平面投影した UV（field / ember 共通）
float2 _SkydomeProjUV(float3 dir)
{
    return dir.xz / max(abs(dir.y) + 0.3, 0.3);
}

// --- Noise パターン：地平線→天頂へ有機的に掃ける ---
float SkydomeNoiseField(float3 dir, float2 screenPos)
{
    float scale = _SkydomeDissolveScale > 0.0 ? _SkydomeDissolveScale : 3.0;

    // 地平線(下=0) → 天頂(上=1) の基本グラデーション
    float sweep = saturate(dir.y * 0.5 + 0.5);

    // 有機的な揺らぎ
    float n = _SkydomeFbm(_SkydomeProjUV(dir) * scale);

    float field = sweep * 0.65 + n * 0.35;

    // スクリーンスペースの微小ディザで境界のジャギを軽減
    field += (_SkydomeHash(screenPos) - 0.5) * 0.02;
    return saturate(field);
}

// --- Tile パターン：格子状のタイルが順に切り替わる ---
float SkydomeTileField(float3 dir)
{
    // PI は Unity の Core.hlsl で定義済みのマクロを使用
    float phi   = atan2(dir.z, dir.x);              // -PI..PI
    float theta = acos(clamp(dir.y, -1.0, 1.0));    // 0(天頂)..PI
    float2 uv = float2(phi / (2.0 * PI) + 0.5, theta / PI);

    float rows = floor(max(_SkydomeDissolveTiles, 2.0));
    float2 gcount = float2(rows * 2.0, rows);        // 横は2倍でほぼ正方形
    float2 g = uv * gcount;
    float2 cell = floor(g);
    float2 f = frac(g);

    // タイル中心の仰角で掃く順を決め、ランダムで散らす
    float2 center = (cell + 0.5) / gcount;
    float sweep = saturate(1.0 - center.y * 2.0);    // 地平線(center.y~0.5)=先, 天頂=後
    float rnd = _SkydomeHash(cell);
    float tileVal = lerp(sweep, rnd, saturate(_SkydomeDissolveTileRandom));

    // 目地（タイル境界）をわずかに先行させ、光る格子ラインを出す
    float2 fb = abs(f - 0.5);
    float edgeDist = 0.5 - max(fb.x, fb.y);          // 0=境界, 0.5=中央
    float seam = smoothstep(0.0, 0.05, edgeDist);
    tileVal -= (1.0 - seam) * 0.03;

    return saturate(tileVal);
}

// --- 3D ノイズ（シームレス：球面の継ぎ目・極を作らない） ---
float _SkyHash3(float3 p)
{
    return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453);
}

float _SkyNoise3(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = f * f * (3.0 - 2.0 * f);
    float n000 = _SkyHash3(i + float3(0, 0, 0));
    float n100 = _SkyHash3(i + float3(1, 0, 0));
    float n010 = _SkyHash3(i + float3(0, 1, 0));
    float n110 = _SkyHash3(i + float3(1, 1, 0));
    float n001 = _SkyHash3(i + float3(0, 0, 1));
    float n101 = _SkyHash3(i + float3(1, 0, 1));
    float n011 = _SkyHash3(i + float3(0, 1, 1));
    float n111 = _SkyHash3(i + float3(1, 1, 1));
    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z);
}

float _SkyFbm3(float3 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * _SkyNoise3(p);
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

// チェッカーの横方向が経線(φ=±π)で連続するよう偶数セル数に丸める
float SkydomeEvenScale(float s)
{
    return max(2.0, round(s * 0.5) * 2.0);
}

// WonderSkydome とディゾルブで共有する「歪み込み UV」（3Dノイズでシームレス）。
// baseUV / distortVec も返すので、スート散布など元の計算に流用できる。
float2 SkydomeWonderDistortedUV(float3 dir, float warpScale, float distortStr, float distortSpeed,
                                out float2 baseUV, out float2 distortVec)
{
    float phi   = atan2(dir.z, dir.x);
    float theta = acos(clamp(dir.y, -1.0, 1.0));
    baseUV = float2(phi / (2.0 * PI) + 0.5, theta / PI);

    float t = _Time.y * distortSpeed;
    float3 wp = dir * warpScale + float3(t * 0.1, t * 0.07, 0.0);
    distortVec = float2(_SkyFbm3(wp), _SkyFbm3(wp + float3(5.2, 1.3, 2.7)));

    return baseUV + distortStr * (distortVec - 0.5);
}

// --- WonderTile パターン：WonderSkydome のチェッカー（歪み込み・シームレス）に一致 ---
float SkydomeWonderTileField(float3 dir)
{
    float2 baseUV, distortVec;
    float2 distortedUV = SkydomeWonderDistortedUV(dir, _SkydomeWonderWarpScale,
                             _SkydomeWonderDistortStrength, _SkydomeWonderDistortSpeed,
                             baseUV, distortVec);

    float sc = SkydomeEvenScale(_SkydomeWonderCheckScale);
    float2 s = distortedUV * sc;
    float2 checker = floor(s);

    // タイル中心の仰角で掃く順を決め、ランダムで散らす
    float cellCenterY = (checker.y + 0.5) / sc;
    float sweep = saturate(1.0 - cellCenterY * 2.0);
    float rnd = _SkyHash3(float3(checker, 0.0));
    float tileVal = lerp(sweep, rnd, saturate(_SkydomeDissolveTileRandom));

    // チェッカー境界（歪みに追従）を先行させ、光る目地を出す
    float2 fb = abs(frac(s) - 0.5);
    float edgeDist = 0.5 - max(fb.x, fb.y);
    float seam = smoothstep(0.0, 0.05, edgeDist);
    tileVal -= (1.0 - seam) * 0.03;

    return saturate(tileVal);
}

// ディゾルブの「進行度フィールド」[0,1]。値が小さいほど先に溶ける。
float SkydomeDissolveField(float3 worldDir, float2 screenPos)
{
    float3 dir = normalize(worldDir);
    if (_SkydomeDissolveMode < 0.5) return SkydomeNoiseField(dir, screenPos);
    if (_SkydomeDissolveMode < 1.5) return SkydomeTileField(dir);
    return SkydomeWonderTileField(dir);
}

// 計算済みの空色 sky に「燃えるウェーブフロント」を合成して返す。
// 溶けた領域は discard（背面ドームが透ける）。フラグメントの return をこれで置き換える。
//   amount : _Dissolve（呼び出し側から渡す。ヘルパー内で _Dissolve を参照しないための引数）
half4 SkydomeDissolveComposite(float3 sky, float3 worldDir, float2 screenPos, float amount)
{
    if (amount <= 0.0) return half4(sky, 1.0);

    float edge = _SkydomeDissolveEdge > 0.0 ? _SkydomeDissolveEdge : 0.08;

    // 外側ハロー分の余白も含めて最後まで溶かし切れるよう amount を拡張
    float a = amount * (1.0 + edge * 2.0);

    float field = SkydomeDissolveField(worldDir, screenPos);

    // 完全に溶けた領域：背面ドームを見せる
    if (field < a - edge)
    {
        clip(-1);
        return half4(sky, 1.0);
    }

    // 溶け際(front = a-edge)からの距離。0=front直上, 大きいほど空側
    float d = field - (a - edge);

    // 各層の強度（front 近傍で最大 → 外側へ減衰）
    float core = saturate(1.0 - d / (edge * 0.6));   // 白熱コア（細い）
    float body = saturate(1.0 - d / (edge * 1.2));   // 炎本体
    float halo = saturate(1.0 - d / (edge * 2.4));   // 外側ハロー（空へにじむ）

    float3 cMain = _SkydomeDissolveColor.a  > 0.0 ? _SkydomeDissolveColor.rgb  : float3(1.6, 0.35, 1.2);
    float3 cHot  = _SkydomeDissolveColor2.a > 0.0 ? _SkydomeDissolveColor2.rgb : float3(2.4, 1.6, 0.6);

    // 火の粉（エンバー）：高周波ノイズをしきい値化して front 帯だけに散らし、時間で瞬かせる
    float scale = _SkydomeDissolveScale > 0.0 ? _SkydomeDissolveScale : 3.0;
    float2 ep = _SkydomeProjUV(normalize(worldDir)) * (scale * 2.0) + float2(_Time.y * 0.3, -_Time.y * 0.2);
    float en = _SkydomeFbm(ep);
    float ember = smoothstep(0.62, 0.72, en) * body;
    ember *= 0.55 + 0.45 * sin(_Time.y * 8.0 + en * 30.0);

    // すべて加算（HDR → Bloom で光る）
    float3 glow = 0;
    glow += cMain * (body * body) * 1.6;   // 炎本体
    glow += cHot  * (core * core) * 3.5;   // 白熱コア
    glow += cHot  * ember        * 5.0;    // 火の粉
    glow += cMain * (halo * halo) * 0.35;  // 空へにじむハロー

    sky += glow;
    return half4(sky, 1.0);
}

#endif // SKYDOME_DISSOLVE_INCLUDED
