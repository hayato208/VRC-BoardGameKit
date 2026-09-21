# BoardGameKit 開発実行計画・Phase 1 実装マスター仕様書 (Antigravity用)

本ドキュメントは、VRカードゲームツールキット「BoardGameKit」における複数枚プレイ対応・ルールプラグイン拡張機能の開発実行計画、進捗状況、完全な実装コード、およびシーン構成規約を定義したマスターリファレンスである。

---

## 1. 全体実行計画（ロードマップ）

本プロジェクトでは、コア基盤（物理移動、同期、手札・山札管理）を破壊しない疎結合な2層アーキテクチャを維持しつつ、段階的に機能を拡張する。

| フェーズ        | 開発項目                        | 主な実装内容                                                                                                                                                                                                                                   | 状態      |
|:----------- |:--------------------------- |:---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |:------- |
| **Phase 1** | **複数枚バリデーション基盤と検証用サンプルの導入** | ・`RulePluginBase` への複数枚判定・通知用仮想メソッド追加<br>・`TableManager.PlaySelectedCards` へのバリデーションとイベント通知の組み込み<br>・動的参照フォールバック（`Start()`）の実装<br>・XMLドキュメントコメント（`/// <summary>`）および既存記法（`{ return ...; }`）への統一<br>・動作検証用サンプル `SamplePairOnlyPlugin` の導入 | **完了**  |
| **Phase 2** | **ゲームステート管理および座席連携の本格統合**   | ・`SeatController` の着席／離席イベントと `TableManager.OnPlayerSeated / OnPlayerLeftSeat` の完全連動<br>・手番（ターン）進行制御とルールプラグイン側 `OnTurnStart / OnTurnEnd` の実挙動バインド<br>・勝敗判定（`CheckWinCondition`）の定期ポーリングまたはイベント駆動トリガーの整備                                  | **未着手** |
| **Phase 3** | **オリジナルカードゲーム実装とパッケージング**   | ・具体的なゲームルールプラグインの実装（大富豪、ブラックジャック等の実作）<br>・アセンブリ定義（`.asmdef`）およびフォルダ構成のクリーンアップ<br>・BOOTH配布用 `.unitypackage` のビルドと導入ドキュメントの整備                                                                                                              | **未着手** |

---

## 2. 現在の進捗状況とアクティブなコンテキスト

### 2.1 完了した作業

1. **コーディング規約の整合**:
   - 式形式メンバー（`=>`）や空中括弧を排除し、プロジェクト既存の記法（中括弧改行＋`return true;`）に統一。
   - 脱落していた `OnPlayerSeated` および `OnPlayerLeftSeat` を復元し、全メソッドにXMLドキュメントコメントを整備。
2. **コンパイル環境の安定化**:
   - UdonSharpアセンブリ関連エラー（`does not belong to a U# assembly`、アップグレードエラー）の切り分けと解消。
3. **実行時参照の解決（フォールバック導入）**:
   - `TableManager` の `activeRulePlugin` が実行時に `null` になる問題に対し、`Start()` で同一階層および子階層から `GetComponentInChildren<RulePluginBase>()` で動的取得するフォールバック処理を実装。
4. **検証プラグインの疎結合化**:
   - ルール判定スクリプトをコア管理外の独立オブジェクト（`DynamicCardField_4Players/SampleRule`）に配置する運用を確定。

### 2.2 現在の到達点

- **Phase 1の設計・コード実装・環境設定が完了**。
- Unity Editor（PlayMode）にて、1枚出しが拒絶され、2枚出しが正常に通過・配置される動作検証を行う段階に到達。



---

## 1. アーキテクチャおよびシーン構成規約

コア機能（同期、物理、手札、山札）とゲーム固有ルールを疎結合に保つため、ルール判定コンポーネントは独立したGameObjectに配置し、`TableManager` から参照をバインドする設計を採用する。

### 1.1 階層構造

```textDynamicCardField_4Players
DynamicCardField_4Players
 ├── TableManager (TableManager.cs)
 └── SampleRule (SamplePairOnlyPlugin.cs などの RulePluginBase 派生クラス)
```

### 1.2 インスペクター設定および参照解決

