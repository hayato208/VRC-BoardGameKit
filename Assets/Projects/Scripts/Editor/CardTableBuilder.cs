using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRCStation = VRC.SDK3.Components.VRCStation;
using VRCUiShape = VRC.SDK3.Components.VRCUiShape;
using VRC.Udon;
using UdonSharp;
using UdonSharpEditor;
using BoardGameKit.Core;
using System.IO;

namespace BoardGameKit.Editor
{
    /// <summary>
    /// 大判カード・扇型スロット空間の幾何パラメータを一元管理する設定データ（SSOT）。
    /// マジックナンバーを完全排除し、スロット数に応じた最適配置の自動計算機能を提供する。
    /// </summary>
    [System.Serializable]
    public class ArcadeFieldConfig
    {
        // --- 共通寸法定数 (SSOT: Single Source of Truth) ---
        public static readonly Vector2 CardDimension = new Vector2(0.70f, 0.98f);
        public static readonly Vector2 GuideFrameDimension = new Vector2(0.72f, 1.00f);
        public static readonly Vector3 TriggerColliderSize = new Vector3(0.72f, 1.05f, 0.30f);

        // --- GUIスライダー許容範囲定数 (Slider Ranges) ---
        public const int MinSlotCount = 1;
        public const int MaxSlotCount = 10;
        public const float MinGapCm = 4.0f;
        public const float MaxGapCm = 20.0f;
        public const float MinTiltAngle = 0.0f;
        public const float MaxTiltAngle = 30.0f;
        public const float MinHeightY = 0.60f;
        public const float MaxHeightY = 1.20f;
        public const float MinFovDeg = 90.0f;
        public const float MaxFovDeg = 160.0f;

        // --- 幾何パラメータ ---
        public int slotCount = 5;            // スロット数
        public int poolCardCount = 20;       // 山札の事前生成プール枚数 (5〜54)
        public float radius = 1.40f;         // プレイヤー中心からの半径 (m)
        public float angleStep = 36.0f;      // 各スロットの展開角度ステップ (度)
        public float tiltAngle = 12.0f;      // 手前見下ろしチルト角 (度)
        public float slotHeightY = 0.85f;    // スロットの基準高さ Y (m)
        public float minBottomGap = 0.08f;   // 最も近づく下端の最小保証隙間 (m)

        /// <summary>
        /// 指定されたスロット数・隙間・視野角制限に応じた最適な幾何パラメータ（半径・角度）を一元数式で自動算出する。
        /// if文による段階的ハードコードを全廃し、連続的な幾何方程式から唯一の解を導出する。
        /// </summary>
        /// <param name="count">スロット枚数 (1〜10)</param>
        /// <param name="customGap">下端の最小保証隙間 (m)</param>
        /// <param name="maxFovDeg">最大全体展開視野角 (度)</param>
        /// <param name="poolCount">山札の事前生成カード枚数 (5〜54)</param>
        public static ArcadeFieldConfig CreateOptimized(int count, float customGap = 0.08f, float maxFovDeg = 140.0f, int poolCount = 20)
        {
            var config = new ArcadeFieldConfig();
            config.slotCount = Mathf.Clamp(count, 1, 10);
            config.poolCardCount = Mathf.Clamp(poolCount, 1, 54);
            config.tiltAngle = 12.0f;
            config.slotHeightY = 0.85f;
            config.minBottomGap = Mathf.Max(customGap, 0.02f); // 最小2cm以上

            if (config.slotCount <= 1)
            {
                config.radius = 1.35f;
                config.angleStep = 0f;
                return config;
            }

            // 1. 下端においてカード枠同士が衝突しないための必要弦長 (幅72cm + 隙間)
            float requiredChord = GuideFrameDimension.x + config.minBottomGap;

            // 2. プレイヤーの最大視野角（maxFovDeg: 約140度）から決まる許容最大ステップ角
            float fovAngleStep = maxFovDeg / (config.slotCount - 1);

            // 3. 至近距離での標準ステップ角（約36度）と視野制限の調和（連続式）
            // 枚数が少ない時は36度基準で中央に自然に集まり、枚数が多い時は視野制限に沿って展開
            float chosenAngleStep = Mathf.Min(36.0f, fovAngleStep);
            config.angleStep = chosenAngleStep;

            // 4. 決定した角度ステップにおいて下端弦長を厳密に成立させる下端半径の幾何逆算:
            //    Chord = 2 * R_bottom * sin(angleStep / 2)  ==>  R_bottom = Chord / (2 * sin(angleStep / 2))
            float halfRad = (chosenAngleStep * 0.5f) * Mathf.Deg2Rad;
            float bottomRadius = requiredChord / (2f * Mathf.Sin(halfRad));

            // 5. チルト角による手前倒れ込み分を加算し、中心高さにおける半径 R を算出
            float tiltOffset = (GuideFrameDimension.y * 0.5f) * Mathf.Sin(config.tiltAngle * Mathf.Deg2Rad);
            config.radius = Mathf.Max(bottomRadius + tiltOffset, 1.35f);

            return config;
        }
    }

    /// <summary>
    /// シーン上に4人対戦用のカードゲームテーブル環境を一括自動生成するエディタ拡張。
    /// ・UdonSharpEditorUtility による100%完全なUdonSharpBehaviourシリアライズ
    /// ・TextMeshPro (TMP) による文字潰れのない高精細UI & ステータスHUD
    /// ・山札・手札への3D直接インタラクト (Udon Interact)
    /// ・VRChatのWorld Space UI仕様 (VRCUiShape + BoxCollider + SendCustomEvent)
    /// ・VRCStation（PlayerMobility = Immobilize）の着席設定
    /// に完全準拠。
    /// </summary>
    public static class CardTableBuilder
    {
        [MenuItem("Tools/VRC-BoardGameKit/Setup 4-Player Table in Scene (From Prefab)")]
        public static void PlaceTableFromPrefab()
        {
            string prefabPath = "Assets/Projects/Prefabs/CardTable_4Players.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            // Prefabが存在しない場合は自動ビルドして保存
            if (prefab == null)
            {
                Debug.Log("[VRC-BoardGameKit] CardTable_4Players.prefab が見つからないため、新規ビルドして保存します...");
                BuildAndSaveTablePrefab();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            }

            if (prefab == null)
            {
                Debug.LogError("[VRC-BoardGameKit] CardTable_4Players.prefab のロードに失敗しました。");
                return;
            }

            // シーン上の既存テーブルを削除
            GameObject existing = GameObject.Find("CardTable_4Players");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            // Prefabからインスタンス化
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(0, 0, 2.0f);
            Undo.RegisterCreatedObjectUndo(instance, "Place Card Table Prefab");
            Selection.activeGameObject = instance;

            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> Prefabからシーンへカードテーブルを配置しました！</color>");
        }

        [MenuItem("Tools/VRC-BoardGameKit/Save Scene Table to Prefab & Dump Transforms")]
        public static void SaveSceneTableToPrefab()
        {
            GameObject table = GameObject.Find("CardTable_4Players");
            if (table == null)
            {
                Debug.LogError("[VRC-BoardGameKit] シーン内に 'CardTable_4Players' が見つかりません。");
                return;
            }

            string prefabsDir = "Assets/Projects/Prefabs";
            if (!Directory.Exists(prefabsDir))
            {
                Directory.CreateDirectory(prefabsDir);
                AssetDatabase.Refresh();
            }

            string tablePrefabPath = $"{prefabsDir}/CardTable_4Players.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(table, tablePrefabPath, InteractionMode.UserAction);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> シーン上の CardTable_4Players を Prefab に上書き保存しました: {tablePrefabPath}</color>");

            // 保存と同時に全Transformをダンプ
            DumpTableHierarchyTransforms();
        }

