/**===========================================================================

 File   : シール帳BookPanelビルダー [StickerBookBuilder.cs]

 Author : Hayato Otsuki
 Date   : 2026/7/8
 -----------------------------------------------------------------------------

 画面イメージ_シール帳.png のデザインに合わせ、Canvas/BookPanel を
 提供スプライトから再構築するエディタツール。
   メニュー: Tools > シール帳 > BookPanel リメイク
 本体・タブ・ページビュー・説明ボックス等を作り直し、
 StickerBookUI / ClothesSwipeUI を配線して衣装カタログを投入する。
 何度でも再実行できる（毎回まっさらに作り直す）。

===========================================================================**/

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class StickerBookBuilder {

    const string DIR      = "Assets/_Project/Sprites/UI/HUD/シール帳/";
    const string ITEM_HAT = "Assets/_Project/Sprites/UI/Item_Hat.png";
    const string FONT     = "Assets/_Project/Fonts/RabbitandFullMoon SDF.asset";

    // 画面基準（1920x1080, 中心アンカー）
    const float SW = 1920f, SH = 1080f;

    [MenuItem("Tools/シール帳/BookPanel リメイク")]
    public static void Build() {
        // --- 使用スプライトを Sprite インポートに揃える -----------------------
        string[] files = {
            "Sticker_Book_back_UI.png","Sticker_Book_L_UI.png","Sticker_Book_R_UI.png",
            "Sticker_Heading_UI.png","Sticker_Textbox_UI.png","Sticker_Backicon_UI.png",
            "Sticker_Changeicon_back_UI.png","Sticker_Page_on_UI.png","Sticker_Page_off_UI.png",
            "Sticker_scrollbar_UI.png","Sticker_scrollbar_back_UI.png",
            "Sticker_Tab_change_on_UI.png","Sticker_Tab_change_off_UI.png",
            "Sticker_Tab_item_on_UI.png","Sticker_Tab_item_off_UI.png",
            "Sticker_Tab_skill_on_UI.png","Sticker_Tab_skill_off_UI.png",
            "Sticker_Clothes_01_UI.png","Sticker_Clothes_02_UI.png",
            "Sticker_Clothes_03_UI.png","Sticker_Clothes_04_UI.png",
        };
        foreach (var f in files) EnsureSprite(DIR + f);
        EnsureSprite(ITEM_HAT);
        AssetDatabase.Refresh();

        // --- Canvas / StickerBookUI を探す ----------------------------------
        var bookUI = Object.FindObjectOfType<StickerBookUI>();
        if (bookUI == null) {
            EditorUtility.DisplayDialog("シール帳ビルダー",
                "シーン内に StickerBookUI が見つかりません。Stage_1 を開いてから実行してください。", "OK");
            return;
        }
        var canvas = bookUI.gameObject;

        // Canvas に紛れ込んだ旧 ClothesSwipeUI は撤去（PageView 側へ移すため）
        var stray = canvas.GetComponent<ClothesSwipeUI>();
        if (stray != null) Object.DestroyImmediate(stray);

        // --- BookPanel を用意して中身を全消し --------------------------------
        Transform bpT = canvas.transform.Find("BookPanel");
        GameObject bookPanel = bpT != null ? bpT.gameObject : NewGO("BookPanel", canvas.transform);
        var bpImg = bookPanel.GetComponent<Image>() ?? bookPanel.AddComponent<Image>();
        var bpRT  = (RectTransform)bookPanel.transform;
        bpRT.anchorMin = Vector2.zero; bpRT.anchorMax = Vector2.one;
        bpRT.offsetMin = Vector2.zero; bpRT.offsetMax = Vector2.zero;
        bpRT.pivot = new Vector2(0.5f, 0.5f);
        bpRT.localRotation = Quaternion.identity; bpRT.localScale = Vector3.one;
        bpImg.color = new Color(0f, 0f, 0f, 0f);   // 透明だがクリックはブロック
        bpImg.raycastTarget = true;
        for (int i = bookPanel.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(bookPanel.transform.GetChild(i).gameObject);

        // ===================================================================
        //  静的チャコール（奥→手前の順で生成）
        // ===================================================================

        // 本のベイク傾き（バッキングに合わせた約6.4°時計回り）
        const float TILT = -6.4f;

        // 黒バッキング（傾きはスプライトにベイク済みなので回転なし）
        Img("Book_Back", bpT_(bookPanel), S("Sticker_Book_back_UI"), 1127, 543, 1550, 1073, 0, false);
        // 左ページ（白）：バッキングに合わせて傾ける
        Img("Page_Left",  bpT_(bookPanel), S("Sticker_Book_L_UI"), 757, 545, 720, 955, TILT, false);
        // 右ページ（ラベンダー）：バッキングに合わせて傾ける
        Img("Page_Right", bpT_(bookPanel), S("Sticker_Book_R_UI"), 1562, 545, 715, 1010, TILT, false);

        // スクロールバー（装飾）
        Img("Scrollbar_Back", bpT_(bookPanel), S("Sticker_scrollbar_back_UI"), 78, 770, 30, 340, 0, false);
        Img("Scrollbar",      bpT_(bookPanel), S("Sticker_scrollbar_UI"),      78, 655, 22, 96,  0, false);

        // ===================================================================
        //  左ページのページビュー（ClothesSwipeUI）
        // ===================================================================
        var pageView = NewGO("PageView", bookPanel.transform);   // フルスクリーン容器
        var pvRT = (RectTransform)pageView.transform;
        pvRT.anchorMin = Vector2.zero; pvRT.anchorMax = Vector2.one;
        pvRT.offsetMin = Vector2.zero; pvRT.offsetMax = Vector2.zero;
        var swipe = pageView.AddComponent<ClothesSwipeUI>();

        // Viewport（マスク・ドラッグ受け）。傾けるので RectMask2D ではなく
        // Mask（ステンシル）を使う。RectMask2D は回転すると AABB で切れて崩れる。
        var viewport = NewGO("Viewport", pageView.transform);
        var vpImg = viewport.AddComponent<Image>();
        vpImg.color = Color.white; vpImg.raycastTarget = true;   // Mask 用に不透明（描画はしない）
        var vpMask = viewport.AddComponent<Mask>();
        vpMask.showMaskGraphic = false;
        Center((RectTransform)viewport.transform, 757, 530, 700, 880, TILT);
        // ScrollContainer（実体はランタイム生成）
        var scroll = NewGO("ScrollContainer", viewport.transform);
        var scRT = (RectTransform)scroll.transform;
        scRT.anchorMin = scRT.anchorMax = new Vector2(0f, 0.5f);
        scRT.pivot = new Vector2(0f, 0.5f); scRT.sizeDelta = new Vector2(700, 880);
        scRT.anchoredPosition = Vector2.zero;

        // ドット親（下部・横並び）
        var dotParent = NewGO("DotParent", pageView.transform);
        Center((RectTransform)dotParent.transform, 812, 1002, 200, 54, TILT);
        var hlg = dotParent.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter; hlg.spacing = 14;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        // ===================================================================
        //  右ページ：説明ボックス＋戻るヒント
        // ===================================================================
        Img("DescBox", bpT_(bookPanel), S("Sticker_Textbox_UI"), 1595, 905, 615, 250, TILT, false);
        var descText = Text("DescText", bookPanel.transform, "", 1585, 900, 545, 180, 30,
                            TextAlignmentOptions.TopLeft);
        descText.rectTransform.localRotation = Quaternion.Euler(0, 0, TILT);

        // 戻るヒント（矢印＋Tabキーキャップ）
        Img("BackIcon", bpT_(bookPanel), S("Sticker_Backicon_UI"), 1642, 648, 120, 112, 0, true);
        var capBG = NewGO("TabCapBG", bookPanel.transform);
        var capImg = capBG.AddComponent<Image>();
        capImg.color = new Color(0.06f, 0.06f, 0.06f, 1f); capImg.raycastTarget = false;
        Center((RectTransform)capBG.transform, 1618, 735, 84, 48);
        Text("TabCapText", bookPanel.transform, "Tab", 1618, 733, 84, 48, 28, TextAlignmentOptions.Center);

        // ===================================================================
        //  見出し
        // ===================================================================
        Img("Heading", bpT_(bookPanel), S("Sticker_Heading_UI"), 282, 132, 505, 185, 0, true);

        // ===================================================================
        //  カテゴリタブ（CHANGE / ITEM / SKILL）
        // ===================================================================
        var tabChange = MakeTab("Tab_Change", bookPanel.transform, S("Sticker_Tab_change_off_UI"), 240, 392);
        var tabItem   = MakeTab("Tab_Item",   bookPanel.transform, S("Sticker_Tab_item_off_UI"),   240, 628);
        var tabSkill  = MakeTab("Tab_Skill",  bookPanel.transform, S("Sticker_Tab_skill_off_UI"),   240, 858);

        // ===================================================================
        //  ClothesSwipeUI の配線
        // ===================================================================
        var so = new SerializedObject(swipe);
        SetObj(so, "viewport",        viewport.GetComponent<RectTransform>());
        SetObj(so, "scrollContainer", scroll.GetComponent<RectTransform>());
        SetObj(so, "dotParent",       dotParent.transform);
        SetObj(so, "dotOnSprite",     S("Sticker_Page_on_UI"));
        SetObj(so, "dotOffSprite",    S("Sticker_Page_off_UI"));
        SetVec(so, "dotOnSize",  new Vector2(34, 34));
        SetVec(so, "dotOffSize", new Vector2(22, 22));
        SetObj(so, "hangerSprite", S("Sticker_Changeicon_back_UI"));
        SetVec(so, "cellSize",    new Vector2(250, 360));
        SetVec(so, "cellSpacing", new Vector2(70, 44));
        so.FindProperty("gridYOffset").floatValue = 18f;
        SetVec(so, "stickerFill", new Vector2(0.80f, 0.72f));
        so.FindProperty("hangerStickerDrop").floatValue = 0.05f;
        so.ApplyModifiedPropertiesWithoutUndo();

        // ===================================================================
        //  StickerBookUI の配線
        // ===================================================================
        var bo = new SerializedObject(bookUI);
        SetObj(bo, "bookPanel", bookPanel);
        SetObj(bo, "pageView",  swipe);
        SetObj(bo, "descriptionText", descText);
        bo.FindProperty("useHangerForClothes").boolValue = true;
        bo.FindProperty("emptyDescription").stringValue  = "";

        // タブ配列
        var tabsProp = bo.FindProperty("tabs");
        tabsProp.arraySize = 3;
        WriteTab(tabsProp.GetArrayElementAtIndex(0), StickerType.Clothes, tabChange,
                 S("Sticker_Tab_change_on_UI"), S("Sticker_Tab_change_off_UI"));
        WriteTab(tabsProp.GetArrayElementAtIndex(1), StickerType.Item, tabItem,
                 S("Sticker_Tab_item_on_UI"), S("Sticker_Tab_item_off_UI"));
        WriteTab(tabsProp.GetArrayElementAtIndex(2), StickerType.Skill, tabSkill,
                 S("Sticker_Tab_skill_on_UI"), S("Sticker_Tab_skill_off_UI"));

        // 衣装カタログ（クリックで実際に着せ替わるよう Cloth プレハブを結び付ける）
        var cat = bo.FindProperty("costumeCatalog");
        cat.arraySize = 4;
        WriteCostume(cat.GetArrayElementAtIndex(0), "いつものふく",
            "はじめから着ていた標準的な服。\n意外と脱ぎ着はしやすいらしい。",
            S("Sticker_Clothes_01_UI"), LoadCloth("Cloth_Default"));
        WriteCostume(cat.GetArrayElementAtIndex(1), "しましまふく",
            "やんちゃな縞模様のいしょう。",
            S("Sticker_Clothes_02_UI"), null);   // 対応プレハブ未作成（見た目のみ）
        WriteCostume(cat.GetArrayElementAtIndex(2), "メイドふく",
            "きちんとした給仕のよそおい。",
            S("Sticker_Clothes_03_UI"), LoadCloth("Cloth_Alice"));
        WriteCostume(cat.GetArrayElementAtIndex(3), "じょおうのドレス",
            "威厳ただよう真紅のドレス。",
            S("Sticker_Clothes_04_UI"), LoadCloth("Cloth_Queen"));
        bo.ApplyModifiedPropertiesWithoutUndo();

        // 初期タブ見た目（CHANGE を on）
        tabChange.GetComponent<Image>().sprite = S("Sticker_Tab_change_on_UI");

        EditorSceneManager.MarkSceneDirty(bookPanel.scene);
        Selection.activeGameObject = bookPanel;
        Debug.Log("[StickerBookBuilder] BookPanel を再構築しました。");
    }

    // =======================================================================
    //  ヘルパー
    // =======================================================================

    static Transform bpT_(GameObject bookPanel) => bookPanel.transform;

    static Sprite S(string file) {
        string path = file.EndsWith(".png") ? file : DIR + file + ".png";
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static void EnsureSprite(string path) {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool changed = false;
        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
        if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
        if (changed) ti.SaveAndReimport();
    }

    static GameObject NewGO(string name, Transform parent) {
        var go = new GameObject(name, typeof(RectTransform));
        int ui = LayerMask.NameToLayer("UI");
        if (ui >= 0) go.layer = ui;
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Center(RectTransform rt, float cx, float cy, float w, float h, float rotZ = 0) {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(cx - SW * 0.5f, SH * 0.5f - cy);
        rt.localRotation = Quaternion.Euler(0, 0, rotZ);
        rt.localScale = Vector3.one;
    }

    static Image Img(string name, Transform parent, Sprite sp, float cx, float cy, float w, float h,
                     float rot = 0, bool preserve = true) {
        var go = NewGO(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = sp; img.preserveAspect = preserve; img.raycastTarget = false;
        Center((RectTransform)go.transform, cx, cy, w, h, rot);
        return img;
    }

    static TMP_Text Text(string name, Transform parent, string content, float cx, float cy, float w, float h,
                         float size, TextAlignmentOptions align) {
        var go = NewGO(name, parent);
        var t = go.AddComponent<TextMeshProUGUI>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT);
        if (font != null) t.font = font;
        t.text = content; t.fontSize = size; t.color = Color.white;
        t.alignment = align; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.Normal;
        Center((RectTransform)go.transform, cx, cy, w, h);
        return t;
    }

    static GameObject MakeTab(string name, Transform parent, Sprite off, float cx, float cy) {
        var go = NewGO(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = off; img.preserveAspect = true; img.raycastTarget = true;
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = img;
        Center((RectTransform)go.transform, cx, cy, 232, 145);
        return go;
    }

    static void WriteTab(SerializedProperty e, StickerType cat, GameObject tab, Sprite on, Sprite off) {
        e.FindPropertyRelative("category").enumValueIndex = (int)cat;
        e.FindPropertyRelative("button").objectReferenceValue    = tab.GetComponent<Button>();
        e.FindPropertyRelative("image").objectReferenceValue     = tab.GetComponent<Image>();
        e.FindPropertyRelative("onSprite").objectReferenceValue  = on;
        e.FindPropertyRelative("offSprite").objectReferenceValue = off;
    }

    static void WriteCostume(SerializedProperty e, string name, string desc, Sprite sp, GameObject equipPrefab) {
        e.FindPropertyRelative("displayName").stringValue = name;
        e.FindPropertyRelative("description").stringValue = desc;
        e.FindPropertyRelative("sprite").objectReferenceValue = sp;
        e.FindPropertyRelative("equipPrefab").objectReferenceValue = equipPrefab;
        e.FindPropertyRelative("stickerData").objectReferenceValue = null;
    }

    // 着せ替え用の服プレハブを読み込む（Assets/_Project/Prefabs/Player/<name>.prefab）
    const string CLOTH_DIR = "Assets/_Project/Prefabs/Player/";
    static GameObject LoadCloth(string name) {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(CLOTH_DIR + name + ".prefab");
        if (go == null) Debug.LogWarning("[StickerBookBuilder] 服プレハブ未検出: " + name);
        return go;
    }

    static void SetObj(SerializedObject so, string prop, Object val) {
        var p = so.FindProperty(prop);
        if (p != null) p.objectReferenceValue = val;
        else Debug.LogWarning("[StickerBookBuilder] プロパティ未検出: " + prop);
    }

    static void SetVec(SerializedObject so, string prop, Vector2 v) {
        var p = so.FindProperty(prop);
        if (p != null) p.vector2Value = v;
    }
}
