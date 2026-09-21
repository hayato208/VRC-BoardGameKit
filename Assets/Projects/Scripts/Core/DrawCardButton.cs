using TMPro;
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
        [SerializeField] private DeckManager deckManager;

        [Tooltip("紐付くプレイヤーの手札エリア")]
        [SerializeField] private PersonalHandArea linkedHandArea;

        [Tooltip("ボタン表面のラベル表示用TextMeshPro")]
        [SerializeField] private TextMeshPro buttonText;

        public void SetDeckManager(DeckManager dm) => deckManager = dm;
        public void SetLinkedHandArea(PersonalHandArea area) => linkedHandArea = area;
        public void SetButtonText(TextMeshPro text) => buttonText = text;

        private void Start()
        {
            if (deckManager != null)
            {
                UpdateRemainingCount(deckManager.GetRemainingCount());
            }
            else
            {
                this.InteractionText = "カードを引く (Draw)";
            }
        }

        /// <summary>
        /// 山札の残数に応じてボタン表面ラベルおよびホバーツールチップを動的に更新する (案A)
        /// </summary>
        public void UpdateRemainingCount(int remainingCount)
        {
            if (remainingCount > 0)
            {
                this.InteractionText = $"カードを引く (残り: {remainingCount}枚)";
                if (buttonText != null)
                {
                    buttonText.text = $"カードを引く\n<size=70%>(残り: {remainingCount}枚)</size>";
                }
            }
            else
            {
                this.InteractionText = "山札なし (0枚)";
                if (buttonText != null)
                {
                    buttonText.text = "山札切れ\n<size=70%>(0枚)</size>";
                }
            }
        }

        /// <summary>
        /// 3D直接インタラクト（Eキー / VRタッチ）
        /// </summary>
        public override void Interact()
        {
            ExecuteDraw();
        }

        private void ExecuteDraw()
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
                // 2. 山札から空きスロットへカードを引く (戻り値で成否を検証)
                int drawnCardId = deckManager.DrawCardForZone(emptySlot);
                if (drawnCardId != -1)
                {
                    Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> [DrawCardButton] 手元ボタンからドロー成功: Card ID {drawnCardId} -> {emptySlot.gameObject.name}</color>");
                }
                else
                {
                    Debug.LogWarning($"[VRC-BoardGameKit] [DrawCardButton] ドロー拒否: スロット {emptySlot.gameObject.name} への配置に失敗しました（既に占有中または山札切れ）。");
                }
            }
            else
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] 手札スロットが満杯のためドローできません。");
            }
        }
    }
}
