using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using BoardGameKit.Plugins;

namespace BoardGameKit.Core
{
    /// <summary>
    /// カードのプレイ方式
    /// </summary>
    public enum CardPlayMode
    {
        Immediate,    // 1クリックで即座に場へプレイ（BOOTH配布版）
        MultiSelect   // 複数選択して手元ボタンでプレイ（開発・オリジナルルール版）
    }

    /// <summary>
    /// テーブル全体の進行・ネットワーク同期および座席・山札・手札の統括マネージャー。
    /// 2層アーキテクチャに基づき、同期変数を本クラスに集約し、固有ルールはRulePluginBaseに委譲する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TableManager : UdonSharpBehaviour
    {
        [Header("Play Settings")]
        [Tooltip("カードのプレイ方式（Immediate: 1クリックで即座に場へ / MultiSelect: 複数選択して手元ボタンで場へ）")]
        public CardPlayMode playMode = CardPlayMode.Immediate;

        [Tooltip("中央の場のプレイエリア（スナップ枠）")]
        public CardSnapZone centerPlayZone;

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
        [UdonSynced]
        private int currentTurnSeatIndex = 0;

        // ゲーム進行状態（0: 待機中, 1: 対戦中, 2: 終局）
        [UdonSynced]
        private int gameState = 0;

        // 勝者のプレイヤーID（未決着は -1）
        [UdonSynced]
        private int winnerPlayerId = -1;

        /// <summary>
        /// 全員に初期カードを一括配布する（ディーラーアクション）
        /// </summary>
        public void DealCardsToAll(int cardsPerPlayer)
        {
            if (deckManager == null || seatControllers == null) return;
            if (!TakeOwnership()) return;

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

            gameState = 1;
            RequestSerialization();
        }

        /// <summary>
        /// 指定プレイヤーが山札から手札へ1枚引く
        /// </summary>
        public void DrawCardForPlayer(int seatIndex)
        {
            if (deckManager == null || seatIndex < 0 || seatIndex >= handTrayControllers.Length) return;

            int cardId = deckManager.DrawCard();
            if (cardId == -1) return;

            HandTrayController tray = handTrayControllers[seatIndex];
            if (tray != null)
            {
                int addedSlot = tray.AddCard(cardId);
                if (addedSlot == -1)
                {
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

            if (activeRulePlugin != null)
            {
                if (!activeRulePlugin.CanPlayCard(playerId, cardId, slotIndex))
                {
                    return;
                }
            }

            tray.PlayCard(slotIndex);

            if (deckManager != null)
            {
                deckManager.DiscardCard(cardId);
            }

            if (activeRulePlugin != null)
            {
                activeRulePlugin.OnCardPlayed(playerId, cardId, slotIndex);

                int checkWinner = activeRulePlugin.CheckWinCondition();
                if (checkWinner != -1)
                {
                    winnerPlayerId = checkWinner;
                    gameState = 2;
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

            if (deckManager != null)
            {
                deckManager.ResetAndReshuffleDeck();
            }

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

        public void OnPlayerSeated(int seatIndex, int playerId)
        {
        }

        public void OnPlayerLeftSeat(int seatIndex, int playerId)
        {
        }

        /// <summary>
        /// カードがクリック（Interact）されたときの通知ハンドラ
        /// </summary>
        /// <param name="card">クリックされたカード</param>
        public void OnCardClicked(CardController card)
        {
            if (card == null) return;
            Debug.Log($"[VRC-BoardGameKit] [TableManager] カードがクリックされました: {card.gameObject.name} (ID: {card.cardId}, PlayMode: {playMode})");

            if (playMode == CardPlayMode.Immediate)
            {
                PlayCardImmediate(card);
            }
            else if (playMode == CardPlayMode.MultiSelect)
            {
                // Step 3 (T38) でトグル浮上処理を実装
                Debug.Log($"[VRC-BoardGameKit] [TableManager] MultiSelect モードでカードがクリックされました: {card.gameObject.name}");
            }
        }

        /// <summary>
        /// 即時モード (Immediate Mode): 1クリックで即座に中央プレイエリアへ整列移動
        /// </summary>
        /// <param name="card">プレイするカード</param>
        public void PlayCardImmediate(CardController card)
        {
            if (card == null) return;
            if (centerPlayZone == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableManager] centerPlayZone が未設定のためプレイできません。");
                return;
            }

            // すでに中央プレイエリアにある場合は二重プレイを防止
            if (card.currentZone == centerPlayZone)
            {
                return;
            }

            // 操作プレイヤーにカードの所有権を移行
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && !Networking.IsOwner(card.gameObject))
            {
                Networking.SetOwner(localPlayer, card.gameObject);
            }

            // 元のスロット（手元スロットなど）からカードを解放（手元スロットが空き状態に復帰）
            if (card.currentZone != null)
            {
                card.currentZone.ReleaseCard(card);
                card.currentZone = null;
            }

            // 中央プレイエリアへ配置要請（Tell: allowStack=true により自動スタック整列）
            bool accepted = centerPlayZone.TrySnap(card);
            if (accepted)
            {
                card.currentZone = centerPlayZone;
                Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> [TableManager] カードを中央プレイエリアへ即座に出しました: {card.gameObject.name} (StackCount: {centerPlayZone.GetStackedCount()})</color>");
            }
            else
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [TableManager] 中央プレイエリアへのスナップが拒否されました: {card.gameObject.name}");
            }
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

        /// <summary>
        /// 指定されたプレイヤーが既にいずれかの座席に着席しているかを判定する（二重着席防止用）
        /// </summary>
        /// <param name="playerId">判定対象のプレイヤーID</param>
        /// <returns>既にいずれかの席に着席中ならtrue、未着席ならfalse</returns>
        public bool IsPlayerAlreadySeated(int playerId)
        {
            if (playerId == -1) return false;
            if (seatControllers == null) return false;

            for (int i = 0; i < seatControllers.Length; i++)
            {
                SeatController seat = seatControllers[i];
                if (seat != null && seat.GetSeatedPlayerId() == playerId)
                {
                    return true;
                }
            }
            return false;
        }

        // --- ゲッター ---
        public int GetCurrentTurnSeatIndex() => currentTurnSeatIndex;
        public int GetGameState() => gameState;
        public int GetWinnerPlayerId() => winnerPlayerId;
    }
}
