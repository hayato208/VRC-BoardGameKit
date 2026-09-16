using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using BoardGameKit.Plugins;

namespace BoardGameKit.Core
{
    /// <summary>
    /// テーブル全体の進行・ネットワーク同期および座席・山札・手札の統括マネージャー。
    /// 2層アーキテクチャに基づき、同期変数を本クラスに集約し、固有ルールはRulePluginBaseに委譲する。
    /// </summary>
    [UdonBehaviourSyncMode(UdonSyncMode.Manual)]
    public class TableManager : UdonSharpBehaviour
    {
        [Header("Core Subsystem References")]
        [Tooltip("山札マネージャーへの参照")]
        [SerializeField] private DeckManager deckManager;

        [Tooltip("座席コントローラー一覧（2〜8席）")]
        [SerializeField] private SeatController[] seatControllers;

        [Tooltip("手札トレイコントローラー一覧")]
        [SerializeField] private HandTrayController[] handTrayControllers;

        [Header("Rule Plugin (Optional)")]
        [Tooltip("オリジナルゲームルール拡張プラグイン（未設定時は汎用サンドボックスとして動作）")]
        [SerializeField] private RulePluginBase activeRulePlugin;

        // --- 同期変数 ---
        // 現在の手番（座席番号 0〜N-1）
        [UdonSynced(UdonSyncMode.Manual)]
        private int currentTurnSeatIndex = 0;

        // ゲーム進行状態（0: 待機中, 1: 対戦中, 2: 終局）
        [UdonSynced(UdonSyncMode.Manual)]
        private int gameState = 0;

        // 勝者のプレイヤーID（未決着は -1）
        [UdonSynced(UdonSyncMode.Manual)]
        private int winnerPlayerId = -1;

        /// <summary>
        /// 全員に初期カードを一括配布する（ディーラーアクション）
        /// </summary>
        /// <param name="cardsPerPlayer">1人あたりの配布枚数</param>
        public void DealCardsToAll(int cardsPerPlayer)
        {
            if (deckManager == null || seatControllers == null) return;
            if (!TakeOwnership()) return;

            // 各座席を着席確認しながらカードを配る
            for (int round = 0; round < cardsPerPlayer; round++)
            {
                for (int s = 0; s < seatControllers.Length; s++)
                {
                    SeatController seat = seatControllers[s];
                    if (seat != null && seat.IsOccupied())
                    {
                        int cardId = deckManager.DrawCard();
                        if (cardId != -1 && s < handTrayControllers.Length)
                        {
                            HandTrayController tray = handTrayControllers[s];
                            if (tray != null)
                            {
                                tray.AddCard(cardId);
                            }
                        }
                    }
                }
            }

            gameState = 1; // プレイ中
            RequestSerialization();
        }

        /// <summary>
        /// 指定プレイヤーが山札から手札へ1枚引く
        /// </summary>
        public void DrawCardForPlayer(int seatIndex)
        {
            if (deckManager == null || seatIndex < 0 || seatIndex >= handTrayControllers.Length) return;

            // 山札からカードを取得
            int cardId = deckManager.DrawCard();
            if (cardId == -1) return; // 山札切れ

            HandTrayController tray = handTrayControllers[seatIndex];
            if (tray != null)
            {
                int addedSlot = tray.AddCard(cardId);
                if (addedSlot == -1)
                {
                    // 手札が満杯の場合は山札・捨て札に戻す防護処理
                    deckManager.DiscardCard(cardId);
                }
            }
        }

        /// <summary>
        /// プレイヤーが手札からカードを場に出す（プレイ）
        /// </summary>
        public void PlayCard(int seatIndex, int slotIndex)
        {
            if (seatIndex < 0 || seatIndex >= handTrayControllers.Length) return;

            HandTrayController tray = handTrayControllers[seatIndex];
            if (tray == null) return;

            int cardId = tray.GetCardIdAt(slotIndex);
            if (cardId == -1) return;

            int playerId = (seatIndex < seatControllers.Length && seatControllers[seatIndex] != null)
                ? seatControllers[seatIndex].GetSeatedPlayerId()
                : -1;

            // ルールプラグインによる可否判定（委譲）
            if (activeRulePlugin != null)
            {
                if (!activeRulePlugin.CanPlayCard(playerId, cardId, slotIndex))
                {
                    // ルール違反のためプレイ却下
                    return;
                }
            }

            // カードを手札から消費
            tray.PlayCard(slotIndex);

            // 捨て札に送る（または場に出す）
            if (deckManager != null)
            {
                deckManager.DiscardCard(cardId);
            }

            // ルールプラグインの事後フック呼び出し
            if (activeRulePlugin != null)
            {
                activeRulePlugin.OnCardPlayed(playerId, cardId, slotIndex);

                // 勝利判定のチェック
                int checkWinner = activeRulePlugin.CheckWinCondition();
                if (checkWinner != -1)
                {
                    winnerPlayerId = checkWinner;
                    gameState = 2; // 終局
                    if (TakeOwnership())
                    {
                        RequestSerialization();
                    }
                }
            }
        }

        /// <summary>
        /// 次の手番プレイヤー（次の着席席）へターンを回す
        /// </summary>
        public void AdvanceTurn()
        {
            if (seatControllers == null || seatControllers.Length == 0) return;
            if (!TakeOwnership()) return;

            int startIndex = currentTurnSeatIndex;
            for (int i = 1; i <= seatControllers.Length; i++)
            {
                int nextIndex = (startIndex + i) % seatControllers.Length;
                if (seatControllers[nextIndex] != null && seatControllers[nextIndex].IsOccupied())
                {
                    currentTurnSeatIndex = nextIndex;
                    break;
                }
            }

            RequestSerialization();

            // ルールプラグインへターン開始通知
            if (activeRulePlugin != null)
            {
                int activePlayerId = seatControllers[currentTurnSeatIndex].GetSeatedPlayerId();
                activeRulePlugin.OnTurnStart(activePlayerId);
            }
        }

        /// <summary>
        /// ゲームの全リセット
        /// </summary>
        public void ResetGame()
        {
            if (!TakeOwnership()) return;

            // 山札リセット
            if (deckManager != null)
            {
                deckManager.ResetAndReshuffleDeck();
            }

            // 全手札クリア
            if (handTrayControllers != null)
            {
                for (int i = 0; i < handTrayControllers.Length; i++)
                {
                    if (handTrayControllers[i] != null)
                    {
                        handTrayControllers[i].ClearHand();
                    }
                }
            }

            gameState = 0;
            winnerPlayerId = -1;
            currentTurnSeatIndex = 0;

            if (activeRulePlugin != null)
            {
                activeRulePlugin.OnGameReset();
            }

            RequestSerialization();
        }

        /// <summary>
        /// 座席着席時の通知ハンドラ
        /// </summary>
        public void OnPlayerSeated(int seatIndex, int playerId)
        {
            // 着席時の自動処理（必要に応じて追加）
        }

        /// <summary>
        /// 離席時の通知ハンドラ
        /// </summary>
        public void OnPlayerLeftSeat(int seatIndex, int playerId)
        {
            // 離席プレイヤーの手札回収など（必要に応じて追加）
        }

        private bool TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                VRCPlayerApi localPlayer = Networking.LocalPlayer;
                if (localPlayer != null)
                {
                    Networking.SetOwner(localPlayer, gameObject);
                }
            }
            return Networking.IsOwner(gameObject);
        }

        // --- ゲッター ---
        public int GetCurrentTurnSeatIndex() => currentTurnSeatIndex;
        public int GetGameState() => gameState;
        public int GetWinnerPlayerId() => winnerPlayerId;
    }
}