- **`TableManager`**:
  
  - `activeRulePlugin` フィールドに `DynamicCardField_4Players/SampleRule` のコンポーネントをアタッチする。
  
  - フィールドが未設定（`None`）の場合は純粋なサンドボックスとして動作し、全カード操作を無条件で許可する。
  
  - 実行時の参照外れ（シリアライズ不整合）を防ぐため、`TableManager.Start()` にて同一階層および子階層からの動的フォールバック検索を実行する。

## 2. 実装ソースコード（全量）

### 2.1 `RulePluginBase.cs`

- **配置パス**: `Assets/BoardGameKit/Plugins/RulePluginBase.cs`

- **責務**: ルール拡張用ベースクラス。複数枚判定および通知イベントを定義。

C#

```
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace BoardGameKit.Plugins
{
    /// <summary>
    /// オリジナルカードゲームのルール拡張用ベースクラス。
    /// TableManagerから各イベントフックが呼び出される。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RulePluginBase : UdonSharpBehaviour
    {
        [Header("Plugin Info")]
        public string ruleName = "Standard Sandbox";

        /// <summary>
        /// プレイヤーが単一カードを出せるか判定します。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardId">対象カードのID</param>
        /// <param name="targetSlot">配置先スロット番号</param>
        /// <returns>プレイを許可する場合はtrue</returns>
        public virtual bool CanPlayCard(int playerId, int cardId, int targetSlot)
        {
            return true;
        }

        /// <summary>
        /// 複数枚カードの一括プレイ判定を行います。
        /// ルールプラグイン側でオーバーライドしない場合は常に全操作を許可します。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardIds">選択されたカードID一覧</param>
        /// <returns>プレイを許可する場合はtrue、拒絶する場合はfalse</returns>
        public virtual bool CanPlayCards(int playerId, int[] cardIds)
        {
            return true;
        }

        /// <summary>
        /// 単一カードが場へ出された直後の処理を行います。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardId">配置されたカードID</param>
        /// <param name="targetSlot">配置先スロット番号</param>
        public virtual void OnCardPlayed(int playerId, int cardId, int targetSlot)
        {
        }

        /// <summary>
        /// 複数枚カードが場へ確定配置された直後に呼び出される通知イベントです。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardIds">配置されたカードID一覧</param>
        public virtual void OnCardsPlayed(int playerId, int[] cardIds)
        {
        }

        /// <summary>
        /// ターン開始時に呼び出されます。
        /// </summary>
        /// <param name="activePlayerId">手番プレイヤーのID</param>
        public virtual void OnTurnStart(int activePlayerId)
        {
        }

        /// <summary>
        /// ターン終了時に呼び出されます。
        /// </summary>
        /// <param name="activePlayerId">手番を終えたプレイヤーのID</param>
        public virtual void OnTurnEnd(int activePlayerId)
        {
        }

        /// <summary>
        /// 勝利判定を行います。
        /// </summary>
        /// <returns>勝者のplayerId（未決着時は -1）</returns>
        public virtual int CheckWinCondition()
        {
            return -1;
        }

        /// <summary>
        /// ゲームリセット時に呼び出されます。
        /// </summary>
        public virtual void OnGameReset()
        {
        }
    }
}
```

### 2.2 `TableManager.cs`

- **配置パス**: `Assets/BoardGameKit/Core/TableManager.cs`

- **責務**: 卓全体の状態同期、カード移動、手番制御、およびルール判定・通知の統括。

C#

