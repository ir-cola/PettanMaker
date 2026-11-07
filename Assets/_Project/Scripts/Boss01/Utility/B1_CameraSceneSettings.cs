using System;
using UnityEngine;

public enum B1_CameraSceneKind
{
    Entrance = 0,
    Engage = Entrance,
    Phase2 = 1,
    Defeat = 2,
}

/// <summary>
/// Boss01 のカメラシーン1本分の共通設定。作成メニューは Entrance / Phase2 / Defeat の
/// 専用型だけにする。カメラの移動そのものと、ビート(Appear/Roar/Title)発火タイミングは
/// PlayableDirector(Timeline)側で作るため、ここには持たない(演出のタイミングを
/// 変えたい場合は Timeline ウィンドウ上で調整する)。
///
/// ボスカメラ設定の共通読み取り口(抽象)。実体は種類ごとの
/// B1_EntranceCameraSettings / B1_Phase2CameraSettings / B1_DefeatCameraSettings が、
/// その種類に必要なフィールドだけを持つ。共通のグループ型(Fog/UI/Shockwave/Reveal)は
/// ここに [Serializable] で定義し、各サブクラスが必要な分だけ使う。
/// B1_CameraTimelineBridge がここを読んで Fog/UI/衝撃波/登場ディゾルブを駆動する。
/// </summary>
public abstract class B1_CameraSceneSettings : ScriptableObject
{
    [Serializable]
    public class FogSettings
    {
        public Color fogColor = new Color(0.72f, 0.55f, 0.95f, 1f);
        public float heightBase = -2.5f;
        public float heightFalloff = 0.18f;
        public float heightStrength = 1.2f;
        public float distanceStart = 3f;
        public float distanceEnd = 42f;
        public float distanceStrength = 1.0f;
        public bool restoreFogOnFinish = false;

        public static FogSettings For(B1_CameraSceneKind kind) => kind switch
        {
            // Phase2 / Defeat は戦闘継続中なので薄く、演出後は元へ戻さない設定を既定に。
            B1_CameraSceneKind.Phase2 => new FogSettings
            {
                heightStrength = 0.6f, distanceStrength = 0.5f, restoreFogOnFinish = true,
            },
            B1_CameraSceneKind.Defeat => new FogSettings
            {
                fogColor = new Color(0.85f, 0.62f, 0.98f, 1f),
                heightStrength = 0.9f, distanceStrength = 0.7f, restoreFogOnFinish = false,
            },
            _ => new FogSettings(),
        };
    }

    /// <summary>
    /// カメラ演出中に前面へ重ねる UI 一式。ボス名タイトルと各種オーバーレイに加え、
    /// テキストエフェクト(傾き/タイプライター/ポップイン/シェイク/ゆらぎ)を
    /// 個別に ON/OFF できる。
    /// </summary>
    [Serializable]
    public class UISettings
    {
        [Header("Boss Name")]
        public bool showBossName = false;
        [Tooltip("ボス名タイトルの表示時間 (秒)。0 なら titleHold に追従。>0 でカメラ演出と独立にフェードイン→保持→フェードアウトする")]
        public float titleDuration = 0f;
        public string bossName = "チェシャ猫";

        [Header("Boss Name Sticker")]
        [Tooltip("設定時はボス名テキストの代わりに、この Texture をシールとして表示する")]
        public Texture bossNameTexture;
        [Tooltip("BossName貼付用のMaterial。Sticker/UI互換Materialを指定する")]
        public Material bossNameStickerMaterial;
        [Tooltip("Texture を既存の UIStickerPaste で画面外から貼り付ける")]
        public bool pasteBossNameTexture = true;
        [Tooltip("シールが飛んでくる画面外の方向。この方向側の角が最後に密着する")]
        public Vector2 stickerFromDirection = new Vector2(-1f, 1f);
        [Tooltip("ボス名シールが貼られるまでの時間 (秒)")]
        [Min(0.01f)] public float stickerPasteDuration = 0.7f;
        [Tooltip("ボス体力ゲージのシールが貼られるまでの時間 (秒)")]
        [Min(0.01f)] public float healthStickerPasteDuration = 0.7f;
        [Range(0f, 1f)] public float stickerPeel = 0.42f;
        [Min(0.01f)] public float stickerOvershoot = 1.12f;
        public float stickerTilt = 9f;
        public Color stickerColor = Color.white;
        public bool preserveStickerAspect = true;

