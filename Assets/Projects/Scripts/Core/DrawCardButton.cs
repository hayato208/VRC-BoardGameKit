using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// プレイヤーの手元（PersonalHandArea）に配置されるドロー専用UIボタン。
    /// WorldSpace Canvas + VRCUiShape + BoxCollider + UdonSharpBehaviour (Interact / OnButtonClick) のハイブリッド構成により、
    /// VRコントローラーのレーザーポインター、デスクトップUIマウスクリック、3D直接Interactの全環境で100%確実に動作する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DrawCardButton : UdonSharpBehaviour
    {
        [Tooltip("山札マネージャーへの参照")]
        public DeckManager deckManager;

        [Tooltip("紐付くプレイヤーの手札エリア")]
        public PersonalHandArea linkedHandArea;

        private void Start()
        {
            this.InteractionText = "カードを引く (Draw)";

            // 参照がインスペクタから外れていた場合の防護フォールバック
            if (linkedHandArea == null)
            {
                linkedHandArea = GetComponentInParent<PersonalHandArea>();
            }
            if (deckManager == null)
            {
                deckManager = Object.FindObjectOfType<DeckManager>();
            }
        }

        /// <summary>
        /// 3D直接インタラクト（Eキー / VRタッチ）
        /// </summary>
        public override void Interact()
        {
            ExecuteDraw();
        }

        /// <summary>
        /// Unity UI (Button.onClick) / VRCUiShape レーザークリック用エントリーポイント
        /// </summary>
        public void OnButtonClick()
        {
            ExecuteDraw();
        }

        private void ExecuteDraw()
        {
            if (deckManager == null)
            {
                deckManager = Object.FindObjectOfType<DeckManager>();
            }
            if (linkedHandArea == null)
            {
                linkedHandArea = GetComponentInParent<PersonalHandArea>();
            }

            if (deckManager == null || linkedHandArea == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] deckManager または linkedHandArea が取得できません。");
                return;
            }

            // 1. 手札エリアから最も若い空きスロットを取得
            CardSnapZone emptySlot = linkedHandArea.GetFirstEmptySlot();

            if (emptySlot != null)
            {
                // 2. 山札から空きスロットへカードを引く
                deckManager.DrawCardForZone(emptySlot);
                Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> [DrawCardButton] 手元ボタンからドローを実行しました (対象スロット: {emptySlot.gameObject.name})</color>");
            }
            else
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] 手札スロットが満杯のためドローできません。");
            }
        }
    }
}
