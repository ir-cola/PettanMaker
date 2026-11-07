using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// マナバーストクロス（Boss01 TailAttack後の十字地面爆発）の VFX プレハブを
/// Kennys_Particles のテクスチャから自動生成するエディタツール。
///
/// メニュー [Tools > Boss01 > Build ManaBurst VFX] を実行すると:
///  1. 加算合成マテリアルを Materials/Boss01_ManaBurst/ に生成
///     （加算系は _BaseColor を HDR ブーストして Bloom で発光させる）
///  2. パーティクル7層（螺旋の渦・点火の火の粉・地を走る火花・魔力の奔流・
///     魔力コア・トレイル火花・燃えかす・煙）のプレハブを Prefabs/Boss01/ に生成。
///     大判ビルボードは使わず、粒の群れと渦運動だけで爆発を表現する。
///  3. 爆発跡: scorch テクスチャのアルファを高さと見なして Sobel フィルタで
///     ノーマルマップをプロシージャル生成し、GroundScar 専用シェーダのマテリアルを作る
///  4. B1_EffectSettings.asset の ManaBurst に プレハブと爆発跡マテリアルを自動アサイン
/// 再実行すると既存の生成物を作り直す（手動で調整した場合は上書きされるので注意）。
/// </summary>
public static class B1_ManaBurstVFXBuilder
{
    private const string TexDir      = "Assets/_Project/Textures/Kennys_Particles/";
    private const string MatDir      = "Assets/_Project/Materials/Boss01_ManaBurst";
    private const string PrefabPath  = "Assets/_Project/Prefabs/Boss01/B1_ManaBurstVFX.prefab";
    private const string SettingsPath = "Assets/_Project/Data/Bosses/B1_EffectSettings.asset";

    // 加算マテリアルの HDR 発光倍率。1 超で Bloom 閾値を越えて光って見える。
    private const float EmissiveBoost = 2.5f;

    // チェシャ猫の魔力色（TailCharge の紫系に合わせる）
    private static readonly Color OuterPurple = new Color(0.45f, 0.15f, 1f,   1f);
    private static readonly Color InnerPurple = new Color(0.85f, 0.65f, 1f,  1f);
    private static readonly Color SparkPink   = new Color(1f,    0.55f, 0.95f, 1f);
    private static readonly Color SmokePurple = new Color(0.3f,  0.12f, 0.5f, 0.5f);