        [Header("Sticker Settle Effect")]
        [Tooltip("貼り終わりに潰れ・反動・減衰揺れで定着感を出す")]
        public bool stickerSettleEffect = true;
        [Min(0.01f)] public float stickerSettleDuration = 0.28f;
        [Range(0f, 0.3f)] public float stickerSettleScale = 0.09f;
        [Min(0f)] public float stickerSettleShake = 7f;

        [Header("Position / Layout")]
        [Tooltip("タイトルの画面上の中心位置(ビューポート 0..1)。(0.5, 0.5)=中央")]
        public Vector2 titleAnchor = new Vector2(0.5f, 0.425f);
        [Tooltip("タイトル表示領域の幅(画面比 0..1)")]
        [Range(0.1f, 1f)] public float titleAreaWidth = 0.8f;
        [Tooltip("タイトル表示領域の高さ(画面比 0..1)")]
        [Range(0.05f, 1f)] public float titleAreaHeight = 0.25f;
        [Tooltip("ピクセル単位の追加オフセット(x=右, y=上)。1920x1080 基準")]
        public Vector2 titlePixelOffset = Vector2.zero;
        [Tooltip("BossName UI の表示倍率(x=横, y=縦)")]
        public Vector2 titleScale = Vector2.one;
        [Tooltip("BossName UI の基本回転角度")]
        public float titleRotation;

        [Header("Text Effects (each on/off)")]
        [Tooltip("表示時に少しランダムな傾きを付ける")]
        public bool randomTilt = false;
        [Tooltip("傾きの最大角 (±度)")]
        public float randomTiltAngle = 8f;
        [Tooltip("1文字ずつ送って表示する")]
        public bool typewriter = false;
        [Tooltip("1秒あたりの表示文字数")]
        public float typewriterCharsPerSecond = 20f;
        [Tooltip("表示時に大きめから弾んで出る")]
        public bool popIn = false;
        [Tooltip("ポップイン開始スケール(オーバーシュート量)")]
        public float popInScale = 1.35f;
        [Tooltip("ポップインに掛ける時間 (秒)")]
        public float popInDuration = 0.35f;
        [Tooltip("表示中こまかく揺れる")]
        public bool shake = false;
        [Tooltip("シェイクの振れ幅 (px)")]
        public float shakeIntensity = 6f;
        [Tooltip("表示中ゆっくり回転して揺らめく")]
        public bool wobble = false;
        [Tooltip("ゆらぎの最大角 (±度)")]
        public float wobbleAngle = 4f;
        [Tooltip("ゆらぎの速さ")]
        public float wobbleSpeed = 2f;

        [Header("Overlays")]
        public bool showConvergenceLines = false;
        public int convergenceLineCount = 44;
        public Color convergenceLineColor = new Color(1f, 1f, 1f, 0.55f);
        public bool showDistortionOverlay = false;
        public Material distortionOverlayMaterial;

        public static UISettings For(B1_CameraSceneKind kind) => kind switch
        {
            // ボス名タイトルは Entrance のみ既定 ON。Phase2 / Defeat はデータで必要なら有効化。
            B1_CameraSceneKind.Phase2 => new UISettings { showBossName = false },
            B1_CameraSceneKind.Defeat => new UISettings { showBossName = false },
            _ => new UISettings { showBossName = true, titleDuration = 2.5f, popIn = true, typewriter = true },
        };
    }

    /// <summary>Entrance Orbit中に画面上下へ表示するシネマティック黒帯。</summary>
    [Serializable]
    public class LetterboxSettings
    {
        [Tooltip("ONでEntrance Orbit中に上下の黒帯を表示する")]
        public bool enabled = true;
        [Tooltip("帯の色")]
        public Color barColor = Color.black;
        [Tooltip("帯1本の高さ（画面高に対する割合）")]
        [Range(0f, 0.35f)] public float barHeight = 0.115f;
        [Tooltip("演出開始時に帯が開く時間（秒）")]
        [Min(0f)] public float enterDuration = 0.45f;
        [Tooltip("演出終了時に帯が閉じる時間（秒）")]
        [Min(0f)] public float exitDuration = 0.35f;

