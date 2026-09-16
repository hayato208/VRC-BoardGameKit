using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.SDK3.Components;
using VRCStation = VRC.SDK3.Components.VRCStation;
using VRCUiShape = VRC.SDK3.Components.VRCUiShape;
using VRC.Udon;
using BoardGameKit.Core;
using System.IO;

namespace BoardGameKit.Editor
{
    /// <summary>
    /// シーン上に4人対戦用のカードゲームテーブル環境を一括自動生成するエディタ拡張。
    /// VRChatのWorld Space UI仕様（VRCUiShape + BoxCollider + SendCustomEvent）に完全準拠。
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
            tableTop.transform.SetParent(root.transform, false);
            tableTop.transform.localPosition = new Vector3(0, 0.7f, 0);
            tableTop.transform.localScale = new Vector3(2.0f, 0.05f, 2.0f);

            // テーブル脚
            GameObject tableLeg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tableLeg.name = "TableLeg";
            tableLeg.transform.SetParent(root.transform, false);
            tableLeg.transform.localPosition = new Vector3(0, 0.35f, 0);
            tableLeg.transform.localScale = new Vector3(0.3f, 0.35f, 0.3f);

            // 3. 山札オブジェクト (Deck)
            GameObject deckObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            deckObj.name = "DeckObject";
            deckObj.transform.SetParent(root.transform, false);
            deckObj.transform.localPosition = new Vector3(0, 0.75f, 0.15f);
            deckObj.transform.localScale = new Vector3(0.12f, 0.05f, 0.18f);

            // 捨て札オブジェクト (Discard)
            GameObject discardObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            discardObj.name = "DiscardArea";
            discardObj.transform.SetParent(root.transform, false);
            discardObj.transform.localPosition = new Vector3(0.2f, 0.73f, 0.15f);
            discardObj.transform.localScale = new Vector3(0.12f, 0.01f, 0.18f);

            // 4. コアコンポーネントのアタッチ (DeckManager, TableManager, TableUIController)
            DeckManager deckManager = root.AddComponent<DeckManager>();
            SerializedObject soDeck = new SerializedObject(deckManager);
            soDeck.FindProperty("deckMeshTransform").objectReferenceValue = deckObj.transform;
            soDeck.ApplyModifiedProperties();

            TableManager tableManager = root.AddComponent<TableManager>();
            TableUIController uiController = root.AddComponent<TableUIController>();

            // UdonBehaviourの取得（SendCustomEventのターゲット）
            UdonBehaviour udonUI = uiController.GetComponent<UdonBehaviour>();

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

                // VRCStation コンポーネントの追加
                VRCStation station = seatObj.AddComponent<VRCStation>();

                // SeatController コンポーネントの追加
                SeatController seatCtrl = seatObj.AddComponent<SeatController>();
                seats[i] = seatCtrl;

                // 手札トレイの生成（座席とテーブルの中間に配置）
                GameObject trayObj = new GameObject($"HandTray_{i}");
                trayObj.transform.SetParent(root.transform, false);
                Vector3 trayPos = Vector3.Lerp(tableTop.transform.localPosition, seatObj.transform.localPosition, 0.55f);
                trayPos.y = 0.74f;
                trayObj.transform.localPosition = trayPos;
                trayObj.transform.localRotation = Quaternion.Euler(20f, seatYRotations[i], 0);

                HandTrayController trayCtrl = trayObj.AddComponent<HandTrayController>();
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

