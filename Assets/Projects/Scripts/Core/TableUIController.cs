using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 卓上および手元のUIボタン操作（ドロー、プレイ、配布、リセット等）を
    /// TableManagerおよびDeckManagerに橋渡しするUIコントローラー。
    /// 操作結果や山札状態をTextMeshProステータス表示に出力する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class TableUIController : UdonSharpBehaviour
    {
        [Header("Manager References")]
        [Tooltip("全体進行を司るTableManagerへの参照")]
        [SerializeField] private TableManager tableManager;

        [Tooltip("山札マネージャーへの参照")]
        [SerializeField] private DeckManager deckManager;

        [Tooltip("座席コントローラー一覧")]
        [SerializeField] private SeatController[] seatControllers;

        [Header("UI Feedback")]
        [Tooltip("各座席の手元UIに配置されたステータステキスト")]
        [SerializeField] private TextMeshProUGUI[] seatStatusTexts;

        [Header("Deal Settings")]
        [Tooltip("Dealボタン押下時に1人あたりに配る初期枚数")]
        [SerializeField] private int initialDealCount = 5;

        private void Start()
        {
            UpdateStatusDisplay("準備完了 - 席をクリックして参加 (Ready - Click Seat)");
        }

        /// <summary>
        /// 「カードを引く (Draw)」ボタン押下時
        /// ローカルプレイヤーが座っている席のトレイにカードを1枚引く
        /// </summary>
        public void OnClickDrawButton()
        {
            if (tableManager == null) return;

            int mySeat = GetLocalPlayerSeatIndex();
            if (mySeat != -1)
            {
                Debug.Log($"<color=#00FF00>[VRC-BoardGameKit] [UI] ドローを実行します (Seat: {mySeat})</color>");
                tableManager.DrawCardForPlayer(mySeat);
                UpdateStatusDisplay($"Seat_{mySeat} がカードを1枚引きました (Drew Card)");
            }
            else
            {
                Debug.LogWarning("[VRC-BoardGameKit] [UI] 座席に着席していないためドローできません。");
                UpdateStatusDisplay("⚠️ イスをクリックして着席してください (Please Sit)");
            }
        }

        /// <summary>
        /// 「カードを一括配布 (Deal)」ボタン押下時（親/ディーラー用）
        /// </summary>
        public void OnClickDealButton()
        {
            if (tableManager == null) return;

            Debug.Log($"<color=#00FF00>[VRC-BoardGameKit] [UI] 全員に初期カード（{initialDealCount}枚）を配布します</color>");
            tableManager.DealCardsToAll(initialDealCount);
            UpdateStatusDisplay($"全員に {initialDealCount} 枚ずつ配布しました (Dealt {initialDealCount} Cards)");
        }

        /// <summary>
        /// 「山札シャッフル (Shuffle)」ボタン押下時
        /// </summary>
        public void OnClickShuffleButton()
        {
            if (deckManager == null) return;

            Debug.Log("<color=#00FF00>[VRC-BoardGameKit] [UI] 山札シャッフルを実行します</color>");
            deckManager.ShuffleDeck();
            UpdateStatusDisplay("山札をシャッフルしました (Deck Shuffled)");
        }

        /// <summary>
        /// 「ターン終了 (Pass / Next Turn)」ボタン押下時
        /// </summary>
        public void OnClickAdvanceTurnButton()
        {
            if (tableManager == null) return;

            Debug.Log("<color=#00FF00>[VRC-BoardGameKit] [UI] 次の手番へ進めます</color>");
            tableManager.AdvanceTurn();
            UpdateStatusDisplay($"手番が Seat_{tableManager.GetCurrentTurnSeatIndex()} へ移動しました");
        }

        /// <summary>
        /// 手元UI「席を離れる (Leave)」ボタン押下時
        /// </summary>
        public void OnClickLeaveSeatButton()
        {
            int mySeat = GetLocalPlayerSeatIndex();
            if (mySeat != -1 && seatControllers != null && mySeat < seatControllers.Length)
            {
                SeatController seat = seatControllers[mySeat];
                if (seat != null)
                {
                    Debug.Log($"<color=#FFFF00>[VRC-BoardGameKit] [UI] 手元ボタンから離席を実行します (Seat_{mySeat})</color>");
                    seat.OnClickLeaveButton();
                    UpdateStatusDisplay($"Seat_{mySeat + 1} から離席しました (Left Seat)");
                }
            }
        }

        /// <summary>
        /// 「ゲーム全リセット (Reset)」ボタン押下時
        /// </summary>
        public void OnClickResetButton()
        {
            if (tableManager == null) return;

            Debug.Log("<color=#00FF00>[VRC-BoardGameKit] [UI] ゲームリセットを実行します</color>");
            tableManager.ResetGame();
            UpdateStatusDisplay("ゲームをリセットしました (Game Reset)");
        }

        /// <summary>
        /// 卓上ステータス表示の更新（各座席の手元UIへ一括反映）
        /// </summary>
        public void UpdateStatusDisplay(string message)
        {
            int remaining = (deckManager != null) ? deckManager.GetRemainingCount() : 0;
            int discard = (deckManager != null) ? deckManager.GetDiscardCount() : 0;
            string formattedText = $"[山札: {remaining} / すて札: {discard}]\n{message}";

            // 各座席の手元UIステータステキストの更新
            if (seatStatusTexts != null)
            {
                for (int i = 0; i < seatStatusTexts.Length; i++)
                {
                    if (seatStatusTexts[i] != null)
                    {
                        seatStatusTexts[i].text = formattedText;
                    }
                }
            }
        }

        /// <summary>
        /// ローカルプレイヤーが現在着席している座席番号を取得（未着席は -1）
        /// </summary>
        private int GetLocalPlayerSeatIndex()
        {
            if (seatControllers == null) return -1;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return -1;

            int localId = localPlayer.playerId;
            for (int i = 0; i < seatControllers.Length; i++)
            {
                if (seatControllers[i] != null && seatControllers[i].GetSeatedPlayerId() == localId)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
