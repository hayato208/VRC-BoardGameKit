using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// スロットが属するゾーンの種別（抽象モデル）
    /// </summary>
    public enum CardZoneType
    {
        Hand = 0,       // プレイヤー手札スロット
        Field = 1,      // 場のプレイエリア
        Discard = 2,    // 捨て札・墓地
        Deck = 3        // 山札
    }

    /// <summary>
    /// カードが吸着（スナップ）するスロット領域を定義するコンポーネント。
    /// 手札トレイのスロットや、テーブルの場のマス目にアタッチされる。
    /// Tell, Don't Ask原則に基づき、カードのTransformを外部から直接変更せず、
    /// 受入判定を行った上でカードに SnapTo() 命令を発行する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CardSnapZone : UdonSharpBehaviour
    {
        [Header("Zone Identity")]
        [Tooltip("このスナップ枠が属するゾーン種別（手札、場など）")]
        public CardZoneType zoneType = CardZoneType.Hand;

        [Header("Slot Information")]
        [Tooltip("スロットの名前または識別番号")]
        public string slotName = "SnapSlot_0";

        // --- 実行時動的ステート（シリアライズ除外・カプセル化） ---
        private bool isOccupied = false;
        private CardController currentCard = null;

        [Header("Stack Settings")]
        [Tooltip("複数枚のカードを重ねて配置（スタック）することを許可するか（中央プレイエリア等）")]
        public bool allowStack = false;

        [Tooltip("スタック時の1枚あたりの手前浮上オフセット量 (m) ※Zファイティング防止")]
        public float stackElevationOffset = 0.002f;

        // スタックされたカード配列（U#最適化: 最大64枚の固定長）
        private const int MAX_STACK_SIZE = 64;
        private CardController[] stackedCards = new CardController[MAX_STACK_SIZE];
        private int stackedCount = 0;

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
                guideRenderer.enabled = true;
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
        /// 現在このスロットに収まっているカードを返す（スタック時は最前面）
        /// </summary>
        public CardController GetCurrentCard()
        {
            return currentCard;
        }

        /// <summary>
        /// 現在スタックされているカードの総枚数を返す
        /// </summary>
        public int GetStackedCount()
        {
            return stackedCount;
        }

        /// <summary>
        /// スタックされているカード配列を返す
        /// </summary>
        public CardController[] GetStackedCards()
        {
            return stackedCards;
        }

        // --- ゾーン種別判定ゲッター（抽象モデル） ---
        public CardZoneType GetZoneType() => zoneType;
        public bool IsHandZone() => zoneType == CardZoneType.Hand;
        public bool IsFieldZone() => zoneType == CardZoneType.Field;

        /// <summary>
        /// カードからの配置要請を受け入れ、判定する命令（Tell）。
        /// 空いているかスタック許可であれば、カード自身に目標姿勢への移動を命じる。
        /// </summary>
        /// <param name="card">配置を希望するカード</param>
        /// <returns>受入成功ならtrue、満杯等で失敗ならfalse</returns>
        public bool TrySnap(CardController card)
        {
            if (card == null) return false;
            if (isOccupied && !allowStack) return false;

            // スロット状態の更新
            isOccupied = true;
            currentCard = card;

            // ガイド枠のハイライト解除（枠自体は常時表示を維持し、エリア消失を防止）
            SetGuideHighlighted(false);

            // Quadの表面法線方向（手前: -transform.forward）へ 2mm オフセット（Zファイティング完全防止）
            Vector3 targetPos = transform.position - (transform.forward * stackElevationOffset);
            Quaternion targetRot = transform.rotation;

            if (allowStack)
            {
                // スタック時はさらに 2mm ずつ手前に重ねる
                targetPos = transform.position - (transform.forward * ((stackedCount + 1) * stackElevationOffset));
                if (stackedCount < MAX_STACK_SIZE)
                {
                    stackedCards[stackedCount] = card;
                    stackedCount++;
                }
            }

            // 【Tell】カード自身に目標位置・回転への移動・整列および所属記憶を命じる
            card.SnapToZone(this, targetPos, targetRot);

            Debug.Log($"<color=#00FF88>[VRC-BoardGameKit] [CardSnapZone] カードを受入・占有しました: スロット=[{slotName}], 最前面=[{(currentCard != null ? currentCard.gameObject.name : "null")}], スタック総数={stackedCount}</color>");
            return true;
        }

        /// <summary>
        /// 収まっていたカードが持ち上げられた（解放）ときの通知命令（Tell）。
        /// </summary>
        /// <param name="card">解放するカード</param>
        public void ReleaseCard(CardController card)
        {
            if (card == null) return;

            if (allowStack)
            {
                // スタック配列から該当カードを探索・削除
                int foundIndex = -1;
                for (int i = 0; i < stackedCount; i++)
                {
                    if (stackedCards[i] == card)
                    {
                        foundIndex = i;
                        break;
                    }
                }

                if (foundIndex != -1)
                {
                    for (int i = foundIndex; i < stackedCount - 1; i++)
                    {
                        stackedCards[i] = stackedCards[i + 1];
                    }
                    stackedCards[stackedCount - 1] = null;
                    stackedCount--;
                }

                if (stackedCount > 0)
                {
                    currentCard = stackedCards[stackedCount - 1];
                }
                else
                {
                    isOccupied = false;
                    currentCard = null;
                    if (guideRenderer != null)
                    {
                        guideRenderer.enabled = true;
                    }
                }
            }
            else
            {
                isOccupied = false;
                currentCard = null;

                if (guideRenderer != null)
                {
                    guideRenderer.enabled = true;
                }
            }

            // カード側の所属スナップ枠を安全に解除（Tell）
            if (card.GetCurrentZone() == this)
            {
                card.ClearZone();
            }

            SetGuideHighlighted(false);
            Debug.Log($"<color=#FFAA00>[VRC-BoardGameKit] [CardSnapZone] カードを解放しました: スロット=[{slotName}], 解放カード=[{card.gameObject.name}] -> 残り最前面=[{(currentCard != null ? currentCard.gameObject.name : "None")}], 残りスタック={stackedCount}</color>");
        }

        /// <summary>
        /// スタックされている全カードをクリアする（リセット・場流れ用）
        /// </summary>
        public void ClearStack()
        {
            for (int i = 0; i < stackedCount; i++)
            {
                stackedCards[i] = null;
            }
            stackedCount = 0;
            isOccupied = false;
            currentCard = null;

            if (guideRenderer != null)
            {
                guideRenderer.enabled = true;
            }
            SetGuideHighlighted(false);
            Debug.Log($"[VRC-BoardGameKit] [CardSnapZone] スロットのスタックをクリアしました: スロット=[{slotName}]");
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
