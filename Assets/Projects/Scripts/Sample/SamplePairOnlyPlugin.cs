using UdonSharp;
using UnityEngine;
using BoardGameKit.Plugins;
using BoardGameKit.Core;

namespace BoardGameKit.Examples
{
    /// <summary>
    /// BOOTH配布用サンプルルールプラグイン。
    /// 手番管理（現在のターン座席のみ行動可能）および2枚出しルールを定義します。
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

            // 未着席の場合は行動不可
            if (seatIndex == -1)
            {
                Debug.Log("[SamplePairOnlyPlugin] 座席に着席していないためプレイできません。");
                return false;
            }

            // 現在の手番座席と一致するか確認
            if (seatIndex == tableManager.GetCurrentTurnSeatIndex())
            {
                return true;
            }

            Debug.Log($"[SamplePairOnlyPlugin] 手番ではありません。（現在手番: 座席{tableManager.GetCurrentTurnSeatIndex()}, あなた: 座席{seatIndex}）");
            return false;
        }

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

        public override void OnCardsPlayed(int playerId, int[] cardIds)
        {
            Debug.Log($"[SamplePairOnlyPlugin] プレイヤー({playerId})が2枚のカードを出しました: ID {cardIds[0]}, {cardIds[1]}");
        }
    }
}