# BoardGameKit 開発引き継ぎ仕様書（Phase 2: 進行制御・UI・トリガー連携）

## 1. 概要・設計原則
- **環境**: Unity (VRChat World / UdonSharp), Manual Sync
- **アーキテクチャ方針**:
  - `TableManager.cs` を中核とする2層アーキテクチャ（状態同期変数を集約し、ファサードAPIを提供）。
  - 個別ゲームルールは `RulePluginBase` 派生のプラグイン（例: `SamplePairOnlyPlugin.cs`）に委譲し、コア層を汚染しない疎結合を維持。
  - UIや物理トリガーは独立コンポーネントとし、`TableManager` を直接肥大化させない。

---

## 2. 本セッションで完了・確認した実装

### ① 手札集計・ファサードAPIの整備
- **`PersonalHandArea.cs`**:
  - `GetHeldCardCount()` を追加。スロット（`snapZones`）を走査して現在保持している手札枚数を正確に返却。
- **`TableManager.cs`**:
  - `GetSeatController(int seatIndex)`: 指定座席のコントローラーを取得。
  - `GetSeatHandCount(int seatIndex)`: 指定座席の手札枚数を集計。
  - `GetPlayerHandCount(int playerId)`: プレイヤーIDから座席を逆引きし、残り手札枚数を1行で取得可能にカプセル化。

### ② 手番管理および手札枯渇による終局判定
- **`SamplePairOnlyPlugin.cs`**:
  - 自手番（`CanPlayerAct`）かつ2枚選択時（`CanPlayCards`）のみカード出しを許可。
  - `OnCardsPlayed` 時に `tableManager.GetPlayerHandCount(playerId)` を参照。
  - 手札が 0 枚になった場合、`tableManager.EndGame(playerId)` を呼び出して `gameState = 2`（終局）へ遷移。未決着時は `tableManager.AdvanceTurn()` を実行。

### ③ 終局ステートにおける操作ロック
- **`TableManager.cs`**:
  - `PlaySelectedCards()`: `gameState == 2` 時はプレイ不可。
  - `DrawCardForPlayer(int seatIndex)`: `gameState == 2` 時はドロー不可。
  - `DrawCardForLocalPlayer()`: UI/外部呼び出し用エントリーポイント。`gameState == 2` をガードした上でドローを実行。
  - `DealCardsToAll(int cardsPerPlayer)`: `gameState == 0`（待機中）のみ配布可能に制限。
  ※手番外ドローについては、トランプゲームの汎用サンドボックス性を考慮し、現仕様ではあえて制限しない方針で合意。

### ④ 3D物理ボタントリガーの実装
- **`TableActionTrigger.cs`**:
  - uGUIではなくColliderの `Interact()` による実行コンポーネント。
  - `TableActionType`（`ResetGame`, `DealCardsToAll`, `AdvanceTurn`）をEnumで切り替え可能。
  - クールダウン処理（連打防止）およびマスター専用ガード（`requireMasterOnly`）を実装。
  - **検証状況**: `ResetGame` による卓初期化（`gameState = 0` 復帰、山札・手札・選択解除）の動作を確認完了。

---

## 3. 次に実施する作業内容（Antigravityへの依頼事項）

### タスク: 卓上ステータス表示コンポーネント（`TableStatusDisplay.cs`）の新設

#### 目的
手番や勝敗結果をConsoleログだけでなく、卓上の3Dテキスト（TextMeshPro）に可視化し、ローカルおよびリモート（ネットワーク同期）でリアルタイムに確認できるようにする。

#### 実装要件
1. **新規スクリプト**: `TableStatusDisplay.cs`
   - `TableManager` と `TextMeshProUGUI`（または `TextMeshPro`）を参照。
   - `TableManager` の状態に応じたテキスト整形・表示更新:
     - `gameState == 0`: 「待機中 (Ready)」
     - `gameState == 1`: 「手番: 座席 {currentTurnSeatIndex}」
     - `gameState == 2`: 「ゲーム終了: 勝者 プレイヤー {winnerPlayerId}」
2. **同期・更新タイミング**:
   - `TableManager.cs` に状態変更通知のイベント/メソッド呼び出しを追加するか、`TableManager.OnDeserialization()` に合わせて表示が自動更新される設計とする。
3. **成果物形式**:
   - スクリプト作成コード
   - `TableManager.cs` 側の最小限の差分コード（メソッド単位・差分形式）
   - Unity Hierarchy/Inspector での配置・コンポーネント割り当て手順