        [Header("Luxury Accent")]
        [Tooltip("黒帯の内側にアクセントラインを表示する")]
        public bool showAccentLine = true;
        public Color accentColor = new Color(0.85f, 0.67f, 0.28f, 0.9f);
        [Tooltip("アクセントラインの太さ（1920x1080基準px）")]
        [Min(0f)] public float accentThickness = 3f;
    }

    /// <summary>
    /// Roar ビートで出す衝撃波の調整。波数・間隔・2D/3D・1波の見た目をまとめる。
    /// director が <see cref="ToParams"/> で B1_ShockwaveDistortion へ渡す。
    /// </summary>
    [Serializable]
    public class ShockwaveSettings
    {
        [Header("波数 / タイミング")]
        [Tooltip("出す衝撃波の波数。0 で衝撃波なし")]
        [Min(0)] public int count = 1;
        [Tooltip("複数波のときの1波ごとの発生間隔(秒)")]
        [Min(0f)] public float interval = 0.12f;
        [Tooltip("発生位置の高さ。注視基準(LookHeight)に加算するオフセット")]
        public float heightOffset = 0f;

        [Header("3D Roar")]
        [Tooltip("ON: ボスを中心に立体Sphere波を生成。OFF: 従来のカメラ向きQuad")]
        public bool use3D = true;
        [Tooltip("3D Roar用Material。未設定時は専用Shaderから自動生成")]
        public Material material3D;
        [Tooltip("3D波の軸別倍率。Yを小さくすると横に広がる咆哮になる")]
        public Vector3 scale3D = new Vector3(1f, 0.65f, 1f);

        [Header("1波の見た目")]
        [Tooltip("1波の寿命(秒)。小さいほど速い")]
        public float duration = 0.6f;
        [Tooltip("歪みの最大量")]
        public float strength = 0.035f;
        [Tooltip("開始スケール")]
        public float startScale = 2.5f;
        [Tooltip("最大スケール")]
        public float endScale = 11f;
        [Tooltip("リング幅")]
        [Range(0.02f, 0.6f)] public float ringWidth = 0.16f;
        [Tooltip("縁の発光色")]
        [ColorUsage(true, true)] public Color rimColor = new Color(0.82f, 0.6f, 1f, 1f);
        [Tooltip("縁の発光強度")]
        public float rimIntensity = 0.6f;

        /// <summary>1波ぶんの見た目パラメータへ変換する。</summary>
        public B1_ShockwaveDistortion.Params ToParams() => new B1_ShockwaveDistortion.Params
        {
            duration     = duration,
            strength     = strength,
            startScale   = startScale,
            endScale     = endScale,
            ringWidth    = ringWidth,
            rimColor     = rimColor,
            rimIntensity = rimIntensity,
            use3D        = use3D,
            material     = material3D,
            scale3D      = scale3D,
        };
    }

    /// <summary>
    /// Roar ビートのカメラシェイク調整。B1_CameraTimelineBridge.OnRoar（Signal）が
    /// <see cref="CameraShake"/> へ渡す。この設定を持つシーンでは、B1_PostProcessFX 側の
    /// 固定値シェイクは抑制され、こちらが唯一のシェイク源になる。
    /// </summary>
    [Serializable]
    public class CameraShakeSettings
    {
        [Tooltip("OFF で Roar 時にカメラを揺らさない。")]
        public bool enabled = true;
        [Tooltip("揺れ時間(秒)。")]
        [Min(0f)] public float duration = 0.6f;
        [Tooltip("揺れ最大幅(m)。0.05〜0.3 あたりが目安。")]
        [Min(0f)] public float intensity = 0.35f;
        [Tooltip("揺れ速度。20〜40 あたりが目安。")]
        [Min(1f)] public float frequency = 22f;
    }

