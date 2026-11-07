/**===========================================================================

 File   : ステージ材質のオクルージョン対応一括変換 [OccludableMaterialMigrator.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/5
 -----------------------------------------------------------------------------
 カメラオクルージョン透過 (_OcclusionFade / 自動シリンダー透過) はシェーダー
 PettanMaker/Environment/OccludablePolka を使うマテリアルにしか効かない。
 本ツールはステージの環境マテリアルをまとめて OccludablePolka へ移行する。

 ■ 対象
   1. 単体 .mat アセット (Stylised Nature の幹・石・葉・草、山、巨大樹)
      → シェーダーをその場で差し替え、テクスチャ/色を引き継ぐ。
      → 葉はカットアウト+風、草は風+色ムラを有効化。
   2. FBX 埋め込みマテリアル (Cube_* / Fence_* / Bridge_* / 城 など)
      → 外部 .mat を生成して ModelImporter の Material Remap に登録。
        (生成先: Assets/_Project/Materials/Stage_01/Occludable/)

 ■ 使い方
   メニュー [PettanMaker > Occlusion > Migrate Stage Materials To OccludablePolka]
   何度実行しても安全 (変換済みはスキップ / 生成済みマテリアルは再利用)。
============================================================================*/

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class OccludableMaterialMigrator
{
    const string PolkaShaderName = "PettanMaker/Environment/OccludablePolka";
    const string GeneratedDir    = "Assets/_Project/Materials/Stage_01/Occludable";

    // その場でシェーダー差し替えする単体マテリアル。値は (cutout, wind, heightGrad)
    // heightGrad = UV.y の根元→先端グラデーション (草カード用。風の高さマスクも UV.y 基準になる)
    static readonly Dictionary<string, (bool cutout, bool wind, bool heightGrad)> StandaloneMats =
        new Dictionary<string, (bool, bool, bool)>
    {
        // 幹・石 (URP/Lit)
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/LrgTrunkMat1.mat", (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/LrgTrunkMat2.mat", (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/MedTrunkMat1.mat", (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/MedTrunkMat2.mat", (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/SmlTrunkMat1.mat", (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/SmlTrunkMat2.mat", (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/Stones.mat",       (false, false, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/RockMoss.mat",     (false, false, false) },
        // 葉 (ShaderGraph LeafShader) : カットアウト + 風
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/TreeLeaf1.mat",    (true, true, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/TreeLeaf2.mat",    (true, true, false) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/TreeLeaf3.mat",    (true, true, false) },
        // 草・茂み (ShaderGraph GrassShader) : カットアウト + 風 + 高さグラデ
        // (草はαで葉形を抜くカードメッシュ。ベタ板になるのでカットアウト必須)
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/GrassMat.mat",     (true, true, true) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/BushMat.mat",      (true, true, true) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/GrassWheat.mat",   (true, true, true) },
        { "Assets/_Project/Model_Beta/Stage/Data/Stylised Nature/Materials/TallGrassMat.mat", (true, true, true) },
        // 背景の山 (URP/Lit)
        { "Assets/_Project/Data/Mountains/M_Mountain_0.mat", (false, false, false) },
        { "Assets/_Project/Data/Mountains/M_Mountain_1.mat", (false, false, false) },
        { "Assets/_Project/Data/Mountains/M_Mountain_2.mat", (false, false, false) },
        { "Assets/_Project/Data/Mountains/M_Mountain_3.mat", (false, false, false) },
        // 巨大樹 Kyodaiju (Toon Shaders Pro)
        { "Assets/_Project/Materials/Stage_01/Tree.mat", (false, false, false) },
    };

    // 埋め込みマテリアルを外部化してリマップする FBX
    static readonly string[] RemapFbxPaths =
    {
        "Assets/_Project/Model_Beta/Stage/Data/Cube_Grass_Single.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Cube_Dirt_Single.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Cube_Default.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Cube_Bricks.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Fence_Middle.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Fence_Corner.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Bridge_Modular.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Bridge_Modular_Center.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/Arrow_Side.fbx",
        "Assets/_Project/Model_Beta/Stage/Data/tent_center.fbx",
        "Assets/_Project/Model_Beta/Stage/Queen_Chasle.fbx",
    };

    [MenuItem("PettanMaker/Occlusion/Migrate Stage Materials To OccludablePolka")]
    public static void Migrate()
    {
        Shader polka = Shader.Find(PolkaShaderName);
        if (polka == null)
        {
            Debug.LogError($"[OccludableMigrator] シェーダーが見つからない: {PolkaShaderName}");
            return;
        }

        int converted = 0, skipped = 0, remapped = 0;

        // ---- 1) 単体マテリアルをその場で差し替え ----
        foreach (var kv in StandaloneMats)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kv.Key);
            if (mat == null) { Debug.LogWarning($"[OccludableMigrator] 見つからない: {kv.Key}"); continue; }
            if (mat.shader == polka) { skipped++; continue; }

            var (tex, baseCol, colorB) = ExtractLook(mat);

            Undo.RecordObject(mat, "Migrate to OccludablePolka");
            mat.shader = polka;
            ApplyLook(mat, tex, baseCol, colorB, kv.Value.cutout, kv.Value.wind, kv.Value.heightGrad);
            EditorUtility.SetDirty(mat);
            converted++;
            Debug.Log($"[OccludableMigrator] 変換: {kv.Key} (tex={(tex ? tex.name : "none")}, col={baseCol})");
        }

        // ---- 2) FBX 埋め込みマテリアルを外部化してリマップ ----
        if (!AssetDatabase.IsValidFolder(GeneratedDir))
        {
            string parent = System.IO.Path.GetDirectoryName(GeneratedDir).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(GeneratedDir));
        }

        foreach (string fbxPath in RemapFbxPaths)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) { Debug.LogWarning($"[OccludableMigrator] FBX が見つからない: {fbxPath}"); continue; }

            string fbxName = System.IO.Path.GetFileNameWithoutExtension(fbxPath);
            var embedded = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>().ToArray();
            bool changed = false;

            var existingRemaps = importer.GetExternalObjectMap();

            foreach (var em in embedded)
            {
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), em.name);
                if (existingRemaps.TryGetValue(id, out var cur) && cur is Material curMat && curMat != null && curMat.shader == polka)
                { skipped++; continue; }

                var (tex, baseCol, _) = ExtractLook(em);

                string outPath = $"{GeneratedDir}/{fbxName}_{Sanitize(em.name)}.mat";
                var outMat = AssetDatabase.LoadAssetAtPath<Material>(outPath);
                if (outMat == null)
                {
                    outMat = new Material(polka);
                    AssetDatabase.CreateAsset(outMat, outPath);
                }
                else
                {
                    outMat.shader = polka;
                }
                ApplyLook(outMat, tex, baseCol, null, false, false, false);
                EditorUtility.SetDirty(outMat);

                importer.AddRemap(id, outMat);
                changed = true;
                remapped++;
                Debug.Log($"[OccludableMigrator] リマップ: {fbxName}/{em.name} → {outPath} (col={baseCol})");
            }

            if (changed)
            {
                importer.SaveAndReimport();
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[OccludableMigrator] 完了: 単体変換 {converted} 件, リマップ {remapped} 件, スキップ {skipped} 件");
    }

    // 変換前マテリアルから見た目 (アルベドテクスチャ / ベース色 / 2色目) を吸い出す。
    // ShaderGraph はプロパティ参照名が自動生成 (Texture2D_XXXX 等) なので、
    // 表示名 (description) ベースで探し、見つからなければ「値が入っている最初の
    // テクスチャ / 最初のカラー」を採用する。
    static (Texture tex, Color baseCol, Color? colorB) ExtractLook(Material mat)
    {
        Shader s = mat.shader;
        int count = ShaderUtil.GetPropertyCount(s);

        Texture tex = null;
        var colors = new List<(string desc, Color col)>();

        for (int i = 0; i < count; i++)
        {
            string name = ShaderUtil.GetPropertyName(s, i);
            string desc = ShaderUtil.GetPropertyDescription(s, i) ?? name;
            var type = ShaderUtil.GetPropertyType(s, i);

            if (type == ShaderUtil.ShaderPropertyType.TexEnv)
            {
                var t = mat.GetTexture(name);
                if (t == null) continue;
                bool preferred = desc.Contains("Albedo") || desc.Contains("Base") || name == "_MainTex" || name == "_BaseMap";
                if (tex == null || preferred) tex = t;
            }
            else if (type == ShaderUtil.ShaderPropertyType.Color)
            {
                colors.Add((desc, mat.GetColor(name)));
            }
        }

        Color baseCol = Color.white;
        Color? colorB = null;
        if (colors.Count > 0)
        {
            // "Color" / "Base" を優先。無ければ宣言順の先頭
            int baseIdx = colors.FindIndex(c => c.desc == "Color" || c.desc.Contains("Base"));
            if (baseIdx < 0) baseIdx = 0;
            baseCol = colors[baseIdx].col;

            int bIdx = colors.FindIndex(c => c.desc.Contains("Color 2") || c.desc.Contains("Color2"));
            if (bIdx >= 0 && bIdx != baseIdx) colorB = colors[bIdx].col;
        }
        return (tex, baseCol, colorB);
    }

    // OccludablePolka へ見た目を書き込み、用途別の機能トグルを設定する
    static void ApplyLook(Material mat, Texture tex, Color baseCol, Color? colorB,
                          bool cutout, bool wind, bool heightGrad)
    {
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_BaseColor", baseCol);

        SetKeyword(mat, "_ALPHATEST_ON", cutout);
        mat.SetFloat("_AlphaClip", cutout ? 1f : 0f);
        if (cutout) mat.SetFloat("_Cutoff", 0.5f);

        SetKeyword(mat, "_WIND_ON", wind);
        mat.SetFloat("_Wind", wind ? 1f : 0f);
        if (wind)
        {
            // 草 (heightGrad) は根元固定で先端 (UV.y=1) ほど揺らす。葉は樹冠全体をゆったり
            mat.SetFloat("_WindHeightMask", heightGrad ? 1f : 0f);
            mat.SetFloat("_WindStrength", heightGrad ? 0.1f : 0.06f);
        }

        // 草: 根元=_BaseColor → 先端=_ColorB の高さグラデーション
        SetKeyword(mat, "_HEIGHT_GRADIENT_ON", heightGrad && colorB.HasValue);
        mat.SetFloat("_HeightGradient", heightGrad && colorB.HasValue ? 1f : 0f);
        SetKeyword(mat, "_COLOR_VARIATION_ON", false);
        mat.SetFloat("_ColorVariation", 0f);
        if (colorB.HasValue) mat.SetColor("_ColorB", colorB.Value);

        // 葉・草のカードポリゴンは両面描画
        mat.SetFloat("_Cull", (cutout || wind) ? 0f : 2f);
    }

    static void SetKeyword(Material mat, string keyword, bool on)
    {
        if (on) mat.EnableKeyword(keyword);
        else    mat.DisableKeyword(keyword);
    }

    static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