            // 6. 操作パネル (World Space Canvas) の生成
            // ★Scale: 0.01f に完全調整
            GameObject canvasObj = new GameObject("TableUI_Canvas");
            canvasObj.transform.SetParent(root.transform, false);
            canvasObj.transform.localPosition = new Vector3(0, 0.74f, -0.25f);
            canvasObj.transform.localRotation = Quaternion.Euler(35f, 0, 0);
            canvasObj.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f); // ★Scale 0.01

            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObj.AddComponent<GraphicRaycaster>();

            // ★VRChatの必須コンポーネント: VRCUiShape ＋ BoxCollider
            VRCUiShape uiShape = canvasObj.AddComponent<VRCUiShape>();
            BoxCollider canvasCollider = canvasObj.AddComponent<BoxCollider>();
            canvasCollider.size = new Vector3(50f, 10f, 1f); // CanvasのRectTransform(50x10)に一致
            canvasCollider.isTrigger = true;

            RectTransform canvasRect = canvasObj.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(50f, 10f); // 0.01倍で 50cm x 10cm

            // 背景パネル
            GameObject panelObj = new GameObject("Panel");
            panelObj.transform.SetParent(canvasObj.transform, false);
            panelObj.transform.localPosition = Vector3.zero;
            panelObj.transform.localScale = Vector3.one;
            Image panelImg = panelObj.AddComponent<Image>();
            panelImg.color = new Color(0.12f, 0.12f, 0.15f, 0.9f);
            panelImg.raycastTarget = false; // 背景はクリック透過
            RectTransform panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(50f, 10f);

            // 5つのボタン生成（Scale 0.01基準: 幅8.5, 高さ7）
            Button dealBtn = CreateButton(canvasObj.transform, "DealBtn", "配る", new Vector2(-19f, 0), new Vector2(8.5f, 7f));
            Button drawBtn = CreateButton(canvasObj.transform, "DrawBtn", "引く", new Vector2(-9.5f, 0), new Vector2(8.5f, 7f));
            Button shuffleBtn = CreateButton(canvasObj.transform, "ShuffleBtn", "混ぜる", new Vector2(0f, 0), new Vector2(8.5f, 7f));
            Button passBtn = CreateButton(canvasObj.transform, "PassBtn", "パス", new Vector2(9.5f, 0), new Vector2(8.5f, 7f));
            Button resetBtn = CreateButton(canvasObj.transform, "ResetBtn", "リセット", new Vector2(19f, 0), new Vector2(8.5f, 7f));

            // ★VRChat公式推奨: UdonBehaviour.SendCustomEvent による確実なイベントルーティング
            if (udonUI != null)
            {
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(dealBtn.onClick, udonUI.SendCustomEvent, "OnClickDealButton");
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(drawBtn.onClick, udonUI.SendCustomEvent, "OnClickDrawButton");
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(shuffleBtn.onClick, udonUI.SendCustomEvent, "OnClickShuffleButton");
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(passBtn.onClick, udonUI.SendCustomEvent, "OnClickAdvanceTurnButton");
                UnityEditor.Events.UnityEventTools.AddStringPersistentListener(resetBtn.onClick, udonUI.SendCustomEvent, "OnClickResetButton");
            }
            else
            {
                // フォールバック
                UnityEditor.Events.UnityEventTools.AddPersistentListener(dealBtn.onClick, uiController.OnClickDealButton);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(drawBtn.onClick, uiController.OnClickDrawButton);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(shuffleBtn.onClick, uiController.OnClickShuffleButton);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(passBtn.onClick, uiController.OnClickAdvanceTurnButton);
                UnityEditor.Events.UnityEventTools.AddPersistentListener(resetBtn.onClick, uiController.OnClickResetButton);
            }

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
            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> VRChat対応UI(Scale 0.01, BoxCollider, SendCustomEvent)のセットアップが完了しました！</color>");
        }

        private static Button CreateButton(Transform parent, string name, string text, Vector2 pos, Vector2 size)
        {
            GameObject btnObj = new GameObject(name);
            btnObj.transform.SetParent(parent, false);
            btnObj.transform.localPosition = new Vector3(pos.x, pos.y, 0);
            btnObj.transform.localScale = Vector3.one;

            Image img = btnObj.AddComponent<Image>();
            img.color = new Color(0.18f, 0.45f, 0.8f, 1.0f);
            img.raycastTarget = true; // クリック判定

            Button btn = btnObj.AddComponent<Button>();
            btn.navigation = new Navigation { mode = Navigation.Mode.None }; // 誤作動防止

            RectTransform rt = btnObj.GetComponent<RectTransform>();
            rt.sizeDelta = size;

            // テキスト
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(btnObj.transform, false);
            textObj.transform.localPosition = Vector3.zero;
            textObj.transform.localScale = Vector3.one;
            Text txt = textObj.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 2; // Scale 0.01環境に適したフォントサイズ
            txt.resizeTextForBestFit = true;
            txt.resizeTextMinSize = 1;
            txt.resizeTextMaxSize = 4;
            txt.raycastTarget = false;

            RectTransform textRt = textObj.GetComponent<RectTransform>();
            textRt.sizeDelta = size;

            return btn;
        }
    }
}