```
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using BoardGameKit.Plugins;

namespace BoardGameKit.Core
{
    public enum CardPlayMode
    {
        Immediate,    // 1クリックで即座に場へプレイ（BOOTH配布版）
        MultiSelect   // 複数選択して手元ボタンで場へ（開発・オリジナルルール版）
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TableManager : UdonSharpBehaviour
    {
        [Header("Play Settings")]
        [SerializeField] private CardPlayMode playMode = CardPlayMode.Immediate;
        [SerializeField] private CardSnapZone centerPlayZone;

        public CardPlayMode PlayMode => playMode;
        public void SetPlayMode(CardPlayMode mode) => playMode = mode;
        public CardSnapZone CenterPlayZone => centerPlayZone;
        public void SetCenterPlayZone(CardSnapZone zone) => centerPlayZone = zone;

        [Header("Core Subsystem References")]
        [SerializeField] private DeckManager deckManager;
        [SerializeField] private SeatController[] seatControllers;

        [Header("Rule Plugin (Optional)")]
        [SerializeField] private RulePluginBase activeRulePlugin;

        // 複数選択モード管理
        private const int MAX_TRACKED_CARDS = 64;
        private bool[] isCardSelected = new bool[MAX_TRACKED_CARDS];
        private int[] selectedOrder = new int[MAX_TRACKED_CARDS]; // クリック順序（FIFO）
        private int selectedCount = 0;

        // 同期変数
        [UdonSynced] private int currentTurnSeatIndex = 0;
        [UdonSynced] private int gameState = 0; // 0: 待機中, 1: 対戦中, 2: 終局
        [UdonSynced] private int winnerPlayerId = -1;

        private void Start()
        {
            // Inspectorで未割り当ての場合、同一オブジェクトまたは子階層から動的取得
            if (activeRulePlugin == null)
            {
                activeRulePlugin = GetComponent<RulePluginBase>();
                if (activeRulePlugin == null)
                {
                    activeRulePlugin = GetComponentInChildren<RulePluginBase>();
                }

                if (activeRulePlugin != null)
                {
                    Debug.Log($"[TableManager] activeRulePlugin を自動検出・割り当てしました: {activeRulePlugin.name}");
                }
            }
        }

        /// <summary>
        /// 全着席プレイヤーに均等にカードを配ります。
        /// </summary>
        /// <param name="cardsPerPlayer">各プレイヤーへ配る枚数</param>
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
        /// 指定された座席インデックスのプレイヤーに山札から1枚ドローさせます。
        /// </summary>
        /// <param name="seatIndex">対象座席インデックス</param>
        public void DrawCardForPlayer(int seatIndex)
        {
            if (deckManager == null || seatControllers == null || seatIndex < 0 || seatIndex >= seatControllers.Length) return;
            SeatController seat = seatControllers[seatIndex];
            if (seat == null) return;

            PersonalHandArea handArea = seat.GetLinkedHandArea();
            if (handArea != null)
            {
                handArea.TryDrawCard(deckManager);
            }
        }

        /// <summary>
        /// 手番を次の着席プレイヤーへ進めます。
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
        /// 卓全体の状態、手札、山札を初期化します。
        /// </summary>
        public void ResetGame()
        {
            if (!TakeOwnership()) return;

            if (deckManager != null) deckManager.ResetAndReshuffleDeck();

            if (seatControllers != null)
            {
                for (int i = 0; i < seatControllers.Length; i++)
                {
                    SeatController seat = seatControllers[i];
                    if (seat != null)
                    {
                        PersonalHandArea handArea = seat.GetLinkedHandArea();
                        if (handArea != null) handArea.ClearAllSlots();
                    }
                }
            }

            gameState = 0;
            winnerPlayerId = -1;
            currentTurnSeatIndex = 0;

            if (activeRulePlugin != null) activeRulePlugin.OnGameReset();

            ClearAllSelections();
            RequestSerialization();
        }

        /// <summary>
        /// カードクリック時の入力エントリーポイントです。
        /// </summary>
        /// <param name="card">クリックされたカード</param>
        public void OnCardClicked(CardController card)
        {
            if (card == null) return;
            CardSnapZone zone = card.GetCurrentZone();

            // 場のカードはクリック操作不可
            if (centerPlayZone != null && zone == centerPlayZone) return;

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
        /// 指定された単一カードを中央プレイゾーンへスナップ配置します。
        /// </summary>
        /// <param name="card">配置対象カード</param>
        /// <returns>配置に成功した場合はtrue</returns>
        private bool PlaySingleSelectedCard(CardController card)
        {
            if (card == null || centerPlayZone == null) return false;

            CardSnapZone currentZone = card.GetCurrentZone();
            if (currentZone != null && currentZone == centerPlayZone) return false;

            if (card.CardId >= 0 && card.CardId < isCardSelected.Length)
            {
                isCardSelected[card.CardId] = false;
            }

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && !Networking.IsOwner(card.gameObject))
            {
                Networking.SetOwner(localPlayer, card.gameObject);
            }

            bool accepted = centerPlayZone.TrySnap(card);
            if (accepted)
            {
                if (currentZone != null && currentZone != centerPlayZone)
                {
                    currentZone.ReleaseCard(card);
                }
                return true;
            }
            return false;
        }

        /// <summary>
        /// 即時モードにおける単一カード配置処理。バリデーション判定および通知を含みます。
        /// </summary>
        /// <param name="card">対象カード</param>
        private void PlayCardImmediate(CardController card)
        {
            if (card == null || centerPlayZone == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            int localPlayerId = localPlayer != null ? localPlayer.playerId : -1;

            if (activeRulePlugin != null)
            {
                int[] singleCardArr = new int[] { card.CardId };
                if (!activeRulePlugin.CanPlayCards(localPlayerId, singleCardArr))
                {
                    return;
                }
            }

            if (PlaySingleSelectedCard(card))
            {
                if (activeRulePlugin != null)
                {
                    activeRulePlugin.OnCardPlayed(localPlayerId, card.CardId, 0);
                    activeRulePlugin.OnCardsPlayed(localPlayerId, new int[] { card.CardId });
                }
            }
        }

        /// <summary>
        /// カードの選択・選択解除をトグルし、順序配列を更新します。
        /// </summary>
        /// <param name="card">対象カード</param>
        private void ToggleCardSelection(CardController card)
        {
            if (card == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null && !Networking.IsOwner(card.gameObject))
            {
                Networking.SetOwner(localPlayer, card.gameObject);
            }

            int id = card.CardId;
            if (id < 0 || id >= isCardSelected.Length) return;

            bool nextSelected = !isCardSelected[id];
            isCardSelected[id] = nextSelected;

            if (nextSelected)
            {
                if (selectedCount < selectedOrder.Length)
                {
                    selectedOrder[selectedCount] = id;
                    selectedCount++;
                }
            }
            else
            {
                int foundIndex = -1;
                for (int i = 0; i < selectedCount; i++)
                {
                    if (selectedOrder[i] == id) { foundIndex = i; break; }
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

            card.SetSelectedVisual(nextSelected);
        }

        /// <summary>
        /// 選択中のカード群を中央プレイエリアへ一括整列配置します。
        /// ルールプラグインがアタッチされている場合はバリデーション判定を行い、違反時は選択を解除します。
        /// </summary>
        public void PlaySelectedCards()
        {
            if (centerPlayZone == null || selectedCount == 0) return;
            if (deckManager == null || deckManager.CardPool == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            int localPlayerId = localPlayer != null ? localPlayer.playerId : -1;

            int[] submittedCardIds = new int[selectedCount];
            for (int i = 0; i < selectedCount; i++)
            {
                submittedCardIds[i] = selectedOrder[i];
            }

            if (activeRulePlugin != null)
            {
                if (!activeRulePlugin.CanPlayCards(localPlayerId, submittedCardIds))
                {
                    ClearAllSelections();
                    return;
                }
            }

            int playedCount = 0;
            for (int i = 0; i < submittedCardIds.Length; i++)
            {
                int cardId = submittedCardIds[i];
                if (cardId < 0 || cardId >= deckManager.CardPool.Length) continue;

                CardController card = deckManager.CardPool[cardId];
                if (card == null) continue;

                if (PlaySingleSelectedCard(card))
                {
                    playedCount++;
                }
            }

            if (playedCount > 0 && activeRulePlugin != null)
            {
                activeRulePlugin.OnCardsPlayed(localPlayerId, submittedCardIds);
            }

            ClearAllSelections();
        }

        /// <summary>
        /// 全選択状態を初期化し、カードの視覚表現を通常に戻します。
        /// </summary>
        public void ClearAllSelections()
        {
            for (int i = 0; i < isCardSelected.Length; i++) isCardSelected[i] = false;
            selectedCount = 0;

            if (deckManager != null && deckManager.CardPool != null)
            {
                for (int i = 0; i < deckManager.CardPool.Length; i++)
                {
                    CardController card = deckManager.CardPool[i];
                    if (card != null && card.IsSelected()) card.SetSelectedVisual(false);
                }
            }
        }

        public bool IsCardSelected(int cardId)
        {
            return (cardId >= 0 && cardId < isCardSelected.Length) ? isCardSelected[cardId] : false;
        }

        public int GetSelectedCardCount()
        {
            return selectedCount;
        }

        private bool TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                VRCPlayerApi localPlayer = Networking.LocalPlayer;
                if (localPlayer != null) Networking.SetOwner(localPlayer, gameObject);
            }
            return Networking.IsOwner(gameObject);
        }

        public bool IsPlayerAlreadySeated(int playerId)
        {
            if (playerId == -1 || seatControllers == null) return false;
            for (int i = 0; i < seatControllers.Length; i++)
            {
                if (seatControllers[i] != null && seatControllers[i].GetSeatedPlayerId() == playerId) return true;
            }
            return false;
        }

        public int GetCurrentTurnSeatIndex()
        {
            return currentTurnSeatIndex;
        }

        public int GetGameState()
        {
            return gameState;
        }

        public int GetWinnerPlayerId()
        {
            return winnerPlayerId;
        }

        /// <summary>
        /// プレイヤーが座席に着席した際にSeatControllerから呼び出されるイベントハンドラーです。
        /// </summary>
        /// <param name="seatIndex">着席した座席のインデックス</param>
        /// <param name="playerId">着席したプレイヤーのID</param>
        public void OnPlayerSeated(int seatIndex, int playerId)
        {
        }

        /// <summary>
        /// プレイヤーが座席から離席した際にSeatControllerから呼び出されるイベントハンドラーです。
        /// </summary>
        /// <param name="seatIndex">離席した座席のインデックス</param>
        /// <param name="playerId">離席したプレイヤーのID</param>
        public void OnPlayerLeftSeat(int seatIndex, int playerId)
        {
        }
    }
}
```

