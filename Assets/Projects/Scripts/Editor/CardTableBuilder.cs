using UnityEditor;
using UnityEngine;
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
using System.Collections.Generic;

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
        [MenuItem("Tools/VRC-BoardGameKit/Utilities/NotoSansJPフォントを一括再適用 (Re-apply TMP Font)", false, 30)]
        public static void ReapplyNotoSansJPFontToAllTMP()
        {
            string fontPath = "Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset";
            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (jpFont == null)
            {
                Debug.LogError($"[VRC-BoardGameKit] フォントアセットが見つかりません: {fontPath}");
                return;
            }

            // シーン内の全 TMP_Text (非アクティブ含む) を検索して再アタッチ
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

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            AssetDatabase.Refresh();
            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> シーン内の全 {sceneCount} 箇所の TextMeshPro に NotoSansJP-Medium SDF を再アタッチしました！</color>");
        }

        private static void EnsureAllProgramAssets()
        {
            string[] scripts = new string[]
            {
                "Assets/Projects/Scripts/Core/DeckManager.cs",
                "Assets/Projects/Scripts/Core/TableManager.cs",
                "Assets/Projects/Scripts/Core/TableUIController.cs",
                "Assets/Projects/Scripts/Core/SeatController.cs",
                "Assets/Projects/Scripts/Core/DeckInteractHandler.cs",
                "Assets/Projects/Scripts/Core/UIButtonHandler.cs",
                "Assets/Projects/Scripts/Core/DebugClickButton.cs",
                "Assets/Projects/Scripts/Core/CardSnapZone.cs",
                "Assets/Projects/Scripts/Core/CardController.cs",
                "Assets/Projects/Scripts/Core/PersonalHandArea.cs",
                "Assets/Projects/Scripts/Core/DrawCardButton.cs",
                "Assets/Projects/Scripts/Core/PlayCardButton.cs",
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

        /// <summary>
        /// 幼い少女カードのPrefabをビルド・保存する内部ヘルパー
        /// </summary>
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

            // 6. VRCPickup の追加 (手持ち無効化・クリック選択方式への移行)
            VRCPickup pickup = cardObj.AddComponent<VRCPickup>();
            pickup.pickupable = false;
            SerializedObject soVrcPickup = new SerializedObject(pickup);
            soVrcPickup.FindProperty("pickupable").boolValue = false;
            soVrcPickup.FindProperty("AutoHold").intValue = 0; // 0 = No (AutoHold オフ)
            soVrcPickup.FindProperty("orientation").intValue = 2; // 2 = Grip
            soVrcPickup.FindProperty("ThrowVelocityBoostScale").floatValue = 0f;
            soVrcPickup.FindProperty("ThrowVelocityBoostMinSpeed").floatValue = 0f;
            soVrcPickup.FindProperty("InteractionText").stringValue = "カードを選ぶ (Select)";
            soVrcPickup.FindProperty("UseText").stringValue = "カードを出す (Play)";
            soVrcPickup.ApplyModifiedProperties();

            // 7. CardController のアタッチ (Tell Don't Ask準拠: 暴れ防止・空中静止・磁石スナップ制御・クリック選択)
            CardController cardController = cardObj.AddUdonSharpComponent<CardController>();
            SerializedObject soCard = new SerializedObject(cardController);
            soCard.FindProperty("keepKinematicWhileHeld").boolValue = true;
            soCard.FindProperty("selectionElevation").floatValue = 0.15f;
            soCard.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(cardController);

            // 8. VRCObjectSync (位置・回転のネットワーク同期)
            cardObj.AddComponent<VRCObjectSync>();

            // 10. Prefabとして保存
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(cardObj, prefabPath);
            Object.DestroyImmediate(cardObj);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 幼い少女カード (Card_01_YoungGirl.prefab) を正常に生成・保存しました！ [サイズ: 70.0cm x 98.0cm]</color>");
            return savedPrefab;
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

            // 2. 中央の場のプレイエリア (Center Play Area) の生成 (70cm x 98cm 大判CardSnapZone, スタック許可)
            GameObject centerPlaySlot = CreateArcadeSnapSlot("Center_PlaySlot", new Vector3(0, config.slotHeightY, 0), Quaternion.Euler(30f, 0, 0));
            centerPlaySlot.transform.SetParent(root.transform, false);

            CardSnapZone centerSnapZone = centerPlaySlot.GetComponent<CardSnapZone>();
            if (centerSnapZone != null)
            {
                centerSnapZone.SetAllowStack(true);
                SerializedObject soZone = new SerializedObject(centerSnapZone);
                soZone.FindProperty("allowStack").boolValue = true;
                soZone.FindProperty("stackElevationOffset").floatValue = 0.002f;
                soZone.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(centerSnapZone);
            }

            // TableManager へのバインド同期
            tableManager.SetCenterPlayZone(centerSnapZone);
            SerializedObject soTable = new SerializedObject(tableManager);
            soTable.FindProperty("centerPlayZone").objectReferenceValue = centerSnapZone;
            soTable.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(tableManager);

            // 3. 4つのプレイヤーエリア（登録キューブ ＆ 円弧状手札スロット）
            Vector3[] seatPositions = {
                new Vector3(0, 0, -2.5f),  // Seat 0: South (正面手前)
                new Vector3(0, 0, 2.5f),   // Seat 1: North (対面奥)
                new Vector3(2.5f, 0, 0),   // Seat 2: East (右)
                new Vector3(-2.5f, 0, 0)   // Seat 3: West (左)
            };
            float[] seatYaws = { 0f, 180f, -90f, 90f };

            SeatController[] seatControllers = new SeatController[4];
            DrawCardButton[] drawButtons = new DrawCardButton[4];
            PlayCardButton[] playButtons = new PlayCardButton[4];

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

                // C. パーソナル操作パネル (3D物理ボタンパネル: BoxCollider + UdonSharp Interact)
                Vector3 uiPos = new Vector3(0f, Mathf.Max(config.slotHeightY - 0.35f, 0.50f), config.radius * 0.55f);
                Quaternion uiRot = Quaternion.Euler(40f, 0f, 0f); // 手前見下ろし40度
                GameObject personalUiObj = CreatePersonalButtonPanel($"Personal_Buttons_Seat{i}", uiPos, uiRot, handArea, null, tableManager, i);
                personalUiObj.transform.SetParent(slotContainer.transform, false);

                DrawCardButton drawBtn = personalUiObj.GetComponentInChildren<DrawCardButton>();
                drawButtons[i] = drawBtn;

                PlayCardButton playBtn = personalUiObj.GetComponentInChildren<PlayCardButton>();
                playButtons[i] = playBtn;

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
            soTable.Update();
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

                // 各座席の DrawCardButton に deckMgr をバインド
                for (int i = 0; i < 4; i++)
                {
                    if (drawButtons[i] != null)
                    {
                        drawButtons[i].SetDeckManager(deckMgr);
                        SerializedObject soDrawBtn = new SerializedObject(drawButtons[i]);
                        soDrawBtn.FindProperty("deckManager").objectReferenceValue = deckMgr;
                        soDrawBtn.ApplyModifiedProperties();
                        UdonSharpEditorUtility.CopyProxyToUdon(drawButtons[i]);

                        // エディタ生成時点の初期残数を即時反映 (案A)
                        drawButtons[i].UpdateRemainingCount(config.poolCardCount);
                    }
                }

                // DeckManager 側に全座席の DrawCardButton をバインド (C#プロキシ＆シリアライズ)
                deckMgr.SetHandDrawButtons(drawButtons);
                SerializedObject soDeckMgr = new SerializedObject(deckMgr);
                SerializedProperty propDrawBtns = soDeckMgr.FindProperty("handDrawButtons");
                propDrawBtns.arraySize = drawButtons.Length;
                for (int b = 0; b < drawButtons.Length; b++)
                {
                    propDrawBtns.GetArrayElementAtIndex(b).objectReferenceValue = drawButtons[b];
                }
                soDeckMgr.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(deckMgr);
            }

            Selection.activeGameObject = root;
            Debug.Log($"<color=#00FF99>[VRC-BoardGameKit] プレイヤー包囲型円弧スロット空間 (手札 {config.slotCount} 枠 / カードプール {config.poolCardCount} 枚 / 半径 {config.radius:F2}m / 手元ドローUI完備) の構築が完了しました！</color>");
        }


        private static GameObject CreatePersonalButtonPanel(string name, Vector3 localPos, Quaternion localRot, PersonalHandArea handArea, DeckManager deckMgr, TableManager tableManager, int seatIndex)
        {
            GameObject panelRoot = new GameObject(name);
            panelRoot.transform.localPosition = localPos;
            panelRoot.transform.localRotation = localRot;
            panelRoot.transform.localScale = Vector3.one;

            TMP_FontAsset jpFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset");

            // ボタン用マテリアル（静的アセットとしてロードまたは自動生成）
            string materialsDir = "Assets/Projects/Components/Materials";
            string drawMatPath = $"{materialsDir}/Button_Draw_Emerald.mat";
            string playMatPath = $"{materialsDir}/Button_Play_Ocean.mat";

            Shader unlitShader = Shader.Find("Unlit/Color");
            if (unlitShader == null) unlitShader = Shader.Find("Standard");

            Material drawMat = AssetDatabase.LoadAssetAtPath<Material>(drawMatPath);
            if (drawMat == null)
            {
                drawMat = new Material(unlitShader);
                drawMat.color = new Color(0.10f, 0.50f, 0.28f, 1.0f); // 視認性の高いエメラルドグリーン
                AssetDatabase.CreateAsset(drawMat, drawMatPath);
            }

            Material playMat = AssetDatabase.LoadAssetAtPath<Material>(playMatPath);
            if (playMat == null)
            {
                playMat = new Material(unlitShader);
                playMat.color = new Color(0.12f, 0.42f, 0.65f, 1.0f); // 落ち着いたオーシャンブルー
                AssetDatabase.CreateAsset(playMat, playMatPath);
            }

            // ==========================================
            // 1. 左側: 3Dドローボタン (Draw_Button)
            // ==========================================
            GameObject drawBtnObj = new GameObject("Draw_Button");
            drawBtnObj.transform.SetParent(panelRoot.transform, false);
            drawBtnObj.transform.localPosition = new Vector3(-0.16f, 0f, 0f);
            drawBtnObj.transform.localRotation = Quaternion.identity;
            drawBtnObj.transform.localScale = Vector3.one;

            // 3D物理コライダー (28cm x 14cm x 2cm)
            BoxCollider drawCol = drawBtnObj.AddComponent<BoxCollider>();
            drawCol.size = new Vector3(0.28f, 0.14f, 0.02f);
            drawCol.center = Vector3.zero;

            // ボタン本体のCubeメッシュ（コライダーは親のBoxColliderで一括管理するため削除）
            GameObject drawMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            drawMesh.name = "ButtonMesh";
            drawMesh.transform.SetParent(drawBtnObj.transform, false);
            drawMesh.transform.localPosition = Vector3.zero;
            drawMesh.transform.localRotation = Quaternion.identity;
            drawMesh.transform.localScale = new Vector3(0.28f, 0.14f, 0.02f);
            Object.DestroyImmediate(drawMesh.GetComponent<Collider>());
            drawMesh.GetComponent<MeshRenderer>().sharedMaterial = drawMat;

            // UdonSharpコンポーネント (Interact() で動作)
            DrawCardButton drawUdon = drawBtnObj.AddUdonSharpComponent<DrawCardButton>();
            drawUdon.SetLinkedHandArea(handArea);
            if (deckMgr != null) drawUdon.SetDeckManager(deckMgr);

            SerializedObject soDrawBtn = new SerializedObject(drawUdon);
            soDrawBtn.FindProperty("linkedHandArea").objectReferenceValue = handArea;
            if (deckMgr != null)
            {
                soDrawBtn.FindProperty("deckManager").objectReferenceValue = deckMgr;
            }
            soDrawBtn.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(drawUdon);

            UdonBehaviour udonDrawBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(drawUdon);
            if (udonDrawBacking != null)
            {
                udonDrawBacking.interactText = "カードを引く (Draw)";
            }

            // 3D TextMeshPro（ボタン表面の2mm手前に配置）
            GameObject drawTextObj = new GameObject("Text");
            drawTextObj.transform.SetParent(drawBtnObj.transform, false);
            drawTextObj.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            drawTextObj.transform.localRotation = Quaternion.identity;
            drawTextObj.transform.localScale = Vector3.one;

            TextMeshPro drawTmp = drawTextObj.AddComponent<TextMeshPro>();
            if (jpFont != null)
            {
                drawTmp.font = jpFont;
                drawTmp.fontSharedMaterial = jpFont.material;
            }
            drawTmp.text = "カードを引く\n<size=70%>DRAW CARD</size>";
            drawTmp.alignment = TextAlignmentOptions.Center;
            drawTmp.color = Color.white;
            drawTmp.enableAutoSizing = true;
            drawTmp.fontSizeMin = 0.5f;
            drawTmp.fontSizeMax = 2.4f;
            RectTransform drawTextRt = drawTextObj.GetComponent<RectTransform>();
            if (drawTextRt != null)
            {
                drawTextRt.sizeDelta = new Vector2(0.28f, 0.14f);
            }

            // DrawCardButton に buttonText (TextMeshPro) をバインド
            drawUdon.SetButtonText(drawTmp);
            soDrawBtn.Update();
            soDrawBtn.FindProperty("buttonText").objectReferenceValue = drawTmp;
            soDrawBtn.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(drawUdon);

            // ==========================================
            // 2. 右側: 3Dプレイボタン (Play_Button)
            // ==========================================
            GameObject playBtnObj = new GameObject("Play_Button");
            playBtnObj.transform.SetParent(panelRoot.transform, false);
            playBtnObj.transform.localPosition = new Vector3(0.16f, 0f, 0f);
            playBtnObj.transform.localRotation = Quaternion.identity;
            playBtnObj.transform.localScale = Vector3.one;

            // 3D物理コライダー (28cm x 14cm x 2cm)
            BoxCollider playCol = playBtnObj.AddComponent<BoxCollider>();
            playCol.size = new Vector3(0.28f, 0.14f, 0.02f);
            playCol.center = Vector3.zero;

            // ボタン本体のCubeメッシュ（コライダーは削除）
            GameObject playMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playMesh.name = "ButtonMesh";
            playMesh.transform.SetParent(playBtnObj.transform, false);
            playMesh.transform.localPosition = Vector3.zero;
            playMesh.transform.localRotation = Quaternion.identity;
            playMesh.transform.localScale = new Vector3(0.28f, 0.14f, 0.02f);
            Object.DestroyImmediate(playMesh.GetComponent<Collider>());
            playMesh.GetComponent<MeshRenderer>().sharedMaterial = playMat;

            // UdonSharpコンポーネント (Interact() で動作)
            PlayCardButton playUdon = playBtnObj.AddUdonSharpComponent<PlayCardButton>();
            playUdon.SetTableManager(tableManager);

            SerializedObject soPlayBtn = new SerializedObject(playUdon);
            soPlayBtn.FindProperty("tableManager").objectReferenceValue = tableManager;
            soPlayBtn.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(playUdon);

            UdonBehaviour udonPlayBacking = UdonSharpEditorUtility.GetBackingUdonBehaviour(playUdon);
            if (udonPlayBacking != null)
            {
                udonPlayBacking.interactText = "カードを出す (Play)";
            }

            // 3D TextMeshPro（ボタン表面の2mm手前に配置）
            GameObject playTextObj = new GameObject("Text");
            playTextObj.transform.SetParent(playBtnObj.transform, false);
            playTextObj.transform.localPosition = new Vector3(0f, 0f, -0.012f);
            playTextObj.transform.localRotation = Quaternion.identity;
            playTextObj.transform.localScale = Vector3.one;

            TextMeshPro playTmp = playTextObj.AddComponent<TextMeshPro>();
            if (jpFont != null)
            {
                playTmp.font = jpFont;
                playTmp.fontSharedMaterial = jpFont.material;
            }
            playTmp.text = "カードを出す\n<size=70%>PLAY CARD</size>";
            playTmp.alignment = TextAlignmentOptions.Center;
            playTmp.color = Color.white;
            playTmp.enableAutoSizing = true;
            playTmp.fontSizeMin = 0.5f;
            playTmp.fontSizeMax = 2.4f;
            RectTransform playTextRt = playTextObj.GetComponent<RectTransform>();
            if (playTextRt != null)
            {
                playTextRt.sizeDelta = new Vector2(0.28f, 0.14f);
            }

            EditorUtility.SetDirty(panelRoot);
            return panelRoot;
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

            // 2. DeckManager のアタッチ
            DeckManager deckManager = deckRoot.AddUdonSharpComponent<DeckManager>();

            // 3. カードオブジェクトプールの事前生成 (指定枚数の Card_01_YoungGirl をインスタンス化)
            GameObject poolContainer = new GameObject("CardPoolContainer");
            poolContainer.transform.SetParent(deckRoot.transform, false);
            poolContainer.transform.localPosition = Vector3.zero;

            // 常に最新のPrefab設定を保証（pickupable=false, CardSlotController除去済み）
            string cardPrefabPath = "Assets/Projects/Prefabs/Card_01_YoungGirl.prefab";
            GameObject cardPrefab = BuildYoungGirlCardPrefab();
            if (cardPrefab == null)
            {
                cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cardPrefabPath);
            }

            CardController[] poolCards = new CardController[poolCount];
            for (int c = 0; c < poolCount; c++)
            {
                GameObject cardInstance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab);
                cardInstance.name = $"PoolCard_{c:D2}";
                cardInstance.transform.SetParent(poolContainer.transform, false);

                // VRCPickup の手持ち無効化をインスタンスレベルでも徹底保証
                VRCPickup pickup = cardInstance.GetComponent<VRCPickup>();
                if (pickup != null)
                {
                    pickup.pickupable = false;
                    SerializedObject soPickup = new SerializedObject(pickup);
                    soPickup.FindProperty("pickupable").boolValue = false;
                    soPickup.ApplyModifiedProperties();
                }

                CardController cardCtrl = cardInstance.GetComponent<CardController>();
                if (cardCtrl != null)
                {
                    cardCtrl.SetCardId(c);
                    cardCtrl.SetTableManager(tableManager);
                    // 初期状態: 山札位置に重なって非表示待機
                    cardCtrl.ResetToDeck(localPos, localRot);
                    UdonSharpEditorUtility.CopyProxyToUdon(cardCtrl);
                    poolCards[c] = cardCtrl;
                }
            }

            // 4. DeckInteractHandler のアタッチ (3D直接インタラクト)
            DeckInteractHandler deckHandler = deckQuad.AddUdonSharpComponent<DeckInteractHandler>();
            deckHandler.SetDeckManager(deckManager);
            deckHandler.SetSeatControllers(seats);
            SerializedObject soHandler = new SerializedObject(deckHandler);
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

            // 5. DeckManager 設定 (C#プロキシ＆シリアライズの完全同期)
            deckManager.SetInteractHandler(deckHandler);
            deckManager.SetCardPool(poolCards);

            SerializedObject soDeck = new SerializedObject(deckManager);
            soDeck.FindProperty("defaultCardCount").intValue = poolCount;
            soDeck.FindProperty("deckMeshTransform").objectReferenceValue = deckQuad.transform;
            soDeck.FindProperty("interactHandler").objectReferenceValue = deckHandler;
            SerializedProperty propPool = soDeck.FindProperty("cardPool");
            propPool.arraySize = poolCount;
            for (int c = 0; c < poolCount; c++)
            {
                propPool.GetArrayElementAtIndex(c).objectReferenceValue = poolCards[c];
            }
            soDeck.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(deckManager);

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
            guideMat.color = new Color(0.2f, 0.7f, 1.0f, 0.35f); // 視認性の高い水色半透明
            guideQuad.GetComponent<MeshRenderer>().sharedMaterial = guideMat;

            // CardSnapZone コンポーネントのアタッチ
            CardSnapZone snapZone = slotObj.AddUdonSharpComponent<CardSnapZone>();
            snapZone.SetSlotName(name);
            SerializedObject soZone = new SerializedObject(snapZone);
            soZone.FindProperty("slotName").stringValue = name;
            soZone.FindProperty("guideRenderer").objectReferenceValue = guideQuad.GetComponent<MeshRenderer>();
            soZone.FindProperty("defaultGuideColor").colorValue = new Color(0.2f, 0.7f, 1.0f, 0.35f);
            soZone.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(snapZone);

            return slotObj;
        }

        /// <summary>
        /// 指定されたカードの表面・裏面テクスチャを設定・更新する
        /// </summary>
        public static void SetCardTextures(CardController card, Texture2D frontTex, Texture2D backTex)
        {
            if (card == null) return;
            MeshRenderer mr = card.GetComponent<MeshRenderer>();
            if (mr == null) return;

            Undo.RecordObject(mr, "Set Card Textures");

            Material mat = mr.sharedMaterial;
            if (mat == null || mat.shader == null || mat.shader.name != "BoardGameKit/CardTwoSided")
            {
                Shader shader = Shader.Find("BoardGameKit/CardTwoSided");
                if (shader == null) shader = Shader.Find("Unlit/Texture");
                mat = new Material(shader);
                mr.sharedMaterial = mat;
            }
            else
            {
                // 個別マテリアルとしてインスタンス化
                mat = new Material(mat);
                mr.sharedMaterial = mat;
            }

            if (frontTex != null) mat.SetTexture("_MainTex", frontTex);
            if (backTex != null) mat.SetTexture("_BackTex", backTex);
            mat.SetFloat("_FlipBackUV", 1.0f);
            mat.SetFloat("_ShowFront", 1.0f);

            EditorUtility.SetDirty(mr);
            EditorUtility.SetDirty(card.gameObject);
        }

        /// <summary>
        /// 山札のオブジェクトプール枚数を動的に増減リサイズする
        /// </summary>
        public static void ResizeDeckPool(DeckManager deckManager, int newPoolCount)
        {
            if (deckManager == null || newPoolCount < 1) return;

            Undo.RecordObject(deckManager, "Resize Deck Pool");

            // CardPoolContainer を検索
            Transform container = deckManager.transform.Find("CardPoolContainer");
            if (container == null)
            {
                GameObject newContainer = new GameObject("CardPoolContainer");
                newContainer.transform.SetParent(deckManager.transform, false);
                newContainer.transform.localPosition = Vector3.zero;
                container = newContainer.transform;
                Undo.RegisterCreatedObjectUndo(newContainer, "Create CardPoolContainer");
            }

            List<CardController> currentList = new List<CardController>();
            if (deckManager.CardPool != null)
            {
                foreach (var c in deckManager.CardPool)
                {
                    if (c != null) currentList.Add(c);
                }
            }

            string cardPrefabPath = "Assets/Projects/Prefabs/Card_01_YoungGirl.prefab";
            GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(cardPrefabPath);
            if (cardPrefab == null)
            {
                cardPrefab = BuildYoungGirlCardPrefab();
            }

            // 増加する場合: 新規カードを生成
            while (currentList.Count < newPoolCount)
            {
                int newId = currentList.Count;
                GameObject cardInstance = (GameObject)PrefabUtility.InstantiatePrefab(cardPrefab);
                cardInstance.name = $"PoolCard_{newId:D2}";
                cardInstance.transform.SetParent(container, false);
                Undo.RegisterCreatedObjectUndo(cardInstance, "Create Pool Card");

                VRCPickup pickup = cardInstance.GetComponent<VRCPickup>();
                if (pickup != null)
                {
                    pickup.pickupable = false;
                    SerializedObject soPickup = new SerializedObject(pickup);
                    soPickup.FindProperty("pickupable").boolValue = false;
                    soPickup.ApplyModifiedProperties();
                }

                CardController cardCtrl = cardInstance.GetComponent<CardController>();
                if (cardCtrl != null)
                {
                    cardCtrl.SetCardId(newId);
                    TableManager tm = Object.FindObjectOfType<TableManager>();
                    if (tm != null) cardCtrl.SetTableManager(tm);
                    cardCtrl.ResetToDeck(deckManager.transform.localPosition, deckManager.transform.localRotation);
                    UdonSharpEditorUtility.CopyProxyToUdon(cardCtrl);
                    currentList.Add(cardCtrl);
                }
            }

            // 減少する場合: 末尾から削除
            while (currentList.Count > newPoolCount)
            {
                int lastIndex = currentList.Count - 1;
                CardController toRemove = currentList[lastIndex];
                currentList.RemoveAt(lastIndex);
                if (toRemove != null)
                {
                    Undo.DestroyObjectImmediate(toRemove.gameObject);
                }
            }

            // DeckManager へのシリアライズ反映
            SerializedObject soDeck = new SerializedObject(deckManager);
            soDeck.FindProperty("defaultCardCount").intValue = newPoolCount;
            SerializedProperty propPool = soDeck.FindProperty("cardPool");
            propPool.arraySize = newPoolCount;
            for (int i = 0; i < newPoolCount; i++)
            {
                propPool.GetArrayElementAtIndex(i).objectReferenceValue = currentList[i];
            }
            soDeck.ApplyModifiedProperties();
            UdonSharpEditorUtility.CopyProxyToUdon(deckManager);

            EditorUtility.SetDirty(deckManager);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 山札カードプールを {newPoolCount} 枚に更新しました！</color>");
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

    /// <summary>
    /// 山札の総数（PoolCard枚数）の変更、表裏テクスチャの設定、複数選択一括割当、
    /// 連番ドラッグ＆ドロップ流し込み、全カード統一設定を行う専用エディタウィンドウ。
    /// </summary>
    public class DeckCardEditorWindow : EditorWindow
    {
        [SerializeField] private DeckManager targetDeck;
        [SerializeField] private int newPoolCount = 20;

        // 全体一括設定用
        [SerializeField] private Texture2D bulkBackTex;
        [SerializeField] private Texture2D bulkFrontTex;

        // 選択カード一括設定用
        [SerializeField] private Texture2D selectedFrontTex;
        [SerializeField] private Texture2D selectedBackTex;

        // 連番D&D用
        [SerializeField] private int dragDropStartId = 0;

        private bool[] selectionFlags;
        private Vector2 windowScrollPos;
        private Vector2 cardListScrollPos;

        [MenuItem("Tools/VRC-BoardGameKit/Deck & Card Editor (山札・カード画像設定 GUI)", false, 2)]
        public static void OpenWindow()
        {
            var window = GetWindow<DeckCardEditorWindow>("Deck & Card Editor");
            window.minSize = new Vector2(460, 600);
            window.Show();
        }

        private void OnEnable()
        {
            FindTargetDeckIfNull();
            SyncSelectionFlags();
        }

        private void FindTargetDeckIfNull()
        {
            if (targetDeck == null)
            {
                targetDeck = Object.FindObjectOfType<DeckManager>();
                if (targetDeck != null && targetDeck.CardPool != null)
                {
                    newPoolCount = targetDeck.CardPool.Length;
                }
            }
        }

        private void SyncSelectionFlags()
        {
            int count = (targetDeck != null && targetDeck.CardPool != null) ? targetDeck.CardPool.Length : 0;
            if (selectionFlags == null || selectionFlags.Length != count)
            {
                selectionFlags = new bool[count];
            }
        }

        private void OnGUI()
        {
            windowScrollPos = EditorGUILayout.BeginScrollView(windowScrollPos);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Deck & Card Editor (山札・カード設定)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("山札枚数の変更、カード表裏画像の設定・一括流し込み・統一設定", EditorStyles.miniLabel);
            EditorGUILayout.Space(8);

            // 1. ターゲット山札の指定
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("【対象の山札 (DeckManager)】", EditorStyles.boldLabel);
            DeckManager prevDeck = targetDeck;
            targetDeck = (DeckManager)EditorGUILayout.ObjectField("Target Deck", targetDeck, typeof(DeckManager), true);
            if (targetDeck != prevDeck)
            {
                if (targetDeck != null && targetDeck.CardPool != null)
                {
                    newPoolCount = targetDeck.CardPool.Length;
                }
                SyncSelectionFlags();
            }

            if (targetDeck == null)
            {
                if (GUILayout.Button("シーン内の山札を自動検索して選択"))
                {
                    FindTargetDeckIfNull();
                    SyncSelectionFlags();
                }
                EditorGUILayout.HelpBox("シーン内に山札（DeckManager）が見つかりません。卓を生成するか、山札をアサインしてください。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndScrollView();
                return;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 2. 山札プール枚数の増減
            int currentPoolCount = (targetDeck.CardPool != null) ? targetDeck.CardPool.Length : 0;
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("【1. 山札プール総数の変更】", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"現在のプール枚数: {currentPoolCount} 枚", EditorStyles.label);

            newPoolCount = EditorGUILayout.IntSlider("変更後のプール枚数 (枚)", newPoolCount, 1, 100);

            if (newPoolCount != currentPoolCount)
            {
                GUI.backgroundColor = new Color(1.0f, 0.7f, 0.2f);
                if (GUILayout.Button($"プール枚数を {currentPoolCount} 枚 ➔ {newPoolCount} 枚 に更新", GUILayout.Height(30)))
                {
                    CardTableBuilder.ResizeDeckPool(targetDeck, newPoolCount);
                    SyncSelectionFlags();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("プール枚数は最新です", GUILayout.Height(24));
                GUI.enabled = true;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 3. 全カード共通画像（統一設定）
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("【2. 全カード画像の一括統一設定】", EditorStyles.boldLabel);
            
            // 裏面統一
            EditorGUILayout.BeginHorizontal();
            bulkBackTex = (Texture2D)EditorGUILayout.ObjectField("共通 裏面画像", bulkBackTex, typeof(Texture2D), false);
            if (GUILayout.Button("全カードの裏面を一括統一", GUILayout.Width(170)))
            {
                if (bulkBackTex != null && targetDeck.CardPool != null)
                {
                    foreach (var card in targetDeck.CardPool)
                    {
                        if (card != null)
                        {
                            CardTableBuilder.SetCardTextures(card, null, bulkBackTex);
                        }
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                    Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 全 {targetDeck.CardPool.Length} 枚の裏面を統一画像に更新しました！</color>");
                }
            }
            EditorGUILayout.EndHorizontal();

            // 表面統一
            EditorGUILayout.BeginHorizontal();
            bulkFrontTex = (Texture2D)EditorGUILayout.ObjectField("共通 表面画像", bulkFrontTex, typeof(Texture2D), false);
            if (GUILayout.Button("全カードの表面を一括統一", GUILayout.Width(170)))
            {
                if (bulkFrontTex != null && targetDeck.CardPool != null)
                {
                    foreach (var card in targetDeck.CardPool)
                    {
                        if (card != null)
                        {
                            CardTableBuilder.SetCardTextures(card, bulkFrontTex, null);
                        }
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                    Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 全 {targetDeck.CardPool.Length} 枚の表面を統一画像に更新しました！</color>");
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 4. 複数画像連番ドラッグ＆ドロップ流し込み
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("【3. 連番画像のドラッグ＆ドロップ一括流し込み】", EditorStyles.boldLabel);
            dragDropStartId = EditorGUILayout.IntField("開始カード番号 (No.)", dragDropStartId);
            if (dragDropStartId < 0) dragDropStartId = 0;

            Rect dropArea = GUILayoutUtility.GetRect(0.0f, 45.0f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "【ここに複数テクスチャをまとめてドラッグ＆ドロップ】\n(名前順にソートして開始番号から順次表面に割り当てます)", EditorStyles.helpBox);

            Event evt = Event.current;
            if (dropArea.Contains(evt.mousePosition))
            {
                if (evt.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                    evt.Use();
                }
                else if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    List<Texture2D> droppedTextures = new List<Texture2D>();
                    foreach (Object draggedObj in DragAndDrop.objectReferences)
                    {
                        if (draggedObj is Texture2D tex)
                        {
                            droppedTextures.Add(tex);
                        }
                    }

                    // 名前昇順でソート
                    droppedTextures.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

                    if (droppedTextures.Count > 0 && targetDeck.CardPool != null)
                    {
                        int applyCount = 0;
                        for (int i = 0; i < droppedTextures.Count; i++)
                        {
                            int targetIdx = dragDropStartId + i;
                            if (targetIdx < targetDeck.CardPool.Length && targetDeck.CardPool[targetIdx] != null)
                            {
                                CardTableBuilder.SetCardTextures(targetDeck.CardPool[targetIdx], droppedTextures[i], null);
                                applyCount++;
                            }
                        }
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                        Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> {applyCount} 枚の連番テクスチャを Card No.{dragDropStartId} から順次割り当てました！</color>");
                    }
                    evt.Use();
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 5. 複数選択 ＆ 選択カード一括設定
            SyncSelectionFlags();
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("【4. 選択カード一括設定】", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("すべて選択"))
            {
                for (int i = 0; i < selectionFlags.Length; i++) selectionFlags[i] = true;
            }
            if (GUILayout.Button("すべて解除"))
            {
                for (int i = 0; i < selectionFlags.Length; i++) selectionFlags[i] = false;
            }
            if (GUILayout.Button("選択反転"))
            {
                for (int i = 0; i < selectionFlags.Length; i++) selectionFlags[i] = !selectionFlags[i];
            }
            EditorGUILayout.EndHorizontal();

            // 選択表面一括
            EditorGUILayout.BeginHorizontal();
            selectedFrontTex = (Texture2D)EditorGUILayout.ObjectField("選択用 表面画像", selectedFrontTex, typeof(Texture2D), false);
            if (GUILayout.Button("選択カードの表面に一括適用", GUILayout.Width(170)))
            {
                if (selectedFrontTex != null && targetDeck.CardPool != null)
                {
                    int count = 0;
                    for (int i = 0; i < targetDeck.CardPool.Length; i++)
                    {
                        if (i < selectionFlags.Length && selectionFlags[i] && targetDeck.CardPool[i] != null)
                        {
                            CardTableBuilder.SetCardTextures(targetDeck.CardPool[i], selectedFrontTex, null);
                            count++;
                        }
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                    Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 選択された {count} 枚の表面テクスチャを更新しました！</color>");
                }
            }
            EditorGUILayout.EndHorizontal();

            // 選択裏面一括
            EditorGUILayout.BeginHorizontal();
            selectedBackTex = (Texture2D)EditorGUILayout.ObjectField("選択用 裏面画像", selectedBackTex, typeof(Texture2D), false);
            if (GUILayout.Button("選択カードの裏面に一括適用", GUILayout.Width(170)))
            {
                if (selectedBackTex != null && targetDeck.CardPool != null)
                {
                    int count = 0;
                    for (int i = 0; i < targetDeck.CardPool.Length; i++)
                    {
                        if (i < selectionFlags.Length && selectionFlags[i] && targetDeck.CardPool[i] != null)
                        {
                            CardTableBuilder.SetCardTextures(targetDeck.CardPool[i], null, selectedBackTex);
                            count++;
                        }
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                    Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> 選択された {count} 枚の裏面テクスチャを更新しました！</color>");
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // 6. 各カード一覧（個別設定）
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField($"【5. カード個別設定一覧 (全 {currentPoolCount} 枚)】", EditorStyles.boldLabel);

            cardListScrollPos = EditorGUILayout.BeginScrollView(cardListScrollPos, GUILayout.Height(300));
            if (targetDeck.CardPool != null)
            {
                for (int i = 0; i < targetDeck.CardPool.Length; i++)
                {
                    CardController card = targetDeck.CardPool[i];
                    if (card == null) continue;

                    MeshRenderer mr = card.GetComponent<MeshRenderer>();
                    Material mat = (mr != null) ? mr.sharedMaterial : null;
                    Texture2D currentFront = (mat != null && mat.HasProperty("_MainTex")) ? (Texture2D)mat.GetTexture("_MainTex") : null;
                    Texture2D currentBack = (mat != null && mat.HasProperty("_BackTex")) ? (Texture2D)mat.GetTexture("_BackTex") : null;

                    EditorGUILayout.BeginHorizontal("box");

                    // 選択チェックボックス
                    if (i < selectionFlags.Length)
                    {
                        selectionFlags[i] = EditorGUILayout.Toggle(selectionFlags[i], GUILayout.Width(20));
                    }

                    // カード番号
                    EditorGUILayout.LabelField($"No.{i:D2}", GUILayout.Width(45));

                    // 表面テクスチャ
                    EditorGUI.BeginChangeCheck();
                    Texture2D newFront = (Texture2D)EditorGUILayout.ObjectField(currentFront, typeof(Texture2D), false, GUILayout.Width(130));
                    if (EditorGUI.EndChangeCheck())
                    {
                        CardTableBuilder.SetCardTextures(card, newFront, currentBack);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                    }

                    // 裏面テクスチャ
                    EditorGUI.BeginChangeCheck();
                    Texture2D newBack = (Texture2D)EditorGUILayout.ObjectField(currentBack, typeof(Texture2D), false, GUILayout.Width(130));
                    if (EditorGUI.EndChangeCheck())
                    {
                        CardTableBuilder.SetCardTextures(card, currentFront, newBack);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
                    }

                    // Ping（選択）ボタン
                    if (GUILayout.Button("選択", GUILayout.Width(45)))
                    {
                        Selection.activeGameObject = card.gameObject;
                        EditorGUIUtility.PingObject(card.gameObject);
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(12);
            EditorGUILayout.EndScrollView();
        }
    }
}

