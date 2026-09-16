using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 卓上および手元のUIボタン操作（ドロー、プレイ、配布、リセット等）を
    /// TableManagerおよびDeckManagerに橋渡しするUIコントローラー。
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

        [Header("Deal Settings")]
        [Tooltip("Dealボタン押下時に1人あたりに配る初期枚数")]
        [SerializeField] private int initialDealCount = 5; // 【★】ゲームに応じて調整可能

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
                tableManager.DrawCardForPlayer(mySeat);
            }
        }

        /// <summary>
        /// 「カードを一括配布 (Deal)」ボタン押下時（親/ディーラー用）
        /// </summary>
        public void OnClickDealButton()
        {
            if (tableManager == null) return;
            tableManager.DealCardsToAll(initialDealCount);
        }

        /// <summary>
        /// 「山札シャッフル (Shuffle)」ボタン押下時
        /// </summary>
        public void OnClickShuffleButton()
        {
            if (deckManager == null) return;
            deckManager.ShuffleDeck();
        }

        /// <summary>
        /// 「ターン終了 (Pass / Next Turn)」ボタン押下時
        /// </summary>
        public void OnClickAdvanceTurnButton()
        {
            if (tableManager == null) return;
            tableManager.AdvanceTurn();
        }

        /// <summary>
        /// 「ゲーム全リセット (Reset)」ボタン押下時
        /// </summary>
        public void OnClickResetButton()
        {
            if (tableManager == null) return;
            tableManager.ResetGame();
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
