using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// プレイヤーの手元（PersonalHandArea）に配置されるドロー専用UIボタン。
    /// WorldSpace Canvas + VRCUiShape + BoxCollider + UdonSharpBehaviour (Interact) のハイブリッド構成により、
    /// VRコントローラーおよびデスクトップの双方で100%確実に山札から空き手札スロットへカードを引く。
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
        }

        public override void Interact()
        {
            if (deckManager == null || linkedHandArea == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] deckManager または linkedHandArea が設定されていません。");
                return;
            }

            // 1. 手札エリアから最も若い空きスロットを取得
            CardSnapZone emptySlot = linkedHandArea.GetFirstEmptySlot();

            if (emptySlot != null)
            {
                // 2. 山札から空きスロットへカードを引く
                deckManager.DrawCardForZone(emptySlot);
                Debug.Log($"[VRC-BoardGameKit] [DrawCardButton] 手元UIボタンからドローを実行 (Slot: {emptySlot.gameObject.name})");
            }
            else
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] 手札スロットが満杯のためドローできません。");
            }
        }
    }
}