    /// <summary>
    /// Roar 瞬間の「タメ→ドン」を作るインパクト演出の調整。B1_CameraTimelineBridge.OnRoar（Signal）が
    /// <see cref="GlobalHitStop"/>（全体を一瞬止める）と <see cref="CameraSnapZoom"/>（FOV パンチイン）へ渡す。
    /// ColorDrain（予兆）→ ヒットストップ（静止）→ 衝撃波、の順で流れると効果が最大になる。
    /// </summary>
    [Serializable]
    public class RoarImpactSettings
    {
        [Tooltip("OFF で Roar 時のヒットストップ/スナップズームを出さない。")]
        public bool enabled = true;

        [Header("ヒットストップ（インパクトフレーム）")]
        [Tooltip("全体(Time.timeScale)を止める時間(秒)。0.06〜0.12 あたりが目安。0 で止めない。")]
        [Min(0f)] public float hitStopDuration = 0.09f;
        [Tooltip("止めている間の timeScale。0.01 でほぼ完全静止。")]
        [Range(0.01f, 1f)] public float hitStopScale = 0.05f;

        [Header("スナップズーム（FOV パンチイン）")]
        [Tooltip("FOV を狭める角度(度)。2〜5 あたりが目安。0 でズームしない。")]
        [Min(0f)] public float snapZoomFov = 3.5f;
        [Tooltip("狭めるまでの時間(秒)。短いほど「スナップ」感が出る。")]
        [Min(0f)] public float snapZoomIn = 0.04f;
        [Tooltip("狭めたまま保持する時間(秒)。ヒットストップと重ねると静止画が締まる。")]
        [Min(0f)] public float snapZoomHold = 0.06f;
        [Tooltip("元へ戻す時間(秒)。in より長めにすると余韻が出る。")]
        [Min(0.01f)] public float snapZoomOut = 0.28f;
    }

    /// <summary>
    /// Roar 直前に画面の色を一瞬抜く演出の調整。B1_CameraTimelineBridge.OnColorDrain（Signal）から
    /// <see cref="B1_ScreenColorDrain"/> へ渡す。Roar Signal の少し手前にビートを置くと「予兆」になる。
    /// </summary>
    [Serializable]
    public class ColorDrainSettings
    {
        [Tooltip("OFF で色抜きを出さない。")]
        public bool enabled = true;

        [Header("タイミング（上から順に流れる）")]
        [Tooltip("色が抜けきるまでの時間(秒)。")]
        public float drainIn = 0.08f;
        [Tooltip("抜けた状態を保持する時間(秒)。この間に Roar を当てると効果的。")]
        public float hold = 0.12f;
        [Tooltip("元の色へ戻るまでの時間(秒)。")]
        public float release = 0.35f;

        [Header("見た目")]
        [Tooltip("最大色抜き量。")]
        [Range(0f, 1f)] public float peak = 1f;
        [Tooltip("彩度ダウンの強さ。1 で完全モノクロ。")]
        [Range(0f, 1f)] public float maxDesaturation = 1f;
        [Tooltip("色抜き中のわずかな減光。")]
        [Range(0f, 1f)] public float darken = 0.12f;
        [Tooltip("色抜き中に薄く掛ける乗算ティント。")]
        public Color tint = new Color(0.75f, 0.72f, 0.85f, 1f);
        [Tooltip("ティントの適用量。")]
        [Range(0f, 1f)] public float tintAmount = 0.25f;

        /// <summary>1回ぶんのパラメータへ変換する。</summary>
        public B1_ScreenColorDrain.Params ToParams() => new B1_ScreenColorDrain.Params
        {
            drainIn         = drainIn,
            hold            = hold,
            release         = release,
            peak            = peak,
            maxDesaturation = maxDesaturation,
            darken          = darken,
            tint            = tint,
            tintAmount      = tintAmount,
        };
    }