        [MenuItem("Tools/VRC-BoardGameKit/Dump Table Hierarchy Transforms")]
        public static void DumpTableHierarchyTransforms()
        {
            GameObject table = GameObject.Find("CardTable_4Players");
            if (table == null)
            {
                Debug.LogError("[VRC-BoardGameKit] シーン内に 'CardTable_4Players' が見つかりません。");
                return;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== [VRC-BoardGameKit] CardTable Hierarchy Transforms Dump ===");
            DumpTransformRecursive(table.transform, 0, sb);
            sb.AppendLine("==============================================================");
            Debug.Log(sb.ToString());
        }

        private static void DumpTransformRecursive(Transform current, int depth, System.Text.StringBuilder sb)
        {
            string indent = new string(' ', depth * 2);
            Vector3 pos = current.localPosition;
            Vector3 rot = current.localEulerAngles;
            Vector3 scale = current.localScale;
            sb.AppendLine($"{indent}- {current.name}: Pos({pos.x:F3}, {pos.y:F3}, {pos.z:F3}) Rot({rot.x:F1}, {rot.y:F1}, {rot.z:F1}) Scale({scale.x:F3}, {scale.y:F3}, {scale.z:F3})");

            for (int i = 0; i < current.childCount; i++)
            {
                DumpTransformRecursive(current.GetChild(i), depth + 1, sb);
            }
        }

        [MenuItem("Tools/VRC-BoardGameKit/Re-apply NotoSansJP Font to All TMP in Scene & Prefab")]
        public static void ReapplyNotoSansJPFontToAllTMP()
        {
            string fontPath = "Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset";
            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (jpFont == null)
            {
                Debug.LogError($"[VRC-BoardGameKit] フォントアセットが見つかりません: {fontPath}");
                return;
            }

            // 1. シーン内の全 TMP_Text (非アクティブ含む) を検索して再アタッチ
            TMP_Text[] allSceneTexts = Object.FindObjectsOfType<TMP_Text>(true);
            int sceneCount = 0;
            foreach (TMP_Text txt in allSceneTexts)
            {
                Undo.RecordObject(txt, "Re-apply NotoSansJP Font");
                txt.font = jpFont;
                txt.fontSharedMaterial = jpFont.material;
                EditorUtility.SetDirty(txt);
                sceneCount++;
            }
            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> シーン内の全 {sceneCount} 箇所の TextMeshPro に NotoSansJP-Medium SDF を再アタッチしました！</color>");

            // 2. Prefab内の全 TMP_Text も直接検索して再アタッチ＆保存
            string prefabPath = "Assets/Projects/Prefabs/CardTable_4Players.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                TMP_Text[] prefabTexts = prefab.GetComponentsInChildren<TMP_Text>(true);
                int prefabCount = 0;
                foreach (TMP_Text pTxt in prefabTexts)
                {
                    pTxt.font = jpFont;
                    pTxt.fontSharedMaterial = jpFont.material;
                    EditorUtility.SetDirty(pTxt);
                    prefabCount++;
                }
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssets();
                Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> Prefab内の全 {prefabCount} 箇所の TextMeshPro にも NotoSansJP-Medium SDF を再アタッチして保存しました！</color>");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/VRC-BoardGameKit/Spawn Debug Click Test Buttons in Scene")]
        public static void SpawnDebugClickButtons()
        {
            EnsureAllProgramAssets();

            // 既存のデバッグオブジェクトを削除
            GameObject existing = GameObject.Find("DEBUG_Click_Test_Group");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            GameObject root = new GameObject("DEBUG_Click_Test_Group");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Debug Click Buttons");
            root.transform.position = new Vector3(0, 1.2f, 0.8f); // プレイヤーの目の前

            // 1. 3Dキューブ型インタラクトボタン (赤色Cube)
            GameObject cubeBtn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubeBtn.name = "DEBUG_3D_Cube_Button";
            cubeBtn.transform.SetParent(root.transform, false);
            cubeBtn.transform.localPosition = new Vector3(-0.35f, 0, 0);
            cubeBtn.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

            // 赤色マテリアル
            Material redMat = new Material(Shader.Find("Standard"));
            redMat.color = Color.red;
            cubeBtn.GetComponent<MeshRenderer>().sharedMaterial = redMat;

            // DebugClickButton アタッチ
            DebugClickButton debugUdon = cubeBtn.AddUdonSharpComponent<DebugClickButton>();
            UdonBehaviour udonBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(debugUdon);
            if (udonBacking != null)
            {
                udonBacking.interactText = "【テスト】3Dボタンを押す (Click Test)";
            }

            // 2. 2D Canvas UIボタン
            GameObject canvasObj = new GameObject("DEBUG_UI_Canvas");
            canvasObj.transform.SetParent(root.transform, false);
            canvasObj.transform.localPosition = new Vector3(0.35f, 0, 0);
            canvasObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<VRCUiShape>();

            RectTransform canvasRt = canvasObj.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(30f, 20f);

            // UIボタン
            GameObject btnObj = new GameObject("DEBUG_UI_Button");
            btnObj.transform.SetParent(canvasObj.transform, false);
            btnObj.transform.localPosition = Vector3.zero;

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0f, 0.7f, 0.3f, 1f); // 緑色
            img.raycastTarget = true;

            Button btn = btnObj.AddComponent<Button>();
            RectTransform btnRt = btnObj.GetComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(28f, 18f);

            BoxCollider btnCol = btnObj.AddComponent<BoxCollider>();
            btnCol.size = new Vector3(28f, 18f, 0.2f);
            btnCol.isTrigger = true;