    [MenuItem("Tools/Boss01/Build ManaBurst VFX（マナバースト生成）")]
    public static void Build()
    {
        // --- 1. マテリアル生成 ---
        EnsureFolder(MatDir);
        Material magicMat = CreateAdditiveMaterial("M_ManaBurst_Magic", TexDir + "magic_01.png");
        Material sparkMat = CreateAdditiveMaterial("M_ManaBurst_Spark", TexDir + "star_07.png");
        Material smokeMat = CreateAlphaMaterial   ("M_ManaBurst_Smoke", TexDir + "smoke_05.png");
        Material flashMat = CreateAdditiveMaterial("M_ManaBurst_Flash", TexDir + "light_01.png");

        // --- 2. プレハブ構築 ---
        var root = new GameObject("B1_ManaBurstVFX");
        try
        {
            // 螺旋を描いて巻き上がる魔力の渦（ルート）。大判の渦ビルボードの代わりに、
            // 小さな魔力片の群れを周回＋上昇＋内向きの吸い込みで回して竜巻を作る。
            // stopAction=Destroy でルートごと自壊し、子も道連れにする。
            // ルートの duration を全層の (発生遅延+寿命) より長くして、先に消えないようにする。
            var vortex = AddParticleLayer(root, null, magicMat, out var vortexMain);
            vortexMain.duration      = 2.6f;
            vortexMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            vortexMain.startSpeed    = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            vortexMain.startSize     = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            vortexMain.startColor    = new ParticleSystem.MinMaxGradient(OuterPurple, InnerPurple);
            vortexMain.maxParticles  = 128;
            vortexMain.stopAction    = ParticleSystemStopAction.Destroy;
            SetBurst(vortex, 36);
            SetGroundCircle(vortex, radius: 1.6f);
            SetSwirlVelocity(vortex, rise: 3f, swirl: 9f, radial: -0.5f); // 速回転＋吸い込みで螺旋が締まる
            FadeAndShrink(vortex);

            // 点火の白い火の粉（爆発の「パッ」)。大判フラッシュの代わりに、全方向へ弾ける極短命の粒で目を引く。
            var flash = AddParticleLayer(root, "FlashSparks", flashMat, out var flashMain);
            flashMain.duration      = 0.3f;
            flashMain.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            flashMain.startSpeed    = new ParticleSystem.MinMaxCurve(10f, 16f);
            flashMain.startSize     = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            flashMain.startColor    = Color.white;
            SetBurst(flash, 18);
            SetUpwardCone(flash, angle: 80f, radius: 0.3f); // ほぼ全方向
            SetColorShift(flash, Color.white, InnerPurple); // 白で点火→魔力色へ
            FadeAndShrink(flash);

            // 地面を走る火花の波（攻撃範囲の「縁」を粒で伝える）。低く速く滑ってトレイルを引く。
            var spray = AddParticleLayer(root, "GroundSpray", sparkMat, out var sprayMain);
            sprayMain.duration        = 0.5f;
            sprayMain.startLifetime   = new ParticleSystem.MinMaxCurve(0.3f, 0.4f);
            sprayMain.startSpeed      = new ParticleSystem.MinMaxCurve(10f, 15f);
            sprayMain.startSize       = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            sprayMain.startColor      = new ParticleSystem.MinMaxGradient(InnerPurple, SparkPink);
            sprayMain.gravityModifier = new ParticleSystem.MinMaxCurve(0.25f); // わずかに沈めて地を這わせる
            sprayMain.maxParticles    = 64;
            SetBurst(spray, 20);
            SetGroundCircle(spray, radius: 0.3f); // 中心から放射状に走る
            SetColorFade(spray);
            AddTrails(spray, sparkMat);

            // 空へ抜ける魔力の奔流（光柱ビルボードの代わり）。速度方向に伸びるストリークの束で
            // 「柱」を描く。遠くからでも「あそこが爆ぜた」と分かる背骨。
            var pillar = AddParticleLayer(root, "Pillar", flashMat, out var pillarMain);
            pillarMain.duration      = 0.5f;
            pillarMain.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            pillarMain.startSpeed    = new ParticleSystem.MinMaxCurve(14f, 20f);
            pillarMain.startSize     = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            pillarMain.startColor    = new ParticleSystem.MinMaxGradient(InnerPurple, SparkPink);
            SetBurst(pillar, 22);
            SetUpwardCone(pillar, angle: 4f, radius: 0.25f); // 細く束ねて柱にする
            SetColorFade(pillar);
            var pillarRenderer = pillar.GetComponent<ParticleSystemRenderer>();
            pillarRenderer.renderMode  = ParticleSystemRenderMode.Stretch; // 速度方向に伸びるストリーク
            pillarRenderer.lengthScale = 4f;

            // 噴き上がる魔力コア（ノイズでゆらゆら歪む＝チェシャ猫の不定形な魔力）
            var core = AddParticleLayer(root, "Core", magicMat, out var coreMain);
            coreMain.duration      = 0.9f;
            coreMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            coreMain.startSpeed    = new ParticleSystem.MinMaxCurve(6f, 11f);
            coreMain.startSize     = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            coreMain.startColor    = new ParticleSystem.MinMaxGradient(InnerPurple, OuterPurple);
            coreMain.gravityModifier = new ParticleSystem.MinMaxCurve(0.6f); // 噴き上がって落ちる魔力片
            coreMain.maxParticles    = 96;
            SetBurst(core, 42);
            SetUpwardCone(core, angle: 18f, radius: 0.5f);
            SetSwirlVelocity(core, rise: 0f, swirl: 3f); // 噴水にひねりを加えて螺旋にする
            FadeAndShrink(core);
            var coreNoise = core.noise;
            coreNoise.enabled    = true;
            coreNoise.strength   = new ParticleSystem.MinMaxCurve(1.3f);
            coreNoise.frequency  = 0.55f;
            coreNoise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.8f);
            coreNoise.damping    = true;

            // 弾ける火花（星）。トレイルで軌跡を引いて「魔力が糸を引く」
            var spark = AddParticleLayer(root, "Sparks", sparkMat, out var sparkMain);
            sparkMain.duration      = 0.7f;
            sparkMain.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            sparkMain.startSpeed    = new ParticleSystem.MinMaxCurve(9f, 15f);
            sparkMain.startSize     = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            sparkMain.startColor    = new ParticleSystem.MinMaxGradient(SparkPink, Color.white);
            sparkMain.gravityModifier = new ParticleSystem.MinMaxCurve(0.8f);
            sparkMain.maxParticles    = 64;
            SetBurst(spark, 24);
            SetUpwardCone(spark, angle: 35f, radius: 0.4f);
            FadeAndShrink(spark);
            AddTrails(spark, sparkMat);

            // 漂う燃えかす（小さな光の粒がふわっと浮かんで消える余韻。負の重力で浮かせる）
            var ember = AddParticleLayer(root, "Embers", flashMat, out var emberMain);
            emberMain.duration       = 1.6f;
            emberMain.startLifetime  = new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            emberMain.startSpeed     = new ParticleSystem.MinMaxCurve(0.5f, 1.6f);
            emberMain.startSize      = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            emberMain.startColor     = new ParticleSystem.MinMaxGradient(InnerPurple, SparkPink);
            emberMain.gravityModifier = new ParticleSystem.MinMaxCurve(-0.06f); // ゆっくり浮かぶ
            SetBurst(ember, 20);
            SetUpwardCone(ember, angle: 60f, radius: 1.0f);
            SetColorFade(ember);
            var emberNoise = ember.noise;
            emberNoise.enabled   = true;
            emberNoise.strength  = new ParticleSystem.MinMaxCurve(0.5f);
            emberNoise.frequency = 0.4f;

            // 足元に残る煙（加算ではなく通常アルファで暗めに。加算だらけの画を締める）
            var smoke = AddParticleLayer(root, "Smoke", smokeMat, out var smokeMain);
            smokeMain.duration      = 1.1f;
            smokeMain.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            smokeMain.startSpeed    = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            smokeMain.startSize     = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            smokeMain.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            smokeMain.startColor    = SmokePurple;
            SetBurst(smoke, 8);
            SetUpwardCone(smoke, angle: 50f, radius: 0.8f);
            FadeAndGrow(smoke, growTo: 1.5f);

            // --- 3. プレハブ保存 & EffectSettings へ自動アサイン ---
            EnsureFolder("Assets/_Project/Prefabs/Boss01");
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);

