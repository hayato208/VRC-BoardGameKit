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
        [SerializeField] private CardPlayMode playMode = CardPlayMode.Immediate;

        [Tooltip("中央の場のプレイエリア（スナップ枠）")]
        [SerializeField] private CardSnapZone centerPlayZone;

        public CardPlayMode PlayMode => playMode;
        public void SetPlayMode(CardPlayMode mode) => playMode = mode;

        public CardSnapZone CenterPlayZone => centerPlayZone;
        public void SetCenterPlayZone(CardSnapZone zone) => centerPlayZone = zone;

        [Header("Core Subsystem References")]
        [Tooltip("山札マネージャーへの参照")]
        [SerializeField] private DeckManager deckManager;

        [Tooltip("座席コントローラー一覧（2〜8席）")]
        [SerializeField] private SeatController[] seatControllers;

        [Header("Rule Plugin (Optional)")]
        [Tooltip("オリジナルゲームルール拡張プラグイン（未設定時は汎用サンドボックスとして動作）")]
        [SerializeField] private RulePluginBase activeRulePlugin;

        // --- 複数選択モード管理（ローカル実行時状態） ---
        private const int MAX_TRACKED_CARDS = 64;
        private bool[] isCardSelected = new bool[MAX_TRACKED_CARDS];
        private int[] selectedOrder = new int[MAX_TRACKED_CARDS]; // クリック順序（FIFO）キュー
        private int selectedCount = 0; // 現在選択中の枚数

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
                        DrawCardForPlayer(s);
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
            if (deckManager == null || seatControllers == null || seatIndex < 0 || seatIndex >= seatControllers.Length) return;

            SeatController seat = seatControllers[seatIndex];
            if (seat == null) return;

            PersonalHandArea handArea = seat.GetLinkedHandArea();
            if (handArea == null) return;

            CardSnapZone emptySlot = handArea.GetFirstEmptySlot();
            if (emptySlot != null)
            {
                deckManager.DrawCardForZone(emptySlot);
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

            if (seatControllers != null)
            {
                for (int i = 0; i < seatControllers.Length; i++)
                {
                    SeatController seat = seatControllers[i];
                    if (seat != null)
                    {
                        PersonalHandArea handArea = seat.GetLinkedHandArea();
                        if (handArea != null)
                        {
                            handArea.ClearAllSlots();
                        }
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

            ClearAllSelections();

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
            Debug.Log($"[VRC-BoardGameKit] [TableManager] カードがクリックされました: {card.gameObject.name} (ID: {card.CardId}, PlayMode: {playMode})");

            CardSnapZone zone = card.GetCurrentZone();

            // 共通の場（centerPlayZone）にあるカードは操作不可のため即座にreturn
            if (centerPlayZone != null && zone == centerPlayZone)
            {
                Debug.Log($"<color=#FFAA00>[VRC-BoardGameKit] [TableManager] 中央プレイエリア（場）にあるカードは操作できません: {card.gameObject.name}</color>");
                return;
            }

            // 手札スロットにあるカードなら各モードに応じた手札挙動を実行
            if (playMode == CardPlayMode.Immediate)
            {
                PlayCardImmediate(card);
            }
            else if (playMode == CardPlayMode.MultiSelect)
            {
                ToggleCardSelection(card);
            }
        }

        /// <summary>
        /// 1枚のカードを中央プレイエリアへ整列配置する共通実処理
        /// </summary>
        /// <param name="card">プレイするカード</param>
        /// <returns>スナップ配置が成功したかどうか</returns>
        private bool PlaySingleSelectedCard(CardController card)
        {
            if (card == null || centerPlayZone == null) return false;

            // 元のスロット（手元スロットなど）を記憶
            CardSnapZone currentZone = card.GetCurrentZone();

            // すでに中央の場に出ているカードは二重プレイ防止のためスキップ
            if (currentZone != null && currentZone == centerPlayZone)
            {
                return false;
            }

            // 選択追跡フラグを解除
            if (card.CardId >= 0 && card.CardId < isCardSelected.Length)
            {
                isCardSelected[card.CardId] = false;
            }

            // 操作プレイヤーにカードの所有権を移行
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && !Networking.IsOwner(card.gameObject))
            {
                Networking.SetOwner(localPlayer, card.gameObject);
            }

            // 中央プレイエリアへ配置要請（Tell: allowStack=true により自動スタック整列＆SnapToZone実行）
            // ※TrySnap内で card.SnapToZone() が走り、カードの基準位置・回転・所属ゾーンが場の座標に確定更新される
            bool accepted = centerPlayZone.TrySnap(card);
            if (accepted)
            {
                // スナップ成功時のみ、元のスロットを安全に解放
                if (currentZone != null && currentZone != centerPlayZone)
                {
                    currentZone.ReleaseCard(card);
                }
                return true;
            }
            else
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [TableManager] 中央プレイエリアへのスナップが拒否されました: {card.gameObject.name}");
                return false;
            }
        }

        /// <summary>
        /// 即時モード (Immediate Mode): 1クリックで即座に中央プレイエリアへ整列移動
        /// </summary>
        /// <param name="card">プレイするカード</param>
        private void PlayCardImmediate(CardController card)
        {
            if (card == null) return;
            if (centerPlayZone == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableManager] centerPlayZone が未設定のためプレイできません。");
                return;
            }

            bool success = PlaySingleSelectedCard(card);
            if (success)
            {
                Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> [TableManager] カードを中央プレイエリアへ即座に出しました: {card.gameObject.name} (StackCount: {centerPlayZone.GetStackedCount()})</color>");
            }
        }

        #region MultiSelect 複数選択管理 (Step 3: T38 & Step 4: T39)

        /// <summary>
        /// カードの選択状態を反転（トグル）し、選択順序キューを更新する (MultiSelectモード)
        /// </summary>
        /// <param name="card">選択/解除対象のカード</param>
        private void ToggleCardSelection(CardController card)
        {
            if (card == null) return;

            // 操作プレイヤーにカードの所有権を移行（VRCObjectSyncによる強制同期巻き戻しを防止）
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && !Networking.IsOwner(card.gameObject))
            {
                Networking.SetOwner(localPlayer, card.gameObject);
            }

            int id = card.CardId;
            if (id < 0 || id >= isCardSelected.Length)
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [TableManager] カードIDが追跡許容範囲外です: {id} (許容最大: {isCardSelected.Length - 1})");
                return;
            }

            // 選択フラグ反転
            bool nextSelected = !isCardSelected[id];
            isCardSelected[id] = nextSelected;

            if (nextSelected)
            {
                // クリック順序（FIFOキュー）の末尾に追加
                if (selectedCount < selectedOrder.Length)
                {
                    selectedOrder[selectedCount] = id;
                    selectedCount++;
                }
            }
            else
            {
                // 選択解除: キューから該当カードIDを検索し、後続要素を前方へ詰める
                int foundIndex = -1;
                for (int i = 0; i < selectedCount; i++)
                {
                    if (selectedOrder[i] == id)
                    {
                        foundIndex = i;
                        break;
                    }
                }

                if (foundIndex != -1)
                {
                    for (int i = foundIndex; i < selectedCount - 1; i++)
                    {
                        selectedOrder[i] = selectedOrder[i + 1];
                    }
                    selectedCount--;
                }
            }

            // カード自身へ視覚更新を命令 (Tell, Don't Ask)
            card.SetSelectedVisual(nextSelected);

            Debug.Log($"<color=#00FFFF><b>[VRC-BoardGameKit]</b> [TableManager] カード選択状態を切り替えました: {card.gameObject.name} (ID: {id}, Selected: {nextSelected}, 選択総数: {selectedCount})</color>");
        }

        /// <summary>
        /// 選択中のカードをクリックした順番通りに中央プレイエリアへ一括でプレイする (MultiSelectモード)
        /// </summary>
        public void PlaySelectedCards()
        {
            if (centerPlayZone == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableManager] centerPlayZone が未設定のためプレイできません。");
                return;
            }

            if (selectedCount == 0)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableManager] プレイ対象として選択されたカードがありません。");
                return;
            }

            if (deckManager == null || deckManager.CardPool == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableManager] deckManager または CardPool が初期化されていません。");
                return;
            }

            int playedCount = 0;

            // クリックされた順番（selectedOrder: FIFOキュー）に従ってプレイ
            for (int i = 0; i < selectedCount; i++)
            {
                int cardId = selectedOrder[i];
                if (cardId < 0 || cardId >= deckManager.CardPool.Length) continue;

                CardController card = deckManager.CardPool[cardId];
                if (card == null) continue;

                if (PlaySingleSelectedCard(card))
                {
                    playedCount++;
                }
            }

            // キューと選択状態を完全クリア
            ClearAllSelections();

            if (playedCount > 0)
            {
                Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> [TableManager] 選択中のカード {playedCount} 枚をクリック順通りに中央プレイエリアへ一括プレイしました。(StackCount: {centerPlayZone.GetStackedCount()})</color>");
            }
            else
            {
                Debug.LogWarning("[VRC-BoardGameKit] [TableManager] プレイ対象として有効な選択中カードがありませんでした。");
            }
        }

        /// <summary>
        /// 全カードの選択状態を解除し、通常位置へ復帰させる
        /// </summary>
        public void ClearAllSelections()
        {
            for (int i = 0; i < isCardSelected.Length; i++)
            {
                isCardSelected[i] = false;
            }
            selectedCount = 0;

            if (deckManager != null && deckManager.CardPool != null)
            {
                for (int i = 0; i < deckManager.CardPool.Length; i++)
                {
                    CardController card = deckManager.CardPool[i];
                    if (card != null && card.IsSelected())
                    {
                        card.SetSelectedVisual(false);
                    }
                }
            }

            Debug.Log("[VRC-BoardGameKit] [TableManager] 全カードの選択状態および選択順序を解除しました。");
        }

        /// <summary>
        /// 指定されたカードIDが現在選択中（浮上中）かどうかを判定する
        /// </summary>
        /// <param name="cardId">判定対象カードID</param>
        /// <returns>選択中ならtrue</returns>
        public bool IsCardSelected(int cardId)
        {
            if (cardId < 0 || cardId >= isCardSelected.Length) return false;
            return isCardSelected[cardId];
        }

        /// <summary>
        /// 現在選択されているカードの総数を取得する
        /// </summary>
        /// <returns>選択中のカード枚数</returns>
        public int GetSelectedCardCount()
        {
            return selectedCount;
        }

        #endregion

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
