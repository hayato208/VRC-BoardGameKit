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
    [UdonBehaviourSyncMode(UdonSyncMode.None)]
    public class RulePluginBase : UdonSharpBehaviour
    {
        [Header("Plugin Info")]
        [Tooltip("ルールの識別名")]
        public string ruleName = "Standard Sandbox";

        /// <summary>
        /// プレイヤーが指定カードをプレイ（場に出す）可能か判定するガード関数。
        /// </summary>
        /// <param name="playerId">操作プレイヤーID</param>
        /// <param name="cardId">プレイ対象のカードID</param>
        /// <param name="targetSlot">手札スロット番号</param>
        /// <returns>プレイ可能なら true、不可なら false</returns>
        public virtual bool CanPlayCard(int playerId, int cardId, int targetSlot)
        {
            // デフォルト（サンドボックス）は常にプレイ可能
            return true;
        }

        /// <summary>
        /// カードがプレイされた直後に呼ばれるイベントフック（特殊効果、コスト消費など）。
        /// </summary>
        public virtual void OnCardPlayed(int playerId, int cardId, int targetSlot)
        {
            // 【★】派生プラグインまたはLocal LLM生成コードで固有ロジックを記述
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
        /// 勝利条件判定フック。
        /// </summary>
        /// <returns>勝者のplayerId（未決着の場合は -1）</returns>
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
    }
}