            DebugClickButton uiDebugUdon = btnObj.AddUdonSharpComponent<DebugClickButton>();
            UdonBehaviour udonUIBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(uiDebugUdon);
            if (udonUIBacking != null)
            {
                udonUIBacking.interactText = "【テスト】UIボタンを押す (UI Click Test)";
            }
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, uiDebugUdon.OnButtonClick);

            // ボタンテキスト
            GameObject textObj = new GameObject("Text (TMP)");
            textObj.transform.SetParent(btnObj.transform, false);
            textObj.transform.localPosition = Vector3.zero;
            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset");
            if (jpFont != null) { tmp.font = jpFont; tmp.fontSharedMaterial = jpFont.material; }
            tmp.text = "UI CLICK\nTEST";
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.fontSize = 4.5f;
            tmp.raycastTarget = false;
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.sizeDelta = new Vector2(28f, 18f);

            Selection.activeGameObject = root;
            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> デバッグ用クリック検知ボタン（3Dキューブ＆UIボタン）をスポーン位置の正面に生成しました！</color>");
        }

        [MenuItem("Tools/VRC-BoardGameKit/Rebuild & Save Table Prefabs (From Code Defaults)")]
        public static void BuildAndSaveTablePrefab()
        {
            // 0. 全UdonSharpスクリプトのProgramAssetを自動確保・同期
            EnsureAllProgramAssets();

            string prefabsDir = "Assets/Projects/Prefabs";
            if (!Directory.Exists(prefabsDir))
            {
                Directory.CreateDirectory(prefabsDir);
                AssetDatabase.Refresh();
            }

            // 既存のテーブルがあれば削除
            GameObject existing = GameObject.Find("CardTable_4Players");
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            // マテリアルの確保・自動生成
            Material cardFrontMat = GetOrCreateCardMaterial("CardFront_Default", new Color(0.96f, 0.96f, 0.94f, 1f));
            Material cardBackMat = GetOrCreateCardMaterial("CardBack_Default", new Color(0.65f, 0.12f, 0.15f, 1f));

            // 1. ルートオブジェクトの生成
            GameObject root = new GameObject("CardTable_4Players");
            Undo.RegisterCreatedObjectUndo(root, "Create Card Table");
            root.transform.position = new Vector3(0, 0, 2.0f); // スポーン正面

            // 2. テーブル天板の生成
            GameObject tableTop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tableTop.name = "TableTop";
            tableTop.transform.SetParent(root.transform, false);
            tableTop.transform.localPosition = new Vector3(0, 0.7f, 0);
            tableTop.transform.localScale = new Vector3(2.0f, 0.05f, 2.0f);

            // テーブル脚
            GameObject tableLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tableLeg.name = "TableLeg";
            tableLeg.transform.SetParent(root.transform, false);
            tableLeg.transform.localPosition = new Vector3(0, 0.35f, 0);
            tableLeg.transform.localScale = new Vector3(0.3f, 0.35f, 0.3f);

            // 3. 山札オブジェクト (Deck) - 3D直接インタラクト (ユーザー調整値: 中央 0, 0.75, 0)
            GameObject deckObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deckObj.name = "DeckObject";
            deckObj.transform.SetParent(root.transform, false);
            deckObj.transform.localPosition = new Vector3(0, 0.75f, 0f);
            deckObj.transform.localScale = new Vector3(0.12f, 0.05f, 0.18f);

            // 捨て札オブジェクト (Discard)
            GameObject discardObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            discardObj.name = "DiscardArea";
            discardObj.transform.SetParent(root.transform, false);
            discardObj.transform.localPosition = new Vector3(0.22f, 0.73f, 0.15f);
            discardObj.transform.localScale = new Vector3(0.12f, 0.01f, 0.18f);

            // 4. コアコンポーネントのアタッチ (UdonSharpComponentExtensions 経由)
            DeckManager deckManager = root.AddUdonSharpComponent<DeckManager>();
            TableManager tableManager = root.AddUdonSharpComponent<TableManager>();
            TableUIController uiController = root.AddUdonSharpComponent<TableUIController>();

            // DeckManager 設定
            SerializedObject soDeck = new SerializedObject(deckManager);
            soDeck.FindProperty("deckMeshTransform").objectReferenceValue = deckObj.transform;
            soDeck.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(deckManager);

            // 5. 座席と手札トレイの生成（東西南北の4席）
            int seatCount = 4;
            SeatController[] seats = new SeatController[seatCount];
            HandTrayController[] trays = new HandTrayController[seatCount];
            TextMeshProUGUI[] seatStatusTexts = new TextMeshProUGUI[seatCount];

            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset");

            Vector3[] seatOffsets = new Vector3[]
            {
                new Vector3(0, 0, -1.2f),  // 南 (Seat 0: 手前)
                new Vector3(1.2f, 0, 0),   // 東 (Seat 1: 右)
                new Vector3(0, 0, 1.2f),   // 北 (Seat 2: 奥)
                new Vector3(-1.2f, 0, 0)   // 西 (Seat 3: 左)
            };

            float[] seatYRotations = new float[] { 0f, 270f, 180f, 90f };
            // 手札トレイのY軸回転: プレイヤーと正対するため座席に対して180度反転 (22:11当時のPrefab記録値)
            float[] trayYRotations = new float[] { 180f, 90f, 0f, 270f };

            for (int i = 0; i < seatCount; i++)
            {
                // 座席オブジェクト
                GameObject seatObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seatObj.name = $"Seat_{i}";
                seatObj.transform.SetParent(root.transform, false);
                seatObj.transform.localPosition = seatOffsets[i] + new Vector3(0, 0.25f, 0);
                seatObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                seatObj.transform.localRotation = Quaternion.Euler(0, seatYRotations[i], 0);

                // SeatController コンポーネントの追加（※VRCStationを使用せず、クリックでプレイエリア連動）
                SeatController seatCtrl = seatObj.AddUdonSharpComponent<SeatController>();
                seats[i] = seatCtrl;

                // 椅子への初期インタラクト表示名設定
                UdonBehaviour udonSeat = UdonSharpEditorUtility.GetBackingUdonBehaviour(seatCtrl);
                if (udonSeat != null)
                {
                    udonSeat.interactText = $"席 {i + 1} につく (Join Seat {i + 1})";
                }

                // 手札トレイの生成（ユーザー調整値: Y: 1.0f、距離: 0.66m、回転: 手前傾斜25度かつプレイヤー正対）
                GameObject trayObj = new GameObject($"HandTray_{i}");
                trayObj.transform.SetParent(root.transform, false);
                Vector3 trayPos = Vector3.Lerp(tableTop.transform.localPosition, seatObj.transform.localPosition, 0.55f);
                trayPos.y = 1.0f; // ユーザー調整値
                trayObj.transform.localPosition = trayPos;
                trayObj.transform.localRotation = Quaternion.Euler(25f, trayYRotations[i], 0); // 手前傾斜＆プレイヤー正対

                HandTrayController trayCtrl = trayObj.AddUdonSharpComponent<HandTrayController>();
                trays[i] = trayCtrl;

                // 手札スロット（カードMeshRenderer × 5枚分）の生成
                int slotCount = 5;
                MeshRenderer[] cardRenderers = new MeshRenderer[slotCount];

                float cardWidth = 0.08f;
                float spacing = 0.09f;
                float startX = -((slotCount - 1) * spacing) / 2.0f;

                for (int c = 0; c < slotCount; c++)
                {
                    GameObject cardMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cardMesh.name = $"CardSlot_{c}";
                    cardMesh.transform.SetParent(trayObj.transform, false);
                    cardMesh.transform.localPosition = new Vector3(startX + c * spacing, 0, 0);
                    cardMesh.transform.localScale = new Vector3(cardWidth, 0.005f, 0.12f);
                    cardMesh.transform.localRotation = Quaternion.identity;

                    cardRenderers[c] = cardMesh.GetComponent<MeshRenderer>();
                    if (cardFrontMat != null) cardRenderers[c].sharedMaterial = cardFrontMat;

                    // 3D直接インタラクト: 手札カードスロット
                    CardSlotController slotCtrl = cardMesh.AddUdonSharpComponent<CardSlotController>();
                    SerializedObject soSlot = new SerializedObject(slotCtrl);
                    soSlot.FindProperty("slotIndex").intValue = c;
                    soSlot.FindProperty("seatIndex").intValue = i;
                    soSlot.FindProperty("handTray").objectReferenceValue = trayCtrl;
                    soSlot.FindProperty("tableManager").objectReferenceValue = tableManager;
                    soSlot.ApplyModifiedProperties();
                    UdonSharpEditorUtility.CopyProxyToUdon(slotCtrl);

                    // VRChatのインタラクト表示名設定
                    UdonBehaviour udonSlot = UdonSharpEditorUtility.GetBackingUdonBehaviour(slotCtrl);
                    if (udonSlot != null)
                    {
                        udonSlot.interactText = "カードを出す (Play)";
                    }
                }

                // --- 個人用手元操作UI (PersonalUI_Canvas) の生成 ---
                GameObject personalUIObj = new GameObject($"PersonalUI_Canvas_{i}");
                personalUIObj.transform.SetParent(trayObj.transform, false);
                personalUIObj.transform.localPosition = new Vector3(0, 0.01f, 0.14f); // 手札カードのすぐ奥上部
                personalUIObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // Y軸180度回転: 上下を保ったまま裏表を反転して正読化
                personalUIObj.transform.localScale = new Vector3(0.008f, 0.008f, 0.008f);

                Canvas pCanvas = personalUIObj.AddComponent<Canvas>();
                pCanvas.renderMode = RenderMode.WorldSpace;
                personalUIObj.AddComponent<GraphicRaycaster>();
                personalUIObj.AddComponent<VRCUiShape>();

                RectTransform pCanvasRt = personalUIObj.GetComponent<RectTransform>();
                pCanvasRt.sizeDelta = new Vector2(56f, 17f);

                // 背景パネル
                GameObject pPanelObj = new GameObject("Panel");
                pPanelObj.transform.SetParent(personalUIObj.transform, false);
                pPanelObj.transform.localPosition = Vector3.zero;
                Image pPanelImg = pPanelObj.AddComponent<Image>();
                pPanelImg.color = new Color(0.12f, 0.12f, 0.16f, 0.94f);
                pPanelImg.raycastTarget = false;
                RectTransform pPanelRt = pPanelObj.GetComponent<RectTransform>();
                pPanelRt.sizeDelta = new Vector2(56f, 17f);

                // 手元ステータステキスト
                GameObject pStatusObj = new GameObject("StatusText (TMP)");
                pStatusObj.transform.SetParent(personalUIObj.transform, false);
                pStatusObj.transform.localPosition = new Vector3(0, 5.5f, 0);

                TextMeshProUGUI pStatusTmp = pStatusObj.AddComponent<TextMeshProUGUI>();
                if (jpFont != null)
                {
                    pStatusTmp.font = jpFont;
                    pStatusTmp.fontSharedMaterial = jpFont.material;
                }
                pStatusTmp.text = $"[山札: 54 / すて札: 0]\nSeat {i + 1} 参加中 (In Play Area)";
                pStatusTmp.alignment = TextAlignmentOptions.Center;
                pStatusTmp.color = new Color(1f, 0.85f, 0.3f, 1f);
                pStatusTmp.enableAutoSizing = true;
                pStatusTmp.fontSizeMin = 1.8f;
                pStatusTmp.fontSizeMax = 2.8f;
                pStatusTmp.raycastTarget = false;
                RectTransform pStatusRt = pStatusObj.GetComponent<RectTransform>();
                pStatusRt.sizeDelta = new Vector2(54f, 4.5f);
                seatStatusTexts[i] = pStatusTmp;

                // 操作ボタン（上段: アクション、下段: 管理）
                Vector2 pBtnSize = new Vector2(17f, 4.6f);
                float row1Y = 0.5f;
                float row2Y = -5.0f;

                // 上段: 引く、パス、離席
                CreateTMPButton(personalUIObj.transform, "DrawBtn", "引く (Draw)", new Vector2(-18.5f, row1Y), pBtnSize, uiController, "OnClickDrawButton", "カードを引く (Draw)");
                CreateTMPButton(personalUIObj.transform, "PassBtn", "パス (Pass)", new Vector2(0f, row1Y), pBtnSize, uiController, "OnClickAdvanceTurnButton", "ターン終了 (Pass)");
                CreateTMPButton(personalUIObj.transform, "LeaveBtn", "離席 (Leave)", new Vector2(18.5f, row1Y), pBtnSize, uiController, "OnClickLeaveSeatButton", "席を離れる (Leave)");

                // 下段: 配る、シャッフル、リセット
                CreateTMPButton(personalUIObj.transform, "DealBtn", "配る (Deal)", new Vector2(-18.5f, row2Y), pBtnSize, uiController, "OnClickDealButton", "カードを配る (Deal)");
                CreateTMPButton(personalUIObj.transform, "ShuffleBtn", "シャッフル", new Vector2(0f, row2Y), pBtnSize, uiController, "OnClickShuffleButton", "山札シャッフル (Shuffle)");
                CreateTMPButton(personalUIObj.transform, "ResetBtn", "リセット", new Vector2(18.5f, row2Y), pBtnSize, uiController, "OnClickResetButton", "リセット (Reset)");

                // 初期状態は非表示（着席時のみローカル表示）
                personalUIObj.SetActive(false);

                // HandTrayController への参照バインド
                SerializedObject soTray = new SerializedObject(trayCtrl);
                soTray.FindProperty("maxHandCount").intValue = slotCount;
                soTray.FindProperty("cardFrontMaterial").objectReferenceValue = cardFrontMat;
                soTray.FindProperty("cardBackMaterial").objectReferenceValue = cardBackMat;
                SerializedProperty propRenderers = soTray.FindProperty("cardMeshRenderers");
                propRenderers.arraySize = slotCount;
                for (int c = 0; c < slotCount; c++)
                {
                    propRenderers.GetArrayElementAtIndex(c).objectReferenceValue = cardRenderers[c];
                }
                soTray.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(trayCtrl);

                // SeatController への参照バインド
                SerializedObject soSeat = new SerializedObject(seatCtrl);
                soSeat.FindProperty("seatIndex").intValue = i;
                soSeat.FindProperty("linkedHandTray").objectReferenceValue = trayCtrl;
                soSeat.FindProperty("tableManager").objectReferenceValue = tableManager;
                soSeat.FindProperty("seatRenderer").objectReferenceValue = seatObj.GetComponent<MeshRenderer>();
                soSeat.FindProperty("personalUIPanel").objectReferenceValue = personalUIObj;
                soSeat.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(seatCtrl);
            }

            // 山札への3D直接インタラクト設定 (DeckInteractHandler)
            DeckInteractHandler deckHandler = deckObj.AddUdonSharpComponent<DeckInteractHandler>();
            SerializedObject soDeckHandler = new SerializedObject(deckHandler);
            soDeckHandler.FindProperty("tableManager").objectReferenceValue = tableManager;
            SerializedProperty propDeckSeats = soDeckHandler.FindProperty("seatControllers");
            propDeckSeats.arraySize = seatCount;
            for (int i = 0; i < seatCount; i++) propDeckSeats.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
            soDeckHandler.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(deckHandler);

            UdonBehaviour udonDeck = UdonSharpEditorUtility.GetBackingUdonBehaviour(deckHandler);
            if (udonDeck != null)
            {
                udonDeck.interactText = "カードを引く (Draw)";
            }

            // 6. TableManager & TableUIController への全自動配線
            SerializedObject soTable = new SerializedObject(tableManager);
            soTable.FindProperty("deckManager").objectReferenceValue = deckManager;

            SerializedProperty propSeats = soTable.FindProperty("seatControllers");
            propSeats.arraySize = seatCount;
            for (int i = 0; i < seatCount; i++) propSeats.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];

            SerializedProperty propTrays = soTable.FindProperty("handTrayControllers");
            propTrays.arraySize = seatCount;
            for (int i = 0; i < seatCount; i++) propTrays.GetArrayElementAtIndex(i).objectReferenceValue = trays[i];

            soTable.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(tableManager);

            // TableUIController 設定（中央パネル廃止・手元UI配列バインド）
            SerializedObject soUI = new SerializedObject(uiController);
            soUI.FindProperty("tableManager").objectReferenceValue = tableManager;
            soUI.FindProperty("deckManager").objectReferenceValue = deckManager;

            SerializedProperty propUISeatTexts = soUI.FindProperty("seatStatusTexts");
            propUISeatTexts.arraySize = seatCount;
            for (int i = 0; i < seatCount; i++) propUISeatTexts.GetArrayElementAtIndex(i).objectReferenceValue = seatStatusTexts[i];

            SerializedProperty propUISeats = soUI.FindProperty("seatControllers");
            propUISeats.arraySize = seatCount;
            for (int i = 0; i < seatCount; i++) propUISeats.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
            soUI.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(uiController);

            // 8. Prefab として保存
            string tablePrefabPath = "Assets/Projects/Prefabs/CardTable_4Players.prefab";
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, tablePrefabPath, InteractionMode.AutomatedAction);
            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> CardTable_4Players を Prefab として完全保存しました: {tablePrefabPath}</color>");

            // シーンの保存マーク
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> テーブル環境のPrefab生成＆セットアップが完了しました！</color>");

            // 生成結果の全Transformをダンプ
            DumpTableHierarchyTransforms();
        }

        private static Material GetOrCreateCardMaterial(string name, Color color)
        {
            string matDir = "Assets/Projects/Components/Materials";
            if (!Directory.Exists(matDir))
            {
                Directory.CreateDirectory(matDir);
                AssetDatabase.Refresh();
            }

            string matPath = $"{matDir}/{name}.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                Shader shader = Shader.Find("Standard");
                mat = new Material(shader);
                mat.color = color;
                AssetDatabase.CreateAsset(mat, matPath);
                AssetDatabase.SaveAssets();
            }
            return mat;
        }

        private static Button CreateTMPButton(Transform parent, string name, string text, Vector2 pos, Vector2 size, TableUIController uiController, string eventName, string interactText)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            btnObj.transform.localPosition = new Vector3(pos.x, pos.y, 0);
            btnObj.transform.localScale = Vector3.one;

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.18f, 0.45f, 0.85f, 1.0f);
            img.raycastTarget = true;

            Button btn = btnObj.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None };

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            // ボタン専用コライダー (3D直接インタラクト・レーザー受信用)
            BoxCollider col = btnObj.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.2f);
            col.isTrigger = true;

            // UIButtonHandler をアタッチして TableUIController へ直結
            UIButtonHandler handler = btnObj.AddUdonSharpComponent<UIButtonHandler>();
            SerializedObject soHandler = new SerializedObject(handler);
            soHandler.FindProperty("targetUI").objectReferenceValue = uiController;
            soHandler.FindProperty("customEventName").stringValue = eventName;
            soHandler.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(handler);

            UdonBehaviour udonBtn = UdonSharpEditorUtility.GetBackingUdonBehaviour(handler);
            if (udonBtn != null)
            {
                udonBtn.interactText = interactText;
            }

            // Button.onClick とも連動
            UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, handler.OnButtonClick);

            // TextMeshProUGUI
            GameObject textObj = new GameObject("Text (TMP)");
            textObj.transform.SetParent(btnObj.transform, false);
            textObj.transform.localPosition = Vector3.zero;
            textObj.transform.localScale = Vector3.one;

            TextMeshProUGUI tmp = textObj.AddComponent<TextMeshProUGUI>();

            // 日本語フォント (NotoSansJP-Medium SDF) およびそのMaterialを割り当て
            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset");
            if (jpFont != null)
            {
                tmp.font = jpFont;
                tmp.fontSharedMaterial = jpFont.material;
            }

            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 2f;
            tmp.fontSizeMax = 4.5f;
            tmp.raycastTarget = false;

            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.sizeDelta = size;

            return btn;
        }

        private static void EnsureAllProgramAssets()
        {
            string[] scripts = new string[]
            {
                "Assets/Projects/Scripts/Core/DeckManager.cs",
                "Assets/Projects/Scripts/Core/TableManager.cs",
                "Assets/Projects/Scripts/Core/TableUIController.cs",
                "Assets/Projects/Scripts/Core/SeatController.cs",
                "Assets/Projects/Scripts/Core/HandTrayController.cs",
                "Assets/Projects/Scripts/Core/CardSlotController.cs",
                "Assets/Projects/Scripts/Core/DeckInteractHandler.cs",
                "Assets/Projects/Scripts/Core/UIButtonHandler.cs",
                "Assets/Projects/Scripts/Core/DebugClickButton.cs",
                "Assets/Projects/Scripts/Core/CardSnapZone.cs",
                "Assets/Projects/Scripts/Core/CardController.cs",
                "Assets/Projects/Scripts/Core/PersonalHandArea.cs",
                "Assets/Projects/Scripts/Plugins/RulePluginBase.cs"
            };

            bool createdAny = false;
            foreach (string scriptPath in scripts)
            {
                if (EnsureProgramAsset(scriptPath))
                {
                    createdAny = true;
                }
            }

            if (createdAny)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                UdonSharpEditorUtility.ResetAssemblyCaches();
            }

            // 同期コンパイルを実行
            UdonSharp.Compiler.UdonSharpCompilerV1.CompileSync();
        }

        private static bool EnsureProgramAsset(string scriptRelativePath)
        {
            string assetPath = Path.ChangeExtension(scriptRelativePath, ".asset");

            UdonSharpProgramAsset programAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptRelativePath);

            if (programAsset == null)
            {
                if (script == null)
                {
                    Debug.LogWarning($"[VRC-BoardGameKit] MonoScript not found at: {scriptRelativePath}");
                    return false;
                }

                programAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                programAsset.sourceCsScript = script;
                AssetDatabase.CreateAsset(programAsset, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> UdonSharpProgramAsset を自動生成しました: {assetPath}</color>");
                return true;
            }
            else if (programAsset.sourceCsScript == null && script != null)
            {
                programAsset.sourceCsScript = script;
                EditorUtility.SetDirty(programAsset);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                Debug.Log($"<color=#FFFF00><b>[VRC-BoardGameKit]</b> 破損していた UdonSharpProgramAsset ({assetPath}) の sourceCsScript を自己修復しました。</color>");
                return true;
            }
            return false;
        }

        [MenuItem("Tools/VRC-BoardGameKit/Build Young Girl Card Prefab", false, 20)]
        [MenuItem("Tools/VRC-BoardGameKit/幼い少女カードPrefab生成", false, 21)]
        public static GameObject BuildYoungGirlCardPrefab()
        {
            EnsureAllProgramAssets();

            string texturesDir = "Assets/Projects/Components/Textures/Cards";
            string materialsDir = "Assets/Projects/Components/Materials";
            string prefabsDir = "Assets/Projects/Prefabs";

            if (!Directory.Exists(texturesDir)) Directory.CreateDirectory(texturesDir);
            if (!Directory.Exists(materialsDir)) Directory.CreateDirectory(materialsDir);
            if (!Directory.Exists(prefabsDir)) Directory.CreateDirectory(prefabsDir);

            AssetDatabase.Refresh();

            string frontTexPath = $"{texturesDir}/Card_Front_01_YoungGirl.png";
            string backTexPath = $"{texturesDir}/Card_Back_Default.png";
            string shaderPath = "Assets/Projects/Components/Shaders/CardTwoSided.shader";
            string matPath = $"{materialsDir}/Card_01_YoungGirl.mat";
            string prefabPath = $"{prefabsDir}/Card_01_YoungGirl.prefab";

            // 1. テクスチャの取得
            Texture2D frontTex = AssetDatabase.LoadAssetAtPath<Texture2D>(frontTexPath);
            Texture2D backTex = AssetDatabase.LoadAssetAtPath<Texture2D>(backTexPath);

            // 2. 両面シェーダーの取得
            Shader shader = Shader.Find("BoardGameKit/CardTwoSided");
            if (shader == null)
            {
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            }

            if (shader == null)
            {
                Debug.LogError("[VRC-BoardGameKit] BoardGameKit/CardTwoSided シェーダーが見つかりません。");
                return null;
            }

            // 3. マテリアルの生成・プロパティ設定
            Material cardMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (cardMat == null)
            {
                cardMat = new Material(shader);
                AssetDatabase.CreateAsset(cardMat, matPath);
            }
            else
            {
                cardMat.shader = shader;
            }

            if (frontTex != null) cardMat.SetTexture("_MainTex", frontTex);
            if (backTex != null) cardMat.SetTexture("_BackTex", backTex);
            cardMat.SetFloat("_FlipBackUV", 1.0f);
            cardMat.SetFloat("_ShowFront", 1.0f);
            EditorUtility.SetDirty(cardMat);

            // 4. QuadベースのカードGameObject生成 (1.2mアバター向け大迫力サイズ: 幅70cm x 高さ98cm)
            GameObject cardObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cardObj.name = "Card_01_YoungGirl";

            // 標準のMeshColliderを削除し、適度な厚み(0.08m = 8cm)のBoxColliderを付与
            MeshCollider meshCol = cardObj.GetComponent<MeshCollider>();
            if (meshCol != null) Object.DestroyImmediate(meshCol);

            BoxCollider boxCol = cardObj.AddComponent<BoxCollider>();
            boxCol.size = new Vector3(1.0f, 1.0f, 0.08f);
            boxCol.center = Vector3.zero;

            // マテリアル適用
            MeshRenderer mr = cardObj.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = cardMat;

            // 5倍大判スケール設定 (幅70cm x 高さ98cm)
            cardObj.transform.localScale = new Vector3(0.70f, 0.98f, 1.0f);

            // 5. Rigidbody の追加 (完全Kinematic・空中静止運用)
            Rigidbody rb = cardObj.AddComponent<Rigidbody>();
            rb.mass = 0.5f;
            rb.drag = 0f;
            rb.angularDrag = 0.05f;
            rb.useGravity = false;
            rb.isKinematic = true;

            // 6. VRCPickup の追加 (手持ち設定・慣性投げ飛ばしゼロ・AutoHoldオフ)
            VRCPickup pickup = cardObj.AddComponent<VRCPickup>();
            SerializedObject soVrcPickup = new SerializedObject(pickup);
            soVrcPickup.FindProperty("AutoHold").intValue = 0; // 0 = No (AutoHold オフ)
            soVrcPickup.FindProperty("orientation").intValue = 2; // 2 = Grip
            soVrcPickup.FindProperty("ThrowVelocityBoostScale").floatValue = 0f;
            soVrcPickup.FindProperty("ThrowVelocityBoostMinSpeed").floatValue = 0f;
            soVrcPickup.FindProperty("InteractionText").stringValue = "カードを持つ (Pick up)";
            soVrcPickup.FindProperty("UseText").stringValue = "カードを出す (Play)";
            soVrcPickup.ApplyModifiedProperties();

            // 7. CardController のアタッチ (Tell Don't Ask準拠: 暴れ防止・空中静止・磁石スナップ制御)
            CardController cardController = cardObj.AddUdonSharpComponent<CardController>();
            SerializedObject soCard = new SerializedObject(cardController);
            soCard.FindProperty("keepKinematicWhileHeld").boolValue = true;
            soCard.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(cardController);

            // 8. CardSlotController (UdonSharp) のアタッチ (B仕様クリック用互換)
            CardSlotController slotCtrl = cardObj.AddUdonSharpComponent<CardSlotController>();
            SerializedObject soSlot = new SerializedObject(slotCtrl);
            soSlot.FindProperty("slotIndex").intValue = 0;
            soSlot.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(slotCtrl);

            // 9. VRCObjectSync (位置・回転のネットワーク同期)
            cardObj.AddComponent<VRCObjectSync>();

            // 10. Prefabとして保存
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(cardObj, prefabPath);
            Object.DestroyImmediate(cardObj);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 幼い少女カード (Card_01_YoungGirl.prefab) [Pickup・空中静止・スナップ対応] を正常に生成・保存しました！ [サイズ: 70.0cm x 98.0cm]</color>");
            return savedPrefab;
        }

        [MenuItem("Tools/VRC-BoardGameKit/Spawn Young Girl Card in Scene", false, 22)]
        [MenuItem("Tools/VRC-BoardGameKit/シーンに幼い少女カード配置", false, 23)]
        public static void SpawnYoungGirlCardInScene()
        {
            string prefabPath = "Assets/Projects/Prefabs/Card_01_YoungGirl.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                prefab = BuildYoungGirlCardPrefab();
            }

            if (prefab == null)
            {
                Debug.LogError("[VRC-BoardGameKit] Card_01_YoungGirl.prefab のロードに失敗しました。");
                return;
            }

            // 既存の同名オブジェクトがあれば削除
            GameObject existing = GameObject.Find("Card_01_YoungGirl");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            // プレイヤーの目の前に配置 (カード下端が床から浮くよう Y=1.2m に配置)
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = new Vector3(0, 1.2f, 1.8f);
            instance.transform.rotation = Quaternion.Euler(0, 180f, 0); // プレイヤーに正面が向くように
            Undo.RegisterCreatedObjectUndo(instance, "Spawn Young Girl Card");
            Selection.activeGameObject = instance;

            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> シーン上の目の前に幼い少女カード (幅70cm x 高さ98cm) を配置しました！</color>");
        }

        [MenuItem("Tools/VRC-BoardGameKit/Spawn Snap Test Area in Scene", false, 24)]
        [MenuItem("Tools/VRC-BoardGameKit/シーンにスナップ検証エリア配置", false, 25)]
        public static void SpawnSnapTestAreaInScene()
        {
            EnsureAllProgramAssets();

            // 既存のテストエリアがあれば削除
            GameObject existing = GameObject.Find("DEBUG_Snap_Test_Area");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            GameObject root = new GameObject("DEBUG_Snap_Test_Area");
            Undo.RegisterCreatedObjectUndo(root, "Spawn Snap Test Area");
            root.transform.position = new Vector3(0, 0, 1.8f);

            // 1. スナップ枠1 (左側スロット)
            GameObject slot1 = CreateSnapSlot("SnapSlot_Left", new Vector3(-0.45f, 1.0f, 0), root.transform);

            // 2. スナップ枠2 (右側スロット)
            GameObject slot2 = CreateSnapSlot("SnapSlot_Right", new Vector3(0.45f, 1.0f, 0), root.transform);

            // 3. テスト用カードの生成と配置 (中央手前に浮かせる)
            GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Projects/Prefabs/Card_01_YoungGirl.prefab");
            if (cardPrefab == null)
            {
                cardPrefab = BuildYoungGirlCardPrefab();
            }

            if (cardPrefab != null)
            {
                GameObject cardInstance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab);
                cardInstance.transform.SetParent(root.transform, false);
                cardInstance.transform.localPosition = new Vector3(0, 0.9f, -0.4f); // 手前の低い位置に浮かせる
                cardInstance.transform.localRotation = Quaternion.Euler(0, 180f, 0);
            }

            Selection.activeGameObject = root;
            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> 目の前に【スナップ枠 × 2 ＋ 幼い少女カード】の検証エリアを配置しました！ClientSimで手持ち＆吸着テストが可能です。</color>");
        }

        private static GameObject CreateSnapSlot(string name, Vector3 localPos, Transform parent)
        {
            GameObject slotObj = new GameObject(name);
            slotObj.transform.SetParent(parent, false);
            slotObj.transform.localPosition = localPos;
            slotObj.transform.localRotation = Quaternion.Euler(0, 180f, 0);

            // 吸着検知用トリガーコライダー (SSOT参照)
            BoxCollider triggerCol = slotObj.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;
            triggerCol.size = ArcadeFieldConfig.TriggerColliderSize;

            // 視覚ガイド用の薄い枠板 (SSOT参照)
            GameObject guideQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            guideQuad.name = "GuideFrame";
            guideQuad.transform.SetParent(slotObj.transform, false);
            guideQuad.transform.localPosition = Vector3.zero;
            guideQuad.transform.localScale = new Vector3(ArcadeFieldConfig.GuideFrameDimension.x, ArcadeFieldConfig.GuideFrameDimension.y, 1.0f);
            Object.DestroyImmediate(guideQuad.GetComponent<MeshCollider>());

            // 半透明のガイド枠マテリアル
            Material guideMat = new Material(Shader.Find("Unlit/Color"));
            guideMat.color = new Color(0.2f, 0.7f, 1.0f, 0.25f); // 水色の半透明
            guideQuad.GetComponent<MeshRenderer>().sharedMaterial = guideMat;

            // CardSnapZone コンポーネントのアタッチ
            CardSnapZone snapZone = slotObj.AddUdonSharpComponent<CardSnapZone>();
            SerializedObject soZone = new SerializedObject(snapZone);
            soZone.FindProperty("slotName").stringValue = name;
            soZone.FindProperty("guideRenderer").objectReferenceValue = guideQuad.GetComponent<MeshRenderer>();
            soZone.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(snapZone);

            return slotObj;
        }

        [MenuItem("Tools/VRC-BoardGameKit/Build Dynamic Arcade Field in Scene (動的円弧スロット空間生成)", false, 9)]
        [MenuItem("Tools/VRC-BoardGameKit/円弧スロット空間構築 (手札5枠: 標準)", false, 10)]
        public static void BuildDynamicArcadeFieldDefault()
        {
            BuildDynamicArcadeField(ArcadeFieldConfig.CreateOptimized(5));
        }

        [MenuItem("Tools/VRC-BoardGameKit/円弧スロット空間構築 (手札3枠: コンパクト)", false, 11)]
        public static void BuildDynamicArcadeField3()
        {
            BuildDynamicArcadeField(ArcadeFieldConfig.CreateOptimized(3));
        }

        [MenuItem("Tools/VRC-BoardGameKit/円弧スロット空間構築 (手札4枠: 左右対称)", false, 12)]
        public static void BuildDynamicArcadeField4()
        {
            BuildDynamicArcadeField(ArcadeFieldConfig.CreateOptimized(4));
        }

        [MenuItem("Tools/VRC-BoardGameKit/円弧スロット空間構築 (手札7枠: ワイド)", false, 13)]
        public static void BuildDynamicArcadeField7()
        {
            BuildDynamicArcadeField(ArcadeFieldConfig.CreateOptimized(7));
        }

        /// <summary>
        /// 指定された設定データ（ArcadeFieldConfig）に基づき、シーン上に動的円弧スロット空間を一括自動生成する。
        /// </summary>
        /// <param name="config">幾何設定データ（null時は5枠最適設定）</param>
        public static void BuildDynamicArcadeField(ArcadeFieldConfig config = null)
        {
            if (config == null)
            {
                config = ArcadeFieldConfig.CreateOptimized(5);
            }

            EnsureAllProgramAssets();

            // 既存のオブジェクトを安全に削除
            string[] existingNames = { "DynamicCardField_4Players", "CardTable_4Players", "SnapTestArea" };
            foreach (string name in existingNames)
            {
                GameObject obj = GameObject.Find(name);
                if (obj != null)
                {
                    Undo.DestroyObjectImmediate(obj);
                }
            }

            // ルートオブジェクト作成
            GameObject root = new GameObject("DynamicCardField_4Players");
            root.transform.position = Vector3.zero;
            Undo.RegisterCreatedObjectUndo(root, "Build Dynamic Arcade Field");

            // 1. TableManager の生成
            GameObject tableMgrObj = new GameObject("TableManager");
            tableMgrObj.transform.SetParent(root.transform, false);
            TableManager tableManager = tableMgrObj.AddUdonSharpComponent<TableManager>();

            // 2. 中央の場のプレイエリア (Center Play Area) の生成 (70cm x 98cm 大判CardSnapZone)
            GameObject centerPlaySlot = CreateArcadeSnapSlot("Center_PlaySlot", new Vector3(0, config.slotHeightY, 0), Quaternion.Euler(30f, 0, 0));
            centerPlaySlot.transform.SetParent(root.transform, false);

            // 3. 4つのプレイヤーエリア（登録キューブ ＆ 円弧状手札スロット）
            Vector3[] seatPositions = {
                new Vector3(0, 0, -2.5f),  // Seat 0: South (正面手前)
                new Vector3(0, 0, 2.5f),   // Seat 1: North (対面奥)
                new Vector3(2.5f, 0, 0),   // Seat 2: East (右)
                new Vector3(-2.5f, 0, 0)   // Seat 3: West (左)
            };
            float[] seatYaws = { 0f, 180f, -90f, 90f };

            SeatController[] seatControllers = new SeatController[4];

            for (int i = 0; i < 4; i++)
            {
                GameObject seatRoot = new GameObject($"PlayerArea_Seat{i}");
                seatRoot.transform.SetParent(root.transform, false);
                seatRoot.transform.localPosition = seatPositions[i];
                seatRoot.transform.localRotation = Quaternion.Euler(0, seatYaws[i], 0);

                // A. 参加登録キューブ (RegistrationCube)
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"RegistrationCube_Seat{i}";
                cube.transform.SetParent(seatRoot.transform, false);
                cube.transform.localPosition = new Vector3(0.9f, 0.6f, 0f); // プレイヤーの右脇
                cube.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

                SeatController seatCtrl = cube.AddUdonSharpComponent<SeatController>();
                SerializedObject soSeat = new SerializedObject(seatCtrl);
                soSeat.FindProperty("seatIndex").intValue = i;
                soSeat.FindProperty("tableManager").objectReferenceValue = tableManager;
                soSeat.FindProperty("seatRenderer").objectReferenceValue = cube.GetComponent<MeshRenderer>();
                soSeat.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(seatCtrl);
                seatControllers[i] = seatCtrl;

                // B. 動的円弧手札エリア (PersonalHandArea)
                GameObject handAreaObj = new GameObject($"PersonalHandArea_Seat{i}");
                handAreaObj.transform.SetParent(seatRoot.transform, false);
                handAreaObj.transform.localPosition = Vector3.zero; // プレイヤー立ち位置中心
                handAreaObj.transform.localRotation = Quaternion.identity;

                PersonalHandArea handArea = handAreaObj.AddUdonSharpComponent<PersonalHandArea>();

                // スロット群をまとめるコンテナ (初期非表示)
                GameObject slotContainer = new GameObject("SlotContainer");
                slotContainer.transform.SetParent(handAreaObj.transform, false);
                slotContainer.transform.localPosition = Vector3.zero;
                slotContainer.transform.localRotation = Quaternion.identity;

                int slotCount = config.slotCount;
                CardSnapZone[] snapZones = new CardSnapZone[slotCount];

                for (int s = 0; s < slotCount; s++)
                {
                    // 幾何計算: プレイヤー中心を原点とした円弧配置 (左右対称)
                    float angleDeg = (s - (slotCount - 1) / 2f) * config.angleStep;
                    float rad = angleDeg * Mathf.Deg2Rad;
                    Vector3 slotPos = new Vector3(Mathf.Sin(rad) * config.radius, config.slotHeightY, Mathf.Cos(rad) * config.radius);
                    Quaternion slotRot = Quaternion.Euler(config.tiltAngle, angleDeg, 0f); // プレイヤー中心を向くYaw + 手前チルト

                    GameObject slotObj = CreateArcadeSnapSlot($"Seat{i}_Slot_{s}", slotPos, slotRot);
                    slotObj.transform.SetParent(slotContainer.transform, false);

                    CardSnapZone zone = slotObj.GetComponent<CardSnapZone>();
                    snapZones[s] = zone;
                }

                SerializedObject soHandArea = new SerializedObject(handArea);
                soHandArea.FindProperty("slotCount").intValue = slotCount;
                soHandArea.FindProperty("radius").floatValue = config.radius;
                soHandArea.FindProperty("angleStep").floatValue = config.angleStep;
                soHandArea.FindProperty("slotTiltAngle").floatValue = config.tiltAngle;
                soHandArea.FindProperty("slotHeightY").floatValue = config.slotHeightY;
                soHandArea.FindProperty("slotContainer").objectReferenceValue = slotContainer;
                SerializedProperty snapZonesProp = soHandArea.FindProperty("snapZones");
                snapZonesProp.arraySize = slotCount;
                for (int s = 0; s < slotCount; s++)
                {
                    snapZonesProp.GetArrayElementAtIndex(s).objectReferenceValue = snapZones[s];
                }
                soHandArea.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(handArea);

                // SeatController に linkedHandArea を紐付け
                soSeat.Update();
                soSeat.FindProperty("linkedHandArea").objectReferenceValue = handArea;
                soSeat.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(seatCtrl);

                // 初期状態は非表示
                slotContainer.SetActive(false);
            }

            // TableManager に 4席を登録
            SerializedObject soTable = new SerializedObject(tableManager);
            SerializedProperty seatsProp = soTable.FindProperty("seatControllers");
            seatsProp.arraySize = 4;
            for (int i = 0; i < 4; i++)
            {
                seatsProp.GetArrayElementAtIndex(i).objectReferenceValue = seatControllers[i];
            }
            soTable.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(tableManager);

            // 4. 大判山札オブジェクト（DeckObject ＆ カードプール一括生成）
            Vector3 deckPos = new Vector3(-1.10f, config.slotHeightY - 0.05f, 0f);
            Quaternion deckRot = Quaternion.Euler(20f, 0, 0); // 手前に少し傾斜
            GameObject deckObj = CreateArcadeDeckObject("DeckObject", deckPos, deckRot, tableManager, seatControllers, config.poolCardCount);
            deckObj.transform.SetParent(root.transform, false);

            DeckManager deckMgr = deckObj.GetComponent<DeckManager>();
            if (deckMgr != null)
            {
                soTable.Update();
                soTable.FindProperty("deckManager").objectReferenceValue = deckMgr;
                soTable.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(tableManager);
            }

            Selection.activeGameObject = root;
            Debug.Log($"<color=#00FF99>[VRC-BoardGameKit] プレイヤー包囲型円弧スロット空間 (手札 {config.slotCount} 枠 / カードプール {config.poolCardCount} 枚 / 半径 {config.radius:F2}m) の構築が完了しました！</color>");
        }

        [MenuItem("Tools/VRC-BoardGameKit/Spawn Deck in Scene (シーンに大判山札配置)", false, 26)]
        public static void SpawnDeckInScene()
        {
            EnsureAllProgramAssets();

            GameObject existing = GameObject.Find("DEBUG_ArcadeDeck");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            TableManager tm = Object.FindObjectOfType<TableManager>();
            SeatController[] seats = Object.FindObjectsOfType<SeatController>();

            Vector3 spawnPos = new Vector3(-0.9f, 0.9f, 1.8f);
            Quaternion spawnRot = Quaternion.Euler(20f, 0, 0);

            GameObject deck = CreateArcadeDeckObject("DEBUG_ArcadeDeck", spawnPos, spawnRot, tm, seats, 20);
            Undo.RegisterCreatedObjectUndo(deck, "Spawn Deck in Scene");
            Selection.activeGameObject = deck;

            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> シーン内に大判山札オブジェクト（両面Quad・カードプール20枚完備）を配置しました！</color>");
        }

        private static GameObject CreateArcadeDeckObject(string name, Vector3 localPos, Quaternion localRot, TableManager tableManager, SeatController[] seats, int poolCount = 20)
        {
            GameObject deckRoot = new GameObject(name);
            deckRoot.transform.localPosition = localPos;
            deckRoot.transform.localRotation = localRot;

            // 1. 山札メッシュ (横側面メッシュ不要・表裏両面Quad)
            GameObject deckQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            deckQuad.name = "DeckMesh_Quad";
            deckQuad.transform.SetParent(deckRoot.transform, false);
            deckQuad.transform.localPosition = Vector3.zero;
            deckQuad.transform.localRotation = Quaternion.identity;
            deckQuad.transform.localScale = new Vector3(0.70f, 0.98f, 1.0f);

            // 標準のMeshColliderを削除し、適切な厚みの当たり判定BoxColliderを付与
            MeshCollider mc = deckQuad.GetComponent<MeshCollider>();
            if (mc != null) Object.DestroyImmediate(mc);

            BoxCollider deckCol = deckQuad.AddComponent<BoxCollider>();
            deckCol.size = new Vector3(1.0f, 1.0f, 0.15f);
            deckCol.center = Vector3.zero;

            // マテリアル設定（両面シェーダー CardTwoSided）
            string texturesDir = "Assets/Projects/Components/Textures/Cards";
            string materialsDir = "Assets/Projects/Components/Materials";
            string backTexPath = $"{texturesDir}/Card_Back_Default.png";
            string matPath = $"{materialsDir}/Deck_TopBack.mat";

            Texture2D backTex = AssetDatabase.LoadAssetAtPath<Texture2D>(backTexPath);
            Shader shader = Shader.Find("BoardGameKit/CardTwoSided");
            if (shader == null) shader = Shader.Find("Standard");

            Material deckMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (deckMat == null)
            {
                deckMat = new Material(shader);
                AssetDatabase.CreateAsset(deckMat, matPath);
            }
            if (backTex != null)
            {
                deckMat.SetTexture("_MainTex", backTex);
                deckMat.SetTexture("_BackTex", backTex);
                deckMat.mainTexture = backTex;
            }
            deckQuad.GetComponent<MeshRenderer>().sharedMaterial = deckMat;

            // 2. 山札上面の残数表示テキスト (TMP)
            GameObject textObj = new GameObject("DeckCountText_TMP");
            textObj.transform.SetParent(deckQuad.transform, false);
            textObj.transform.localPosition = new Vector3(0, 0, -0.02f); // Quad表面の少し手前
            textObj.transform.localRotation = Quaternion.Euler(0, 0, 0);
            textObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);

            TextMeshPro countTmp = textObj.AddComponent<TextMeshPro>();
            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset");
            if (jpFont != null)
            {
                countTmp.font = jpFont;
                countTmp.fontSharedMaterial = jpFont.material;
            }
            countTmp.text = $"山札: {poolCount}枚";
            countTmp.alignment = TextAlignmentOptions.Center;
            countTmp.fontSize = 28f;
            countTmp.color = new Color(1.0f, 0.95f, 0.6f, 1.0f); // 金色

            // 3. DeckManager のアタッチ
            DeckManager deckManager = deckRoot.AddUdonSharpComponent<DeckManager>();

            // 4. カードオブジェクトプールの事前生成 (指定枚数の Card_01_YoungGirl をインスタンス化)
            GameObject poolContainer = new GameObject("CardPoolContainer");
            poolContainer.transform.SetParent(deckRoot.transform, false);
            poolContainer.transform.localPosition = Vector3.zero;

            string cardPrefabPath = "Assets/Projects/Prefabs/Card_01_YoungGirl.prefab";
            GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cardPrefabPath);
            if (cardPrefab == null)
            {
                cardPrefab = BuildYoungGirlCardPrefab();
            }

            CardController[] poolCards = new CardController[poolCount];
            for (int c = 0; c < poolCount; c++)
            {
                GameObject cardInstance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab);
                cardInstance.name = $"PoolCard_{c:D2}";
                cardInstance.transform.SetParent(poolContainer.transform, false);

                CardController cardCtrl = cardInstance.GetComponent<CardController>();
                if (cardCtrl != null)
                {
                    cardCtrl.cardId = c;
                    // 初期状態: 山札位置に重なって非表示待機
                    cardCtrl.ResetToDeck(localPos, localRot);
                    poolCards[c] = cardCtrl;
                }
            }

            // DeckManager 設定
            SerializedObject soDeck = new SerializedObject(deckManager);
            soDeck.FindProperty("defaultCardCount").intValue = poolCount;
            soDeck.FindProperty("deckMeshTransform").objectReferenceValue = deckQuad.transform;
            soDeck.FindProperty("remainingText").objectReferenceValue = countTmp;
            SerializedProperty propPool = soDeck.FindProperty("cardPool");
            propPool.arraySize = poolCount;
            for (int c = 0; c < poolCount; c++)
            {
                propPool.GetArrayElementAtIndex(c).objectReferenceValue = poolCards[c];
            }
            soDeck.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(deckManager);

            // 5. DeckInteractHandler のアタッチ (3D直接インタラクト)
            DeckInteractHandler deckHandler = deckQuad.AddUdonSharpComponent<DeckInteractHandler>();
            SerializedObject soHandler = new SerializedObject(deckHandler);
            soHandler.FindProperty("tableManager").objectReferenceValue = tableManager;
            soHandler.FindProperty("deckManager").objectReferenceValue = deckManager;
            if (seats != null && seats.Length > 0)
            {
                SerializedProperty propSeats = soHandler.FindProperty("seatControllers");
                propSeats.arraySize = seats.Length;
                for (int s = 0; s < seats.Length; s++)
                {
                    propSeats.GetArrayElementAtIndex(s).objectReferenceValue = seats[s];
                }
            }
            soHandler.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(deckHandler);

            UdonBehaviour udonBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(deckHandler);
            if (udonBacking != null)
            {
                udonBacking.interactText = "カードを引く (Draw)";
            }

            return deckRoot;
        }

        private static GameObject CreateArcadeSnapSlot(string name, Vector3 localPos, Quaternion localRot)
        {
            GameObject slotObj = new GameObject(name);
            slotObj.transform.localPosition = localPos;
            slotObj.transform.localRotation = localRot;

            // 吸着検知用トリガーコライダー (SSOT参照: ガイド枠幅に一致させ隣接スロットとの干渉をゼロ化)
            BoxCollider triggerCol = slotObj.AddComponent<BoxCollider>();
            triggerCol.isTrigger = true;
            triggerCol.size = ArcadeFieldConfig.TriggerColliderSize;

            // 視覚ガイド用の薄い枠板 (SSOT参照)
            GameObject guideQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            guideQuad.name = "GuideFrame";
            guideQuad.transform.SetParent(slotObj.transform, false);
            guideQuad.transform.localPosition = Vector3.zero;
            guideQuad.transform.localScale = new Vector3(ArcadeFieldConfig.GuideFrameDimension.x, ArcadeFieldConfig.GuideFrameDimension.y, 1.0f);
            Object.DestroyImmediate(guideQuad.GetComponent<MeshCollider>());

            // 半透明のガイド枠マテリアル
            Material guideMat = new Material(Shader.Find("Unlit/Color"));
            guideMat.color = new Color(0.2f, 0.7f, 1.0f, 0.25f); // 水色の半透明
            guideQuad.GetComponent<MeshRenderer>().sharedMaterial = guideMat;

            // CardSnapZone コンポーネントのアタッチ
            CardSnapZone snapZone = slotObj.AddUdonSharpComponent<CardSnapZone>();
            SerializedObject soZone = new SerializedObject(snapZone);
            soZone.FindProperty("slotName").stringValue = name;
            soZone.FindProperty("guideRenderer").objectReferenceValue = guideQuad.GetComponent<MeshRenderer>();
            soZone.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(snapZone);

            return slotObj;
        }
    }

    /// <summary>
    /// プレイヤー包囲型円弧スロット空間を直感的なGUIで設計・生成する専用エディタウィンドウ。
    /// スロット枚数（1〜10枚）・カードプール枚数・スキマ・チルト角をスライダーで自由に変更し、ワンクリックでシーンへ反映する。
    /// </summary>
    public class ArcadeFieldBuilderWindow : EditorWindow
    {
        [SerializeField] private int slotCount = 5;
        [SerializeField] private int poolCardCount = 20;
        [SerializeField] private float minBottomGapCm = 8.0f;
        [SerializeField] private float tiltAngle = 12.0f;
        [SerializeField] private float slotHeightY = 0.85f;
        [SerializeField] private float maxFovDeg = 140.0f;

        private Vector2 scrollPos;

        [MenuItem("Tools/VRC-BoardGameKit/Dynamic Arcade Field Builder (円弧空間ビルダー GUI)", false, 1)]
        public static void OpenWindow()
        {
            var window = GetWindow<ArcadeFieldBuilderWindow>("Arcade Field Builder");
            window.minSize = new Vector2(380, 480);
            window.Show();
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Dynamic Arcade Field Builder", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("プレイヤー包囲型円弧スロット空間の幾何パラメータ設計＆自動生成", EditorStyles.miniLabel);
            EditorGUILayout.Space(8);

            // --- 1. 基本パラメータ設定 ---
            EditorGUILayout.LabelField("【スロット ＆ カードプール設定】", EditorStyles.boldLabel);
            slotCount = EditorGUILayout.IntSlider("手札スロット数 (枚)", slotCount, ArcadeFieldConfig.MinSlotCount, ArcadeFieldConfig.MaxSlotCount);
            poolCardCount = EditorGUILayout.IntSlider("山札プール枚数 (枚)", poolCardCount, 5, 54);
            minBottomGapCm = EditorGUILayout.Slider("下端の最小スキマ (cm)", minBottomGapCm, ArcadeFieldConfig.MinGapCm, ArcadeFieldConfig.MaxGapCm);
            tiltAngle = EditorGUILayout.Slider("手前チルト見下ろし角 (度)", tiltAngle, ArcadeFieldConfig.MinTiltAngle, ArcadeFieldConfig.MaxTiltAngle);
            slotHeightY = EditorGUILayout.Slider("スロット基準高さ Y (m)", slotHeightY, ArcadeFieldConfig.MinHeightY, ArcadeFieldConfig.MaxHeightY);
            maxFovDeg = EditorGUILayout.Slider("最大全体視野角 (度)", maxFovDeg, ArcadeFieldConfig.MinFovDeg, ArcadeFieldConfig.MaxFovDeg);

            EditorGUILayout.Space(12);

            // --- 2. 幾何計算プレビュー ---
            ArcadeFieldConfig previewConfig = ArcadeFieldConfig.CreateOptimized(slotCount, minBottomGapCm * 0.01f, maxFovDeg, poolCardCount);
            previewConfig.tiltAngle = tiltAngle;
            previewConfig.slotHeightY = slotHeightY;

            float totalArcDeg = (slotCount > 1) ? (slotCount - 1) * previewConfig.angleStep : 0f;

            EditorGUILayout.LabelField("【自動幾何計算プレビュー (一元数式算出)】", EditorStyles.boldLabel);
            string previewInfo = 
                $"・プレイヤー中心半径 R: {previewConfig.radius:F2} m\n" +
                $"・スロット間ステップ角度: {previewConfig.angleStep:F1} 度\n" +
                $"・全体展開視野角: {totalArcDeg:F1} 度 (正面左右 ±{totalArcDeg * 0.5f:F1}度)\n" +
                $"・最も狭まる下端スキマ: {minBottomGapCm:F1} cm (完全保証)\n" +
                $"・カードプール枚数: {poolCardCount} 枚 (オブジェクトプール事前生成)\n" +
                $"・カード寸法: 幅 {ArcadeFieldConfig.CardDimension.x * 100:F0}cm × 高 {ArcadeFieldConfig.CardDimension.y * 100:F0}cm (大判)";

            EditorGUILayout.HelpBox(previewInfo, MessageType.Info);

            EditorGUILayout.Space(16);

            // --- 3. 生成ボタン ---
            GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
            if (GUILayout.Button($"シーン上に空間を自動生成 (手札 {slotCount} 枠 / プール {poolCardCount} 枚)", GUILayout.Height(40)))
            {
                CardTableBuilder.BuildDynamicArcadeField(previewConfig);
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox("※生成を実行すると、シーン上の既存フィールドが自動削除され、最新設定で再構築されます。", MessageType.None);

            EditorGUILayout.EndScrollView();
        }
    }
}
