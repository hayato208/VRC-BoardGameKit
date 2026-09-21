using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 実行可能なテーブル全体アクション種別
    /// </summary>
    public enum TableActionType
    {
        ResetGame = 0,
        DealCardsToAll = 1,
        AdvanceTurn = 2
    }

    /// <summary>
    /// 卓上に配置される3D物理ボタン用の汎用トリガーコンポーネント。
    /// uGUIに依存せず、Collider付きオブジェクトへの Interact() により、
    /// TableManager の各種パブリックアクション（ゲーム初期化・一括配札・手番進行等）を実行する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TableActionTrigger : UdonSharpBehaviour
    {
        [Header("Target & Action")]
        [Tooltip("操作対象のTableManager")]
        [SerializeField] private TableManager tableManager;

        [Tooltip("実行するアクション種別")]
        [SerializeField] private TableActionType actionType = TableActionType.ResetGame;

        [Tooltip("DealCardsToAll実行時の各プレイヤーへの配布枚数")]
        [SerializeField] private int dealCardsCount = 5;

        [Header("Security & Anti-Spam")]
        [Tooltip("インスタンスのマスター（ホスト）のみ操作を許可するか")]
        [SerializeField] private bool requireMasterOnly = false;

        [Tooltip("連打・誤操作防止のクールダウン時間（秒）")]
        [SerializeField] private float cooldownSeconds = 1.0f;

        [Header("Visual / HUD Text (Optional)")]
        [Tooltip("カスタム案内テキスト（空欄の場合はアクション種別に応じたデフォルトを表示）")]
        [SerializeField] private string customInteractionText = "";

        // 直前の実行時刻（ローカル連打防止）
        private float lastInteractTime = -999f;

        private void Start()
        {
            UpdateInteractionText();
        }

        public void SetTableManager(TableManager tm) => tableManager = tm;
        public void SetActionType(TableActionType type) => actionType = type;

        /// <summary>
        /// ホバー時のHUDツールチップ案内テキストを更新します。
        /// </summary>
        public void UpdateInteractionText()
        {
            if (!string.IsNullOrEmpty(customInteractionText))
            {
                this.InteractionText = customInteractionText;
                return;
            }

            switch (actionType)
            {
                case TableActionType.ResetGame:
                    this.InteractionText = requireMasterOnly ? "ゲームをリセット (Master専用)" : "ゲームをリセット (Reset)";
                    break;
                case TableActionType.DealCardsToAll:
                    this.InteractionText = requireMasterOnly ? $"全員に配る [{dealCardsCount}枚] (Master専用)" : $"全員に配る [{dealCardsCount}枚] (Deal)";
                    break;
                case TableActionType.AdvanceTurn:
                    this.InteractionText = "手番を次へ進める (Pass)";
                    break;
                default:
                    this.InteractionText = "操作 (Interact)";
                    break;
            }
        }

        /// <summary>
        /// 3D直接インタラクト（デスクトップ: 左クリック / Eキー, VR: トリガー / 直接タップ）
        /// </summary>
        public override void Interact()
        {
            // 1. クールダウン判定（連打防止）
            if (Time.time - lastInteractTime < cooldownSeconds)
            {
                return;
            }

            // 2. マスター権限チェック（ホスト専用トグルが有効な場合）
            if (requireMasterOnly && !Networking.IsMaster)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableActionTrigger] この操作はインスタンスのマスター（ホスト）のみ実行可能です。");
                return;
            }

            // 3. TableManagerの存在チェック
            if (tableManager == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableActionTrigger] tableManager が設定されていません。");
                return;
            }

            lastInteractTime = Time.time;

            // 4. アクション実行
            ExecuteAction();
        }

        private void ExecuteAction()
        {
            switch (actionType)
            {
                case TableActionType.ResetGame:
                    tableManager.ResetGame();
                    break;

                case TableActionType.DealCardsToAll:
                    tableManager.DealCardsToAll(dealCardsCount);
                    break;

                case TableActionType.AdvanceTurn:
                    tableManager.AdvanceTurn();
                    break;

                default:
                    Debug.LogWarning($"[VRC-BoardGameKit] [TableActionTrigger] 未定義のアクション種別です: {actionType}");
                    break;
            }
        }
    }
}
