using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// プレイヤーの手元（PersonalHandArea）に配置されるカードプレイ専用UIボタン。
    /// 選択中のカードをクリック順に中央プレイエリア（centerPlayZone）へ一括でプレイする。
    /// WorldSpace Canvas + VRCUiShape + BoxCollider + UdonSharpBehaviour (Interact / OnButtonClick) のハイブリッド構成により、
    /// VRコントローラーのレーザーポインター、デスクトップUIマウスクリック、3D直接Interactの全環境で100%確実に動作する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PlayCardButton : UdonSharpBehaviour
    {
        [Tooltip("テーブル統括マネージャーへの参照")]
        [SerializeField] private TableManager tableManager;

        public void SetTableManager(TableManager tm) => tableManager = tm;

        private void Start()
        {
            this.InteractionText = "カードを出す (Play)";
        }

        /// <summary>
        /// 3D直接インタラクト（Eキー / VRタッチ）
        /// </summary>
        public override void Interact()
        {
            ExecutePlay();
        }

        private void ExecutePlay()
        {
            if (tableManager == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [PlayCardButton] tableManager が設定されていません。");
                return;
            }

            // 選択中のカードを一括プレイ
            tableManager.PlaySelectedCards();
        }
    }
}
