using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// カードが吸着（スナップ）するスロット領域を定義するコンポーネント。
    /// 手札トレイのスロットや、テーブルの場のマス目にアタッチされる。
    /// isTrigger = true の BoxCollider を持ち、近づいたカードを磁石のように定位置へ吸着させる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CardSnapZone : UdonSharpBehaviour
    {
        [Header("Slot Information")]
        [Tooltip("スロットの名前または識別番号")]
        public string slotName = "SnapSlot_0";

        [Tooltip("このスロットに現在カードが収まっているかどうか")]
        public bool isOccupied = false;

        [Tooltip("現在このスロットに嵌まっているカード")]
        public GameObject currentCard = null;

        [Header("Visual Feedback (Optional)")]
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

        /// <summary>
        /// ガイド枠のハイライト表示切り替え
        /// </summary>
        public void SetGuideHighlighted(bool highlighted)
        {
            if (guideMaterialInstance != null)
            {
                guideMaterialInstance.color = highlighted ? highlightGuideColor : defaultGuideColor;
            }
        }

        /// <summary>
        /// カードがこのスロットにスナップ（収容）されたときの通知
        /// </summary>
        public void OnCardSnapped(GameObject card)
        {
            isOccupied = true;
            currentCard = card;
            SetGuideHighlighted(false);

            // 対策1: カード吸着時はガイド枠を非表示にし、Zファイティング（荒ぶり・チラつき）を完全防止
            if (guideRenderer != null)
            {
                guideRenderer.enabled = false;
            }

            Debug.Log($"[VRC-BoardGameKit] [SnapZone] カードがスロットに吸着しました（ガイド枠非表示）: {slotName}");
        }

        /// <summary>
        /// カードがこのスロットから持ち上げられた（離脱）ときの通知
        /// </summary>
        public void OnCardRemoved()
        {
            isOccupied = false;
            currentCard = null;

            // 対策1: カード離脱時はガイド枠を再表示
            if (guideRenderer != null)
            {
                guideRenderer.enabled = true;
            }

            SetGuideHighlighted(false);
            Debug.Log($"[VRC-BoardGameKit] [SnapZone] カードがスロットから離れました（ガイド枠再表示）: {slotName}");
        }
    }
}
