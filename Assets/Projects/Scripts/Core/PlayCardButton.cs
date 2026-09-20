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
        public TableManager tableManager;

        [Tooltip("紐付くプレイヤーの手札エリア")]
        public PersonalHandArea linkedHandArea;

        [Tooltip("紐付く座席インデックス（0〜3）")]
        public int seatIndex = -1;

        private void Start()
        {
            this.InteractionText = "カードを出す (Play)";

            // 参照が外れていた場合の親階層フォールバック
            if (linkedHandArea == null)
            {
                linkedHandArea = GetComponentInParent<PersonalHandArea>();
            }
        }

        /// <summary>
        /// 3D直接インタラクト（Eキー / VRタッチ）
        /// </summary>
        public override void Interact()
        {
            ExecutePlay();
        }

        /// <summary>
        /// Unity UI (Button.onClick) / VRCUiShape レーザークリック用エントリーポイント
        /// </summary>
        public void OnButtonClick()
        {
            ExecutePlay();
        }

        private void ExecutePlay()
        {
            if (linkedHandArea == null)
            {
                linkedHandArea = GetComponentInParent<PersonalHandArea>();
            }

            if (tableManager == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [PlayCardButton] tableManager が設定されていません。");
                return;
            }

            // 手札エリアを明示して一括プレイを要求
            tableManager.PlaySelectedCards(linkedHandArea);
        }
    }
}
