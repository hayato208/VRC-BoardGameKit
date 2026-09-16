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
        /// 勝利条件判定フック。
        /// </summary>
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
