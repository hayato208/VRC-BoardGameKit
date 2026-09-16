using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRCStation = VRC.SDK3.Components.VRCStation;
using BoardGameKit.Core;
using System.IO;

namespace BoardGameKit.Editor
{
    /// <summary>
    /// シーン上に4人対戦用のカードゲームテーブル環境を一括自動生成するエディタ拡張。
    /// テーブル、椅子(VRCStation)、手札トレイ、山札、操作ボタンUI、およびU#コンポーネントの自動配線を行う。
    /// </summary>
    public static class CardTableBuilder
    {
        [MenuItem("Tools/VRC-BoardGameKit/Setup 4-Player Table in Scene")]
        public static void BuildTable()
        {
            // 1. ルートオブジェクトの生成
            GameObject root = new GameObject("CardTable_4Players");
            Undo.RegisterCreatedObjectUndo(root, "Create Card Table");
            root.transform.position = new Vector3(0, 0, 2.0f); // スポーン正面

            // 2. テーブル天板の生成
            GameObject tableTop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tableTop.name = "TableTop";
            tableTop.transform.SetParent(root.transform);
            tableTop.transform.localPosition = new Vector3(0, 0.7f, 0);
            tableTop.transform.localScale = new Vector3(2.2f, 0.05f, 2.2f);

            // テーブル脚
            GameObject tableLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tableLeg.name = "TableLeg";
            tableLeg.transform.SetParent(root.transform);
            tableLeg.transform.localPosition = new Vector3(0, 0.35f, 0);
            tableLeg.transform.localScale = new Vector3(0.3f, 0.35f, 0.3f);

            // 3. 山札オブジェクト (Deck)
            GameObject deckObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deckObj.name = "DeckObject";
            deckObj.transform.SetParent(root.transform);
            deckObj.transform.localPosition = new Vector3(0, 0.75f, 0.15f);
            deckObj.transform.localScale = new Vector3(0.12f, 0.05f, 0.18f);

            // 捨て札オブジェクト (Discard)
            GameObject discardObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            discardObj.name = "DiscardArea";
            discardObj.transform.SetParent(root.transform);
            discardObj.transform.localPosition = new Vector3(0.2f, 0.73f, 0.15f);
            discardObj.transform.localScale = new Vector3(0.12f, 0.01f, 0.18f);

            // 4. コアコンポーネントのアタッチ (DeckManager, TableManager)
            DeckManager deckManager = root.AddComponent<DeckManager>();
            SerializedObject soDeck = new SerializedObject(deckManager);
            soDeck.FindProperty("deckMeshTransform").objectReferenceValue = deckObj.transform;
            soDeck.ApplyModifiedProperties();

            TableManager tableManager = root.AddComponent<TableManager>();

            // 5. 座席と手札トレイの生成（東西南北の4席）
            int seatCount = 4;
            SeatController[] seats = new SeatController[seatCount];
            HandTrayController[] trays = new HandTrayController[seatCount];

            Vector3[] seatOffsets = new Vector3[]
            {
                new Vector3(0, 0, -1.3f),  // 南 (Seat 0: 手前)
                new Vector3(1.3f, 0, 0),   // 東 (Seat 1: 右)
                new Vector3(0, 0, 1.3f),   // 北 (Seat 2: 奥)
                new Vector3(-1.3f, 0, 0)   // 西 (Seat 3: 左)
            };

            float[] seatYRotations = new float[] { 0f, 270f, 180f, 90f };

            for (int i = 0; i < seatCount; i++)
            {
                // 座席オブジェクト
                GameObject seatObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seatObj.name = $"Seat_{i}";
                seatObj.transform.SetParent(root.transform);
                seatObj.transform.localPosition = seatOffsets[i] + new Vector3(0, 0.25f, 0);
                seatObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                seatObj.transform.localRotation = Quaternion.Euler(0, seatYRotations[i], 0);

                // VRCStation コンポーネントの追加
                VRCStation station = seatObj.AddComponent<VRCStation>();

                // SeatController コンポーネントの追加
                SeatController seatCtrl = seatObj.AddComponent<SeatController>();
                seats[i] = seatCtrl;

                // 手札トレイの生成（座席とテーブルの中間に配置）
                GameObject trayObj = new GameObject($"HandTray_{i}");
                trayObj.transform.SetParent(root.transform);
                Vector3 trayPos = Vector3.Lerp(tableTop.transform.localPosition, seatObj.transform.localPosition, 0.55f);
                trayPos.y = 0.74f;
                trayObj.transform.localPosition = trayPos;
                trayObj.transform.localRotation = Quaternion.Euler(20f, seatYRotations[i], 0); // 手前傾斜

                HandTrayController trayCtrl = trayObj.AddComponent<HandTrayController>();
                trays[i] = trayCtrl;

                // 手札スロット（カードMeshRenderer × 5枚分）の生成
                int slotCount = 5;
                MeshRenderer[] cardRenderers = new MeshRenderer[slotCount];
                Transform[] cardAnchors = new Transform[slotCount];

                float cardWidth = 0.1f;
                float spacing = 0.11f;
                float startX = -((slotCount - 1) * spacing) / 2.0f;

                for (int c = 0; c < slotCount; c++)
                {
                    GameObject cardMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cardMesh.name = $"CardSlot_{c}";
                    cardMesh.transform.SetParent(trayObj.transform);
                    cardMesh.transform.localPosition = new Vector3(startX + c * spacing, 0, 0);
                    cardMesh.transform.localScale = new Vector3(cardWidth, 0.005f, 0.14f);
                    cardMesh.transform.localRotation = Quaternion.identity;

                    cardRenderers[c] = cardMesh.GetComponent<MeshRenderer>();
                    cardAnchors[c] = cardMesh.transform;
                }

                // HandTrayController への参照バインド
                SerializedObject soTray = new SerializedObject(trayCtrl);
                soTray.FindProperty("maxHandCount").intValue = slotCount;
                SerializedProperty propRenderers = soTray.FindProperty("cardMeshRenderers");
                propRenderers.arraySize = slotCount;
                for (int c = 0; c < slotCount; c++)
                {
                    propRenderers.GetArrayElementAtIndex(c).objectReferenceValue = cardRenderers[c];
                }
                soTray.ApplyModifiedProperties();

                // SeatController への参照バインド
                SerializedObject soSeat = new SerializedObject(seatCtrl);
                soSeat.FindProperty("seatIndex").intValue = i;
                soSeat.FindProperty("linkedHandTray").objectReferenceValue = trayCtrl;
                soSeat.FindProperty("tableManager").objectReferenceValue = tableManager;
                soSeat.ApplyModifiedProperties();
            }

            // 6. 操作パネル (World Space Canvas & UI Buttons) の生成
            GameObject canvasObj = new GameObject("TableUI_Canvas");
            canvasObj.transform.SetParent(root.transform);
            canvasObj.transform.localPosition = new Vector3(0, 0.85f, -0.45f);
            canvasObj.transform.localRotation = Quaternion.Euler(45f, 0, 0);
            canvasObj.transform.localScale = new Vector3(0.002f, 0.002f, 0.002f);

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector3(500, 120);

            // 背景パネル
            GameObject panelObj = new GameObject("Panel");
            panelObj.transform.SetParent(canvasObj.transform);
            panelObj.transform.localPosition = Vector3.zero;
            Image panelImg = panelObj.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(500, 120);

            TableUIController uiController = root.AddComponent<TableUIController>();

            // ボタン生成ヘルパー
            CreateButton(canvasObj.transform, "DealBtn", "配る (Deal)", new Vector2(-190, 0), uiController, "OnClickDealButton");
            CreateButton(canvasObj.transform, "DrawBtn", "引く (Draw)", new Vector2(-95, 0), uiController, "OnClickDrawButton");
            CreateButton(canvasObj.transform, "ShuffleBtn", "シャッフル", new Vector2(0, 0), uiController, "OnClickShuffleButton");
            CreateButton(canvasObj.transform, "PassBtn", "パス (Next)", new Vector2(95, 0), uiController, "OnClickAdvanceTurnButton");
            CreateButton(canvasObj.transform, "ResetBtn", "リセット", new Vector2(190, 0), uiController, "OnClickResetButton");

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

            SerializedObject soUI = new SerializedObject(uiController);
            soUI.FindProperty("tableManager").objectReferenceValue = tableManager;
            soUI.FindProperty("deckManager").objectReferenceValue = deckManager;
            SerializedProperty propUISeats = soUI.FindProperty("seatControllers");
            propUISeats.arraySize = seatCount;
            for (int i = 0; i < seatCount; i++) propUISeats.GetArrayElementAtIndex(i).objectReferenceValue = seats[i];
            soUI.ApplyModifiedProperties();

            Selection.activeGameObject = root;
            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> 4人対戦用カードテーブルの自動セットアップが完了しました！</color>");
        }

        private static void CreateButton(Transform parent, string name, string text, Vector2 pos, TableUIController target, string methodName)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent);
            btnObj.transform.localPosition = new Vector3(pos.x, pos.y, 0);

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.2f, 0.4f, 0.7f, 1.0f);

            Button btn = btnObj.AddComponent<Button>();
            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(85, 45);

            // テキスト
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform);
            textObj.transform.localPosition = Vector3.zero;
            Text txt = textObj.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 14;
            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.sizeDelta = new Vector2(85, 45);

            // クリックイベントの登録 (UnityEvent)
            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                btn.onClick,
                new UnityEngine.Events.UnityAction((System.Action)System.Delegate.CreateDelegate(typeof(System.Action), target, methodName))
            );
        }
    }
}