            // 爆発跡: ノーマルマップをプロシージャル生成して専用シェーダのマテリアルに束ねる
            Material scarMat = BuildScarMaterial();

            var settings = AssetDatabase.LoadAssetAtPath<B1_EffectSettings>(SettingsPath);
            if (settings != null)
            {
                settings.ManaBurst.BurstVFXPrefab = prefab;
                settings.ManaBurst.ScarMaterial   = scarMat;
                EditorUtility.SetDirty(settings);
                Debug.Log($"[ManaBurstVFXBuilder] {SettingsPath} の ManaBurst にプレハブと爆発跡マテリアルをアサインしました。");
            }
            else
            {
                Debug.LogWarning($"[ManaBurstVFXBuilder] {SettingsPath} が見つからないため、プレハブの自動アサインをスキップしました。");
            }

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[ManaBurstVFXBuilder] 生成完了: {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    // ---- パーティクル層の共通セットアップ ----

    // name=null ならルート自身に ParticleSystem を付ける（stopAction=Destroy 用）。
    private static ParticleSystem AddParticleLayer(GameObject root, string name, Material mat, out ParticleSystem.MainModule main)
    {
        GameObject go = name == null ? root : new GameObject(name);
        if (name != null) go.transform.SetParent(root.transform, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 編集中の再生を止めてから設定

        main = ps.main;
        main.loop          = false;
        main.playOnAwake   = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles  = 64;
        main.stopAction    = ParticleSystemStopAction.None;

        var emission = ps.emission;
        emission.rateOverTime = 0f; // バーストのみ

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial   = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows    = false;
        return ps;
    }

    private static void SetBurst(ParticleSystem ps, int count)
    {
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    private static void SetUpwardCone(ParticleSystem ps, float angle, float radius)
    {
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = angle;
        shape.radius    = radius;
        shape.rotation  = new Vector3(-90f, 0f, 0f); // コーンを真上に向ける
    }

    // 地面と平行な円周から放射状に放出（渦の外周・地を走る火花用）
    private static void SetGroundCircle(ParticleSystem ps, float radius)
    {
        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius    = radius;
        shape.rotation  = new Vector3(90f, 0f, 0f); // 円を地面に寝かせる
    }

    // 上昇(rise)＋中心の周りを周回(swirl, rad/s)＋radial(正=外向き/負=吸い込み)の渦運動。
    // ※ velocityOverLifetime の各軸はモード（定数/カーブ）を揃えないと
    //   "Particle Velocity curves must all be in the same mode" エラーになるため、全軸を定数で統一する。
    private static void SetSwirlVelocity(ParticleSystem ps, float rise, float swirl, float radial = 0f)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.x = 0f;
        vel.y = rise;
        vel.z = 0f;
        vel.orbitalX = 0f;
        vel.orbitalY = swirl;
        vel.orbitalZ = 0f;
        vel.radial   = radial;
    }

    // 先細りするトレイルを付ける（火花の軌跡用）
    private static void AddTrails(ParticleSystem ps, Material trailMat)
    {
        var trails = ps.trails;
        trails.enabled              = true;
        trails.lifetime             = new ParticleSystem.MinMaxCurve(0.45f); // 粒子寿命に対する比
        trails.dieWithParticles     = true;
        trails.sizeAffectsWidth     = true;
        trails.inheritParticleColor = true;
        trails.widthOverTrail       = new ParticleSystem.MinMaxCurve(0.35f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        ps.GetComponent<ParticleSystemRenderer>().trailMaterial = trailMat;
    }

    // 寿命でフェードアウト + 縮小（魔力片・火花向け）
    private static void FadeAndShrink(ParticleSystem ps)
    {
        SetColorFade(ps);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.1f));
    }

    // 寿命でフェードアウト + 拡大（渦・煙向け）
    private static void FadeAndGrow(ParticleSystem ps, float growTo)
    {
        SetColorFade(ps);
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, growTo));
    }

    private static void SetColorFade(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.35f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    // 寿命で from→to へ色が転がりつつフェードアウト（点火フラッシュの白→紫用）
    private static void SetColorShift(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 0.4f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.3f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    // ---- マテリアル生成 ----

    private static readonly string[] ShaderCandidates =
    {
        "Universal Render Pipeline/Particles/Unlit",
        "Sprites/Default",
        "Unlit/Transparent",
    };

    // 加算合成（光もの用）
    private static Material CreateAdditiveMaterial(string name, string texPath)
        => CreateParticleMaterial(name, texPath, additive: true);

    // 通常アルファ合成（煙用）
    private static Material CreateAlphaMaterial(string name, string texPath)
        => CreateParticleMaterial(name, texPath, additive: false);

    private static Material CreateParticleMaterial(string name, string texPath, bool additive)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
            Debug.LogWarning($"[ManaBurstVFXBuilder] テクスチャが見つかりません: {texPath}（白抜きで生成します）");

        Shader shader = null;
        foreach (string s in ShaderCandidates)
        {
            shader = Shader.Find(s);
            if (shader != null) break;
        }

        var mat = new Material(shader);
        // URP Particles/Unlit を Transparent + 目的のブレンドへ（シェーダが違ってもプロパティが無ければ無視されるだけ）
        if (mat.HasProperty("_Surface"))   mat.SetFloat("_Surface", 1f);   // Transparent
        if (mat.HasProperty("_SrcBlend"))  mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend"))  mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite"))    mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_BaseMap"))   mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex"))   mat.SetTexture("_MainTex", tex);
        // 加算系は HDR ブーストで Bloom 閾値を越えさせて発光させる（アルファ系は等倍のまま）
        Color baseColor = additive ? Color.white * EmissiveBoost : Color.white;
        baseColor.a = 1f;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        // 地面と交差する粒子の切断線を消す（URP アセットの Depth Texture 有効が前提）
        if (mat.HasProperty("_SoftParticlesEnabled"))
        {
            mat.SetFloat("_SoftParticlesEnabled", 1f);
            mat.EnableKeyword("_SOFTPARTICLES_ON"); // URP 系
            mat.EnableKeyword("SOFTPARTICLES_ON");  // フォールバックシェーダ用
        }
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)RenderQueue.Transparent;

        string path = $"{MatDir}/{name}.mat";
        AssetDatabase.DeleteAsset(path); // 再実行時は作り直し
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ---- 爆発跡（GroundScar）マテリアル生成 ----

    private const string ScarShaderName = "PettanMaker/Boss_01/GroundScar";
    private const string ScarSrcTexPath = TexDir + "scorch_02.png";
    private const string GenTexDir      = "Assets/_Project/Textures/Generated";
    private const string ScarNormalPath = GenTexDir + "/T_ManaScar_Normal.png";

    private static Material BuildScarMaterial()
    {
        var baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(ScarSrcTexPath);
        if (baseTex == null)
        {
            Debug.LogWarning($"[ManaBurstVFXBuilder] {ScarSrcTexPath} が見つからないため爆発跡マテリアルをスキップしました。");
            return null;
        }

        Texture2D normalTex = GenerateNormalMapFromAlpha(baseTex, ScarNormalPath, bumpStrength: 2.5f);

        Shader shader = Shader.Find(ScarShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[ManaBurstVFXBuilder] シェーダ {ScarShaderName} が見つかりません（コンパイル待ちの可能性）。再実行してください。");
            return null;
        }

        var mat = new Material(shader);
        mat.SetTexture("_BaseMap", baseTex);
        if (normalTex != null) mat.SetTexture("_BumpMap", normalTex);

        string path = $"{MatDir}/M_ManaBurst_Scar.mat";
        AssetDatabase.DeleteAsset(path); // 再実行時は作り直し
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>
    /// テクスチャのアルファ（＝跡の形・濃さ）を高さと見なし、Sobel フィルタで
    /// タンジェント空間ノーマルマップを生成して PNG アセットとして保存する。
    /// 元テクスチャが Read/Write 無効でも動くよう RenderTexture 経由で画素を読む。
    /// </summary>
    private static Texture2D GenerateNormalMapFromAlpha(Texture2D src, string dstPath, float bumpStrength)
    {
        // 1. RenderTexture へブリットして CPU 読み出し可能なコピーを作る
        int w = src.width, h = src.height;
        var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var readable = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        readable.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        Color32[] pixels = readable.GetPixels32();
        float[] height = new float[w * h];
        for (int i = 0; i < pixels.Length; i++)
            height[i] = pixels[i].a / 255f; // アルファ＝彫りの深さ

        // 2. Sobel フィルタで勾配 → ノーマル（端はクランプ）
        var normalPixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            int y0 = Mathf.Max(y - 1, 0) * w, y1 = y * w, y2 = Mathf.Min(y + 1, h - 1) * w;
            for (int x = 0; x < w; x++)
            {
                int x0 = Mathf.Max(x - 1, 0), x2 = Mathf.Min(x + 1, w - 1);

                float dx = (height[y0 + x2] + 2f * height[y1 + x2] + height[y2 + x2])
                         - (height[y0 + x0] + 2f * height[y1 + x0] + height[y2 + x0]);
                float dy = (height[y2 + x0] + 2f * height[y2 + x] + height[y2 + x2])
                         - (height[y0 + x0] + 2f * height[y0 + x] + height[y0 + x2]);

                Vector3 n = new Vector3(-dx * bumpStrength, -dy * bumpStrength, 1f).normalized;
                normalPixels[y1 + x] = new Color32(
                    (byte)((n.x * 0.5f + 0.5f) * 255f),
                    (byte)((n.y * 0.5f + 0.5f) * 255f),
                    (byte)((n.z * 0.5f + 0.5f) * 255f),
                    255);
            }
        }

        // 3. PNG として保存し、NormalMap としてインポートさせる
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true);
        outTex.SetPixels32(normalPixels);
        outTex.Apply();
        EnsureFolder(GenTexDir);
        System.IO.File.WriteAllBytes(dstPath, outTex.EncodeToPNG());
        Object.DestroyImmediate(outTex);
        Object.DestroyImmediate(readable);

        AssetDatabase.ImportAsset(dstPath, ImportAssetOptions.ForceUpdate);
        if (AssetImporter.GetAtPath(dstPath) is TextureImporter importer
            && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(dstPath);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf   = System.IO.Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
