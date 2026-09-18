using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// カードが吸着（スナップ）するスロット領域を定義するコンポーネント。
    /// 手札トレイのスロットや、テーブルの場のマス目にアタッチされる。
    /// Tell, Don't Ask原則に基づき、カードのTransformを外部から直接変更せず、
    /// 受入判定を行った上でカードに SnapTo() 命令を発行する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CardSnapZone : UdonSharpBehaviour
    {
        [Header("Slot Information")]
        [Tooltip("スロットの名前または識別番号")]
        public string slotName = "SnapSlot_0";

        [Tooltip("このスロットに現在カードが収まっているかどうか")]
        [SerializeField] private bool isOccupied = false;

        [Tooltip("現在このスロットに収まっているカード")]
        [SerializeField] private CardController currentCard = null;

        [Header("Visual Feedback")]
        [Tooltip("カードが近づいたときにハイライト表示する枠（MeshRenderer）")]
        public MeshRenderer guideRenderer;

        [Tooltip("通常時のガイド色")]
        public Color defaultGuideColor = new Color(1f, 1f, 1f, 0.15f);

        [Tooltip("カード接近時のガイド色（緑色に発光）")]
        public Color highlightGuideColor = new Color(0.2f, 1f, 0.4f, 0.5f);

        private Material guideMaterialInstance;

        private void Start()
        {
            if (guideRenderer != null)
            {
                guideMaterialInstance = guideRenderer.material;
                guideRenderer.enabled = !isOccupied;
                SetGuideHighlighted(false);
            }
        }

        #region Tell, Don't Ask 外部公開API

        /// <summary>
        /// 現在このスロットが占有されているかを返す
        /// </summary>
        public bool IsOccupied()
        {
            return isOccupied;
        }

        /// <summary>
        /// 現在このスロットに収まっているカードを返す
        /// </summary>
        public CardController GetCurrentCard()
        {
            return currentCard;
        }

        /// <summary>
        /// カードからの配置要請を受け入れ、判定する命令（Tell）。
        /// 空いていればカード自身に目標姿勢への移動を命じる。
        /// </summary>
        /// <param name="card">配置を希望するカード</param>
        /// <returns>受入成功ならtrue、満杯等で失敗ならfalse</returns>
        public bool TrySnap(CardController card)
        {
            if (card == null) return false;
            if (isOccupied) return false;

            // スロット状態の更新
            isOccupied = true;
            currentCard = card;

            // ガイド枠のハイライト解除と非表示（Zファイティング完全防止）
            SetGuideHighlighted(false);
            if (guideRenderer != null)
            {
                guideRenderer.enabled = false;
            }

            // 【Tell】カード自身に目標位置・回転への移動・整列を命じる
            card.SnapTo(transform.position, transform.rotation);

            Debug.Log($"[VRC-BoardGameKit] [CardSnapZone] カードを受入・スナップ命令を発行しました: {slotName} (Card: {card.gameObject.name})");
            return true;
        }

        /// <summary>
        /// 収まっていたカードが持ち上げられた（解放）ときの通知命令（Tell）。
        /// </summary>
        /// <param name="card">解放するカード</param>
        public void ReleaseCard(CardController card)
        {
            if (card == null) return;
            if (currentCard != card) return;

            isOccupied = false;
            currentCard = null;

            // ガイド枠を再表示
            if (guideRenderer != null)
            {
                guideRenderer.enabled = true;
            }

            SetGuideHighlighted(false);
            Debug.Log($"[VRC-BoardGameKit] [CardSnapZone] カードがスロットから解放されました（ガイド再表示）: {slotName}");
        }

        /// <summary>
        /// ガイド枠のハイライト色を切り替える
        /// </summary>
        public void SetGuideHighlighted(bool highlighted)
        {
            if (guideMaterialInstance != null)
            {
                guideMaterialInstance.color = highlighted ? highlightGuideColor : defaultGuideColor;
            }
        }

        #endregion
    }
}