### 2.3 `SamplePairOnlyPlugin.cs`

- **配置パス**: `Assets/BoardGameKit/Plugins/SamplePairOnlyPlugin.cs`

- **アタッチ対象**: `DynamicCardField_4Players/SampleRule`

- **責務**: カードが2枚ちょうど選択されている場合のみプレイを許可する検証用ルール。

C#

```
using UdonSharp;
using UnityEngine;
using BoardGameKit.Plugins;

namespace BoardGameKit.Examples
{
    /// <summary>
    /// BOOTH配布用サンプルルールプラグイン。
    /// 常にカードを2枚選択して出すルールを定義する最小実装例です。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SamplePairOnlyPlugin : RulePluginBase
    {
        private void Start()
        {
            this.ruleName = "Pair Only Mode";
        }

        /// <summary>
        /// 選択されたカードが2枚ちょうどの時のみプレイを許可します。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardIds">選択されたカードID一覧</param>
        /// <returns>2枚選択されている場合はtrue、それ以外はfalse</returns>
        public override bool CanPlayCards(int playerId, int[] cardIds)
        {
            if (cardIds == null)
            {
                return false;
            }

            if (cardIds.Length == 2)
            {
                return true;
            }

            Debug.Log("[SamplePairOnlyPlugin] カードは2枚同時に選択して出す必要があります。");
            return false;
        }

        /// <summary>
        /// 2枚のカードが場に出された際にログを出力します。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardIds">配置されたカードID一覧</param>
        public override void OnCardsPlayed(int playerId, int[] cardIds)
        {
            Debug.Log($"[SamplePairOnlyPlugin] プレイヤー({playerId})が2枚のカードを出しました: ID {cardIds[0]}, {cardIds[1]}");
        }
    }
}
```