    /// <summary>
    /// 登場ボス出現の「順番付きタイムライン」。各フェーズの尺(秒)を上から順に並べる。
    /// ① 煙(不可視) → ② 透明フェードイン → ③ 透明保持 → ④ ディゾルブ → ⑤ 戦闘Idle。
    /// 開始時刻・合計は自動計算（Start○○ / RevealDuration / TotalDuration）。戦闘(Phase1)は
    /// ④ 完了（RevealDuration）まで待ってから始まる。
    /// </summary>
    [Serializable]
    public class RevealSequence
    {
        [Tooltip("① 煙だけを出す（ボスは完全に不可視）")]
        public float smoke = 4f;
        [Tooltip("② 完全不可視 → 透明(クローク)を有機ノイズでフェードイン")]
        public float cloakFadeIn = 3f;
        [Tooltip("③ 透明(クローク)状態で保持")]
        public float transparentHold = 1.5f;
        [Tooltip("④ 透明 → B1_Toon へ相補ディゾルブ")]
        public float dissolve = 4f;
        [Tooltip("⑤ 戦闘開始後、攻撃せず Idle で待つ（ディゾルブ完了後）")]
        public float combatStartIdle = 1.5f;
        [Tooltip("②④ のディゾルブ進行カーブ")]
        public AnimationCurve dissolveCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        private static float NN(float v) => Mathf.Max(0f, v);

        // 各フェーズの開始時刻（出現ビート基準, 秒）
        public float CloakFadeInStart     => NN(smoke);
        public float TransparentHoldStart => CloakFadeInStart + NN(cloakFadeIn);
        public float DissolveStart        => TransparentHoldStart + NN(transparentHold);
        /// <summary>煙〜ディゾルブ完了までの尺。戦闘はここまで待つ。</summary>
        public float RevealDuration       => DissolveStart + NN(dissolve);
        /// <summary>戦闘Idle 猶予まで含めた総尺。</summary>
        public float TotalDuration        => RevealDuration + NN(combatStartIdle);
    }

    /// <summary>
    /// 登場（Entrance）のボス出現演出まわりを一元化した設定。
    /// 時間は <see cref="RevealSequence"/> に順番付きで集約。B1_Boss / B1_EffectManager がここから読む。
    /// </summary>
    [Serializable]
    public class RevealSettings
    {
        [Header("出現方式")]
        [Tooltip("ON: 煙→透明クローク→B1_Toon のディゾルブ登場。OFF: 隠し→ポップ出現。")]
        public bool useDissolveReveal = true;

        [Header("タイムライン（上から順に流れる）")]
        public RevealSequence sequence = new();

        [Header("ディゾルブ / クローク")]
        [Tooltip("登場クローク用マテリアル（InvisibleDistortion 等）。未設定なら B1_EffectManager の invisibleMaterial を使用。")]
        public Material cloakMaterial;
        [Tooltip("有機ノイズの粗さ。実ボス B1_Toon の _DissolveScale と同じ値にする（相補整合）。")]
        public float dissolveNoiseScale = 3f;
        [Tooltip("クロークを実メッシュより手前に描くための renderQueue オフセット。")]
        public int ghostQueueOffset = 50;

        [Header("煙")]
        public B1_EntranceSmokeVFX.Settings smoke = new();
    }

    // --- 共通の読み取り口。サブクラスは必要なものだけ実装する ---
    public abstract B1_CameraSceneKind SceneKind { get; }
    public abstract float LookHeight { get; }  // 注視/出現の基準高さ(衝撃波の高さ等に使う)
    public virtual FogSettings Fog => null;     // 持たない種類は null
    public virtual UISettings UI => null;
    public virtual LetterboxSettings Letterbox => null; // Entrance Orbit用シネマティック黒帯
    public virtual ShockwaveSettings Shockwave => null; // Roar 衝撃波（持たない種類は null）
    public virtual CameraShakeSettings RoarShake => null; // Roar カメラシェイク（持たない種類は null）
    public virtual RoarImpactSettings RoarImpact => null; // Roar ヒットストップ/スナップズーム（持たない種類は null）
    public virtual ColorDrainSettings ColorDrain => null; // Roar 直前の色抜き（持たない種類は null）
    public virtual RevealSettings Reveal => null;   // 登場演出（Entrance のみ）

    // アセット未割り当て時のランタイム既定(種類に応じた具象型を生成)。
    public static B1_CameraSceneSettings CreateRuntimeDefault(B1_CameraSceneKind kind) => kind switch
    {
        B1_CameraSceneKind.Phase2 => CreateInstance<B1_Phase2CameraSettings>(),
        B1_CameraSceneKind.Defeat => CreateInstance<B1_DefeatCameraSettings>(),
        _ => CreateInstance<B1_EntranceCameraSettings>(),
    };
}
