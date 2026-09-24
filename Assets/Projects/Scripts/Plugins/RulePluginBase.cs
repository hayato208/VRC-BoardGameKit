using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Plugins
{
    /// <summary>
    /// オリジナルカードゲームのルール拡張用ベースクラス。
    /// 【委譲モデル】TableManagerから各イベントフックが呼び出される。
    /// Local LLMを用いてルールを追加する際は、本クラスを参考にロジックを実装する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RulePluginBase : UdonSharpBehaviour
    {
        [Header("Plugin Info")]
        [Tooltip("ルールの識別名")]
        public string ruleName = "Standard Sandbox";

        /// <summary>
        /// プレイヤーが指定カードをプレイ（場に出す）可能か判定するガード関数。
        /// </summary>
        public virtual bool CanPlayCard(int playerId, int cardId, int targetSlot)
        {
            return true;
        }

        /// <summary>
        /// カードがプレイされた直後に呼ばれるイベントフック（特殊効果、コスト消費など）。
        /// </summary>
        public virtual void OnCardPlayed(int playerId, int cardId, int targetSlot)
        {
        }

        /// <summary>
        /// ターンが開始した際のイベントフック。
        /// </summary>
        public virtual void OnTurnStart(int activePlayerId)
        {
        }

        /// <summary>
        /// ターンが終了した際のイベントフック。
        /// </summary>
        public virtual void OnTurnEnd(int activePlayerId)
        {
        }

        /// <summary>
        /// 勝利条件の検証を行います。
        /// ルールプラグイン側で任意のタイミング（カード配置後、ターン経過後、ショーダウン時等）に呼び出して判定します。
        /// </summary>
        /// <returns>勝者のplayerId（未決着時は -1）</returns>
        public virtual int CheckWinCondition()
        {
            return -1;
        }

        /// <summary>
        /// ゲーム初期化・リセット時のリセット処理フック。
        /// </summary>
        public virtual void OnGameReset()
        {
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
        /// 複数枚カードが場へ確定配置された直後に呼び出される通知イベントです。
        /// </summary>
        /// <param name="playerId">操作を行ったプレイヤーのID</param>
        /// <param name="cardIds">配置されたカードID一覧</param>
        public virtual void OnCardsPlayed(int playerId, int[] cardIds)
        {
        }

        /// <summary>
        /// 指定されたプレイヤー（または座席）が現在アクションを実行可能か判定します。
        /// サンドボックス時は手番制限を行わないため、デフォルトで常にtrueを返します。
        /// </summary>
        /// <param name="seatIndex">操作プレイヤーの座席インデックス</param>
        /// <param name="playerId">操作プレイヤーのID</param>
        /// <returns>行動可能な場合はtrue、手番外等で拒絶する場合はfalse</returns>
        public virtual bool CanPlayerAct(int seatIndex, int playerId)
        {
            return true;
        }
    }
}
