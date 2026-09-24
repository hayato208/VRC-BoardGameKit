using UdonSharp;
using UnityEngine;
using BoardGameKit.Plugins;
using BoardGameKit.Core;

namespace BoardGameKit.Examples
{
    /// <summary>
    /// BOOTH配布用サンプルルールプラグイン。
    /// 手番管理（現在のターン座席のみ行動可能）および2枚出し・即時手番交代ルールを実装します。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SamplePairOnlyPlugin : RulePluginBase
    {
        [SerializeField] private TableManager tableManager;

        private void Start()
        {
            this.ruleName = "Turn-based Pair Mode";

            if (tableManager == null)
            {
                tableManager = GetComponentInParent<TableManager>();
            }
        }

        /// <summary>
        /// 現在のターン座席に着席しているプレイヤーのみ行動を許可します。
        /// </summary>
        public override bool CanPlayerAct(int seatIndex, int playerId)
        {
            if (tableManager == null) return true;

            if (seatIndex == -1)
            {
                Debug.Log("[SamplePairOnlyPlugin] 座席に着席していないためプレイできません。");
                return false;
            }

            if (seatIndex == tableManager.GetCurrentTurnSeatIndex())
            {
                return true;
            }

            Debug.Log($"[SamplePairOnlyPlugin] 手番ではありません。（現在手番: 座席{tableManager.GetCurrentTurnSeatIndex()}, あなた: 座席{seatIndex}）");
            return false;
        }

        /// <summary>
        /// 選択されたカードが2枚ちょうどの時のみプレイを許可します。
        /// </summary>
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
        /// 2枚のカードが場に出された直後に手札枯渇による勝利判定を行い、未決着時は次手番へ進めます。
        /// </summary>
        public override void OnCardsPlayed(int playerId, int[] cardIds)
        {
            Debug.Log($"[SamplePairOnlyPlugin] プレイヤー({playerId})が2枚のカードを出しました: ID {cardIds[0]}, {cardIds[1]}");

            if (tableManager == null)
            {
                Debug.LogError("[SamplePairOnlyPlugin] tableManager の参照が未設定（null）です。Inspectorで割り当ててください。");
                return;
            }

            // 1. 操作プレイヤーの残り手札枚数を判定（TableManagerのファサードAPI経由）
            int remainingCards = tableManager.GetPlayerHandCount(playerId);
            Debug.Log($"[SamplePairOnlyPlugin] プレイヤー({playerId})の残り手札枚数: {remainingCards}");

            // 2. 勝利判定: 手札が0枚になった場合はゲーム終了
            if (remainingCards == 0)
            {
                Debug.Log($"[SamplePairOnlyPlugin] 手札が0枚になったため、プレイヤー({playerId})の勝利です。");
                tableManager.EndGame(playerId);
                return;
            }

            // 3. 未決着時は次の手番へ進める
            Debug.Log("[SamplePairOnlyPlugin] tableManager.AdvanceTurn() を呼び出します。");
            tableManager.AdvanceTurn();
        }

        /// <summary>
        /// TableManager.ResetGame() 実行時に呼び出される初期化フックです。
        /// ルール固有の内部状態（仮想手札数や各種フラグ）を初期値へ戻します。
        /// </summary>
        public override void OnGameReset()
        {
            Debug.Log("[SamplePairOnlyPlugin] ゲームリセット通知を受信しました。ルール状態を初期化します。");
            // 必要に応じてルール固有の内部変数があれば初期化
        }
    }
}