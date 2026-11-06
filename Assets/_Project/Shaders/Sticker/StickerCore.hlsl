#ifndef STICKER_CORE_INCLUDED
#define STICKER_CORE_INCLUDED

// =====================================================================
//  Sticker 共通コア  ―  剥がし(Peel) / 貼り付け(Apply) 兼用の純粋計算
//  --------------------------------------------------------------------
//  UI(UGUI) と World の両シェーダーから include する。パイプライン依存の
//  マクロやテクスチャサンプルは持たず、数値計算のみなので Built-in CG /
//  URP どちらの CGPROGRAM/HLSLPROGRAM でもコンパイルできる。
//
//  ・_Peel : 折り目(クリース)の進行度。1=完全にめくれ / 0=完全に密着。
//            「貼り付け(Apply)」は _Peel を 1→0 へ流すだけ(方向は _Dir で反転)。
//  ・Apply 追加要素 : 接着シマー(Shimmer) と シワ(Wrinkle)。
// =====================================================================

#define STICKER_PI 3.14159265

// ---- 手続きバリューノイズ (ディゾルブ / シワ用・2オクターブ) ----
float Sticker_hash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}
float Sticker_vnoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Sticker_hash21(i);
    float b = Sticker_hash21(i + float2(1, 0));
    float c = Sticker_hash21(i + float2(0, 1));
    float d = Sticker_hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// ---- 剥がし方向 dir へ UV(0-1) を射影し [0,1] 正規化した座標 sN。0=最初に剥がれる角 ----
float Sticker_ProjectSN(float2 uv, float2 dir, out float span)
{
    float p00 = 0.0, p10 = dir.x, p01 = dir.y, p11 = dir.x + dir.y;
    float pmin = min(min(p00, p10), min(p01, p11));
    float pmax = max(max(p00, p10), max(p01, p11));
    span = max(pmax - pmin, 1e-4);
    return (dot(uv, dir) - pmin) / span;
}

// ---- フラグメント折り返しマスク(UI 用フェイクフォールド) ----
struct StickerFold
{
    float  mFlat;     // 平らに貼り付いた面
    float  mFlap;     // 折り返った三角(裏面)
    float  mRevealed; // めくれて下地が見える部分
    float  sN;
    float  c;         // 折り目(クリース)位置 = _Peel
    float  span;
    float2 dir;
};
StickerFold Sticker_ComputeFold(float2 uv, float2 dir, float peel)
{
    float span;
    float sN   = Sticker_ProjectSN(uv, dir, span);
    float c    = peel;
    float twoC = 2.0 * c;
    float aa   = fwidth(sN) * 1.5 + 1e-4;

    float mRevealed = 1.0 - smoothstep(c - aa, c + aa, sN);      // sN < c
    float mBeyond   = smoothstep(twoC - aa, twoC + aa, sN);      // sN > 2c
    float mFlap     = saturate((1.0 - mRevealed) - mBeyond);     // c < sN < 2c

    StickerFold f;
    f.mFlat = mBeyond; f.mFlap = mFlap; f.mRevealed = mRevealed;
    f.sN = sN; f.c = c; f.span = span; f.dir = dir;
    return f;
}

// 折り目で反射した裏面サンプル UV
float2 Sticker_FoldUV(StickerFold f, float2 uv)
{
    return uv + f.dir * (2.0 * (f.c - f.sN) * f.span);
}

// 裏面(フラップ)の色: 裏紙色 + 絵のにじみ + 自由端ハイライト + 立体陰影
float3 Sticker_FlapColor(StickerFold f, float3 backImg, float3 backColor,
                         float backImageMix, float backDark, float3 rimColor)
{
    float twoC = 2.0 * f.c;
    float3 flapCol = lerp(backColor, backImg, backImageMix) * backDark;
    float  freeEdge = smoothstep(twoC, twoC - 0.04, f.sN);
    flapCol = lerp(flapCol, rimColor, freeEdge * 0.5);
    flapCol *= lerp(0.82, 1.0, smoothstep(f.c, f.c + 0.06, f.sN));
    return flapCol;
}

// ---- Apply 用: 接着シマー(クリースに沿って走る光の帯)。_Peel が動くと帯が掃引する ----
float Sticker_Shimmer(float sN, float creaseC, float width)
{
    float band = 1.0 - saturate(abs(sN - creaseC) / max(width, 1e-3));
    return band * band;
}

// ---- Apply 用: シワ(まだ密着していない側に出る折り目ノイズ)。中心 0 の符号付き ----
float Sticker_Wrinkle(float2 uv, float scale)
{
    return (Sticker_vnoise(uv * scale) * 0.6 + Sticker_vnoise(uv * scale * 2.3) * 0.4) - 0.5;
}

// ---- World 用: 頂点を円筒ページカールへ変形 ----
//   xy    : 板ローカル(XY±0.5)の頂点
//   出力  : xyOut=変形後XY, lift=持ち上げ量(表=−Z へ), curl=立ち上がり陰影 0..1
void Sticker_CurlVertex(float2 xy, float2 dir, float peel, float curlRadius,
                        out float2 xyOut, out float lift, out float curl)
{
    float halfSpan = 0.5 * (abs(dir.x) + abs(dir.y));
    float span = 2.0 * halfSpan;
    float R = max(curlRadius, 1e-3);
    float crease = -halfSpan + peel * (span + STICKER_PI * R);
    float s = dot(xy, dir);
    float d = crease - s;               // 折り目からどれだけ剥がれた側か (>0 で剥離)

    curl = 0.0; lift = 0.0; xyOut = xy;
    if (d > 0.0)
    {
        float theta = d / R;
        float sNew;
        if (theta < STICKER_PI) { sNew = crease - R * sin(theta); lift = R * (1.0 - cos(theta)); curl = sin(min(theta, STICKER_PI * 0.5)); }
        else                    { sNew = crease + (d - STICKER_PI * R); lift = 2.0 * R; curl = 0.35; }
        xyOut = xy + dir * (sNew - s);
    }
}

#endif // STICKER_CORE_INCLUDED