## 3. Phase 1 差分仕様（Unified Diff 形式）

### 3.1 `RulePluginBase.cs`

Diff

```
--- a/Assets/BoardGameKit/Plugins/RulePluginBase.cs
+++ b/Assets/BoardGameKit/Plugins/RulePluginBase.cs
@@ -19,6 +19,25 @@ namespace BoardGameKit.Plugins
             return true;
         }

+        /// <summary>
+        /// 複数枚カードの一括プレイ判定を行います。
+        /// ルールプラグイン側でオーバーライドしない場合は常に全操作を許可します。
+        /// </summary>
+        /// <param name="playerId">操作を行ったプレイヤーのID</param>
+        /// <param name="cardIds">選択されたカードID一覧</param>
+        /// <returns>プレイを許可する場合はtrue、拒絶する場合はfalse</returns>
+        public virtual bool CanPlayCards(int playerId, int[] cardIds)
+        {
+            return true;
+        }
+
         // カードが出された直後の処理
         public virtual void OnCardPlayed(int playerId, int cardId, int targetSlot)
         {
         }
+
+        /// <summary>
+        /// 複数枚カードが場へ確定配置された直後に呼び出される通知イベントです。
+        /// </summary>
+        /// <param name="playerId">操作を行ったプレイヤーのID</param>
+        /// <param name="cardIds">配置されたカードID一覧</param>
+        public virtual void OnCardsPlayed(int playerId, int[] cardIds)
+        {
+        }
```

