#ifndef B1_DISSOLVE_INCLUDED
#define B1_DISSOLVE_INCLUDED

// SkydomeBlendSystem_Noise と同じ思想の「有機ノイズ・ディゾルブ」共通ヘルパー。
// B1_Toon / InvisibleDistortion など複数シェーダーで同じ溶け模様・燃えるウェーブフロントを共有する。
//
// 呼び出し側(各パス)が用意するもの:
//   float _Dissolve;            // 0=通常, 1=完全に溶けた
//   float _DissolveEdge;        // 燃焼エッジ帯の幅
//   float _DissolveScale;       // 有機ノイズのスケール(SkydomeBlendSystem_Noise の noiseScale 相当)
//   float4 _DissolveEdgeColor;  // 炎本体・ハローの主色(HDR)
//   float4 _DissolveEmberColor; // 白熱コア・火の粉の副色(HDR)
//   float _OcclusionFade;       // カメラオクルージョン(従来のスクリーンディザ)。無ければ 0 を渡す。
//
// 使い方:
//   B1_DissolveClip(posWS, screenPx, _Dissolve, _DissolveEdge, _DissolveScale, _OcclusionFade); // 全パス
//   final += B1_DissolveGlow(posWS, screenPx, _Dissolve, _DissolveEdge, _DissolveScale,
//                            _DissolveEdgeColor, _DissolveEmberColor);                          // カラーパスのみ

float B1_DisHash(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float B1_DisNoise(float2 uv)
{
    float2 id = floor(uv);
    float2 f  = frac(uv);
    float2 u  = f * f * (3.0 - 2.0 * f);
    float a = B1_DisHash(id);
    float b = B1_DisHash(id + float2(1.0, 0.0));
    float c = B1_DisHash(id + float2(0.0, 1.0));
    float d = B1_DisHash(id + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

float B1_DisFbm(float2 p)
{
    float v = 0.0, a = 0.5;
    [unroll]
    for (int k = 0; k < 4; k++) { v += a * B1_DisNoise(p); p *= 2.0; a *= 0.5; }
    return v;
}

// 2枚の FBM 平均は理論値域 [0,0.9375] に対し実測では 99% が [0.26,0.66] に集中する
// (正規分布状: 平均 0.47 / σ≈0.086)。生値のまま boundary = amount*(1+2*edge)-edge と
// 比較すると amount 0.3〜0.65 の区間でしか溶けず、両端 1/3 が「何も起きない死に時間」になる。
// smoothstep でヒストグラムを均し、field が [0,1] を線形に埋めるようにする
// (正規分布の CDF は smoothstep でよく近似できる)。amount と溶け具合がほぼ比例する。
#define B1_DIS_FIELD_LO 0.28
#define B1_DIS_FIELD_HI 0.66

// 3軸をミックスして球面/縞にならない有機ノイズの「溶け進行度フィールド」[0,1]。
float B1_DissolveField(float3 posWS, float2 screenPx, float scale)
{
    scale = scale > 0.0 ? scale : 3.0;
    float n1 = B1_DisFbm(posWS.xz * scale + posWS.y * scale);
    float n2 = B1_DisFbm(posWS.yz * scale - posWS.x * scale);
    float field = smoothstep(B1_DIS_FIELD_LO, B1_DIS_FIELD_HI, (n1 + n2) * 0.5);
    field += (B1_DisHash(screenPx) - 0.5) * 0.02;   // スクリーン微小ディザで境界のジャギ軽減
    return saturate(field);
}

// 全パス共通のクリップ。カメラオクルージョン(スクリーンディザ)＋有機ノイズディゾルブ。
// invert=0: amount 0→1 で「消える」。invert=1: amount 0→1 で「現れる」(相補)。
// 同じ場・同じ amount で invert 違いの2面を描けば、各ピクセルは常に片方だけが見える
// (二重に出ない / 隙間も出ない クロスフェード)。
void B1_DissolveClip(float3 posWS, float2 screenPx, float amount, float edge, float scale, float occlusion, float invert)
{
    // カメラオクルージョン用ディザクリップ(従来挙動を維持)
    float od = frac(52.9829189 * frac(dot(screenPx, float2(0.06711056, 0.00583715))));
    clip(od - occlusion);

    // 完全表示なら以降スキップ(FBM 計算も省く)。
    //   invert=0(消える): amount≈0 が完全表示 / invert=1(現れる): amount≈1 が完全表示
    // 注意: invert=1 の amount≈0 は「完全に隠す」なのでスキップしてはいけない
    // (ここを amount>0 だけで早期リターンすると、出現側が amount=0 で丸見えになる = B1_Toon フラッシュのバグ)。
    bool fullyVisible = (invert > 0.5) ? (amount >= 0.9999) : (amount <= 0.0001);
    if (fullyVisible) return;

    // 有機ノイズディゾルブ
    edge = edge > 0.0 ? edge : 0.12;
    float front = amount * (1.0 + edge * 2.0);   // ハロー分まで溶かし切る
    float field = B1_DissolveField(posWS, screenPx, scale);
    float boundary = front - edge;
    // invert=0: field<boundary を隠す(消える) / invert=1: field>=boundary を隠す(現れる)
    bool hide = (invert > 0.5) ? (field >= boundary) : (field < boundary);
    if (hide) clip(-1);
}

// 溶け際の「燃えるウェーブフロント」(白熱コア/炎本体/ハロー/火の粉)。HDR 加算 → Bloom で光る。
// invert は B1_DissolveClip と揃える(現れる側でも溶け際が光る)。
half3 B1_DissolveGlow(float3 posWS, float2 screenPx, float amount, float edge, float scale,
                      half4 edgeColor, half4 emberColor, float invert)
{
    if (amount <= 0.0001) return (half3)0.0;

    edge = edge > 0.0 ? edge : 0.12;
    float front = amount * (1.0 + edge * 2.0);
    float field = B1_DissolveField(posWS, screenPx, scale);
    float boundary = front - edge;
    // 表示側での溶け際からの距離(invert で符号を反転)
    float dd = (invert > 0.5) ? (boundary - field) : (field - boundary);

    float core = saturate(1.0 - dd / (edge * 0.6));    // 白熱コア(細い)
    float body = saturate(1.0 - dd / (edge * 1.2));    // 炎本体
    float halo = saturate(1.0 - dd / (edge * 2.4));    // 外側ハロー

    half3 cMain = edgeColor.a  > 0.0 ? edgeColor.rgb  : half3(1.6, 0.35, 1.2);
    half3 cHot  = emberColor.a > 0.0 ? emberColor.rgb : half3(2.4, 1.6, 0.6);

    // 火の粉(エンバー)：高周波ノイズをしきい値化して front 帯だけに散らし時間で瞬かせる
    float escale = (scale > 0.0 ? scale : 3.0) * 2.0;
    float en = B1_DisFbm(posWS.xz * escale + _Time.y * 0.3);
    float ember = smoothstep(0.62, 0.72, en) * body;
    ember *= 0.55 + 0.45 * sin(_Time.y * 8.0 + en * 30.0);

    return cMain * (body * body) * 1.6
         + cHot  * (core * core) * 3.5
         + cHot  * ember         * 5.0
         + cMain * (halo * halo) * 0.35;
}

#endif // B1_DISSOLVE_INCLUDED
