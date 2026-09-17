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

        [MenuItem("Tools/VRC-BoardGameKit/Rebuild & Save Table Prefabs")]
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

            Vector3[] seatOffsets = new Vector3[]
            {
                new Vector3(0, 0, -1.2f),  // 南 (Seat 0: 手前)
                new Vector3(1.2f, 0, 0),   // 東 (Seat 1: 右)
                new Vector3(0, 0, 1.2f),   // 北 (Seat 2: 奥)
                new Vector3(-1.2f, 0, 0)   // 西 (Seat 3: 左)
            };

            float[] seatYRotations = new float[] { 0f, 270f, 180f, 90f };

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

                // 手札トレイの生成（ユーザー調整値: Y: 1.0f、距離: 0.66m）
                GameObject trayObj = new GameObject($"HandTray_{i}");
                trayObj.transform.SetParent(root.transform, false);
                Vector3 trayPos = Vector3.Lerp(tableTop.transform.localPosition, seatObj.transform.localPosition, 0.55f);
                trayPos.y = 1.0f; // ユーザー調整値
                trayObj.transform.localPosition = trayPos;
                trayObj.transform.localRotation = Quaternion.Euler(25f, seatYRotations[i], 0); // 手前傾斜

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

            // 6. 操作パネル (World Space Canvas) の生成
            // 6. 操作パネル (World Space Canvas) の生成
            GameObject canvasObj = new GameObject("TableUI_Canvas");
            canvasObj.transform.SetParent(root.transform, false);
            canvasObj.transform.localPosition = new Vector3(0, 1.0f, -0.25f);
            canvasObj.transform.localRotation = Quaternion.Euler(35f, 0, 0);
            canvasObj.transform.localScale = new Vector3(0.02f, 0.02f, 0.02f);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<GraphicRaycaster>();
            canvasObj.AddComponent<VRCUiShape>(); // VRChatのUI判定

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.anchoredPosition3D = new Vector3(0, 1.0f, -0.25f);
            canvasRect.sizeDelta = new Vector2(50f, 15f);

            // 背景パネル
            GameObject panelObj = new GameObject("Panel");
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.transform.localPosition = Vector3.zero;
            panelObj.transform.localScale = Vector3.one;
            Image panelImg = panelObj.AddComponent<Image>();
            panelImg.color = new Color(0.12f, 0.12f, 0.16f, 0.94f);
            panelImg.raycastTarget = false;
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(50f, 15f);

            // ステータス表示用テキスト (TextMeshProUGUI)
            GameObject statusObj = new GameObject("StatusText (TMP)");
            statusObj.transform.SetParent(canvasObj.transform, false);
            statusObj.transform.localPosition = new Vector3(0, 3.8f, 0);
            statusObj.transform.localScale = Vector3.one;

            TextMeshProUGUI tmpStatus = statusObj.AddComponent<TextMeshProUGUI>();
            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset");
            if (jpFont != null)
            {
                tmpStatus.font = jpFont;
                tmpStatus.fontSharedMaterial = jpFont.material;
            }
            tmpStatus.text = "[山札: 54 / すて札: 0]\n準備完了 - 席をクリックして参加 (Ready - Click Seat)";
            tmpStatus.alignment = TextAlignmentOptions.Center;
            tmpStatus.color = new Color(1f, 0.85f, 0.3f, 1f);
            tmpStatus.enableAutoSizing = true;
            tmpStatus.fontSizeMin = 2f;
            tmpStatus.fontSizeMax = 3.2f;
            tmpStatus.raycastTarget = false;
            RectTransform statusRt = statusObj.GetComponent<RectTransform>();
            statusRt.sizeDelta = new Vector2(48f, 6f);

            // 5つのボタン生成 (UIButtonHandler & TextMeshProUGUI 版)
            float btnY = -3.0f;
            Vector2 btnSize = new Vector2(8.8f, 6.0f);
            CreateTMPButton(canvasObj.transform, "DealBtn", "配る (Deal)", new Vector2(-19f, btnY), btnSize, uiController, "OnClickDealButton", "カードを配る (Deal)");
            CreateTMPButton(canvasObj.transform, "DrawBtn", "引く (Draw)", new Vector2(-9.5f, btnY), btnSize, uiController, "OnClickDrawButton", "カードを引く (Draw)");
            CreateTMPButton(canvasObj.transform, "ShuffleBtn", "シャッフル", new Vector2(0f, btnY), btnSize, uiController, "OnClickShuffleButton", "山札シャッフル (Shuffle)");
            CreateTMPButton(canvasObj.transform, "PassBtn", "パス (Pass)", new Vector2(9.5f, btnY), btnSize, uiController, "OnClickAdvanceTurnButton", "ターン終了 (Pass)");
            CreateTMPButton(canvasObj.transform, "ResetBtn", "リセット", new Vector2(19f, btnY), btnSize, uiController, "OnClickResetButton", "リセット (Reset)");

            // 7. TableManager & TableUIController への全自動配線
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

            SerializedObject soUI = new SerializedObject(uiController);
            soUI.FindProperty("tableManager").objectReferenceValue = tableManager;
            soUI.FindProperty("deckManager").objectReferenceValue = deckManager;
            soUI.FindProperty("statusText").objectReferenceValue = tmpStatus;
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
            if (programAsset == null)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptRelativePath);
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
            return false;
        }
    }
}