### 3.2 `TableManager.cs`

Diff

```
--- a/Assets/BoardGameKit/Core/TableManager.cs
+++ b/Assets/BoardGameKit/Core/TableManager.cs
@@ -35,6 +35,24 @@ namespace BoardGameKit.Core
         [UdonSynced] private int gameState = 0; // 0: 待機中, 1: 対戦中, 2: 終局
         [UdonSynced] private int winnerPlayerId = -1;

+        private void Start()
+        {
+            // Inspectorで未割り当ての場合、同一オブジェクトまたは子階層から動的取得
+            if (activeRulePlugin == null)
+            {
+                activeRulePlugin = GetComponent<RulePluginBase>();
+                if (activeRulePlugin == null)
+                {
+                    activeRulePlugin = GetComponentInChildren<RulePluginBase>();
+                }
+
+                if (activeRulePlugin != null)
+                {
+                    Debug.Log($"[TableManager] activeRulePlugin を自動検出・割り当てしました: {activeRulePlugin.name}");
+                }
+            }
+        }
+
         public void DealCardsToAll(int cardsPerPlayer)
         {
@@ -190,6 +208,13 @@ namespace BoardGameKit.Core
             VRCPlayerApi localPlayer = Networking.LocalPlayer;
             int localPlayerId = localPlayer != null ? localPlayer.playerId : -1;

+            if (activeRulePlugin != null)
+            {
+                int[] singleCardArr = new int[] { card.CardId };
+                if (!activeRulePlugin.CanPlayCards(localPlayerId, singleCardArr))
+                {
+                    return;
+                }
+            }
+
             if (PlaySingleSelectedCard(card))
             {
@@ -250,6 +275,15 @@ namespace BoardGameKit.Core
                 submittedCardIds[i] = selectedOrder[i];
             }

+            if (activeRulePlugin != null)
+            {
+                if (!activeRulePlugin.CanPlayCards(localPlayerId, submittedCardIds))
+                {
+                    ClearAllSelections();
+                    return;
+                }
+            }
+
             int playedCount = 0;
             for (int i = 0; i < submittedCardIds.Length; i++)
             {
@@ -262,6 +296,11 @@ namespace BoardGameKit.Core
                 }
             }

+            if (playedCount > 0 && activeRulePlugin != null)
+            {
+                activeRulePlugin.OnCardsPlayed(localPlayerId, submittedCardIds);
+            }
+
             ClearAllSelections();
         }
@@ -325,5 +364,23 @@ namespace BoardGameKit.Core
         public int GetWinnerPlayerId()
         {
             return winnerPlayerId;
         }
+
+        /// <summary>
+        /// プレイヤーが座席に着席した際にSeatControllerから呼び出されるイベントハンドラーです。
+        /// </summary>
+        /// <param name="seatIndex">着席した座席のインデックス</param>
+        /// <param name="playerId">着席したプレイヤーのID</param>
+        public void OnPlayerSeated(int seatIndex, int playerId)
+        {
+        }
+
+        /// <summary>
+        /// プレイヤーが座席から離席した際にSeatControllerから呼び出されるイベントハンドラーです。
+        /// </summary>
+        /// <param name="seatIndex">離席した座席のインデックス</param>
+        /// <param name="playerId">離席したプレイヤーのID</param>
+        public void OnPlayerLeftSeat(int seatIndex, int playerId)
+        {
+        }
     }
 }
```

## 4. 動作検証チェックリスト

1. **Unity EditorのPlayModeを開始する。**

2. **座席に着席し、山札からカードを3枚引く。**

3. **1枚出しの検証**:
   
   - カードを1枚選択して「PLAY」ボタンを押す。
   
   - カードが場に出ず手元に戻り、コンソールに `カードは2枚同時に選択して出す必要があります。` と出力されること。

4. **2枚出しの検証**:
   
   - カードを2枚選択して「PLAY」ボタンを押す。
   
   - 中央場へ2枚整列配置され、コンソールに `プレイヤー(...)が2枚のカードを出しました` と出力されること。

5. **3枚出しの検証**:
   
   - カードを3枚選択して「PLAY」ボタンを押す。
   
   - 1枚出し同様に弾かれ、選択が解除されること。

6. **サンドボックス切り替えの検証**:
   
   - `TableManager` の `activeRulePlugin` を `None` に設定した場合、1枚出し・複数枚出しが無制限に実行できること。