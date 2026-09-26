using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// プレイヤー包囲型円弧スロット（Arcade Cockpit Layout）を管理するコンポーネント。
    /// 着席したプレイヤーの立ち位置を中心（半径約1.1m）として、大判カード（70cm×98cm）用の
    /// CardSnapZone を扇状（円弧状）に展開・管理する。
    /// スロット数は可変（初期値5）で、着席時のみローカルに動的出現する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PersonalHandArea : UdonSharpBehaviour
    {
        [Header("Slot Configuration")]
        [Tooltip("手札スロット数（可変）")]
        [SerializeField] private int slotCount = 5;

        [Header("Arc Geometry Parameters")]
        [Tooltip("プレイヤー中心からの半径距離 (m)")]
        [SerializeField] private float radius = 1.40f;

        [Tooltip("各スロット間の展開角度ステップ (度) ※チルト角を考慮し下端でも8cm以上の隙間を確保")]
        [SerializeField] private float angleStep = 36.0f;

        [Tooltip("スロットの手前見下ろしチルト角 (度) ※視線正対と下端間隔を両立する12度")]
        [SerializeField] private float slotTiltAngle = 12.0f;

        [Tooltip("スロットの基準高さ Y (m)")]
        [SerializeField] private float slotHeightY = 0.85f;

        [Header("Slot References")]
        [Tooltip("管理下の大判 CardSnapZone 配列")]
        [SerializeField] private CardSnapZone[] snapZones;

        [Tooltip("スロット群をまとめるルートGameObject（非表示/表示トグル用）")]
        [SerializeField] private GameObject slotContainer;

        [Header("UI Feedback")]
        [Tooltip("手札枚数・ページ表示用TextMeshPro（手元パネル上段）")]
        [SerializeField] private TMPro.TextMeshPro handCountText;

        [Tooltip("テーブル統括マネージャーへの参照（ページ送り時の選択解除用）")]
        [SerializeField] private TableManager tableManager;

        public CardSnapZone[] SnapZones => snapZones;
        public void SetSnapZones(CardSnapZone[] zones) => snapZones = zones;

        public void SetHandCountText(TMPro.TextMeshPro tmp) => handCountText = tmp;
        public void SetTableManager(TableManager tm) => tableManager = tm;

        // --- 論理手札管理ステート（シリアライズ除外・ローカル実行時状態） ---
        private const int MAX_HAND_CAPACITY = 64;
        private CardController[] heldCards = new CardController[MAX_HAND_CAPACITY];
        private int heldCardCount = 0;
        private int pageOffset = 0;

        private void Start()
        {
            // 初期状態は非表示（未着席）
            SetAreaVisible(false);
            UpdateHandCountVisual();
        }

        /// <summary>
        /// 手札エリア全体の表示/非表示を切り替える（着席連動）
        /// </summary>
        /// <param name="visible">表示するかどうか</param>
        public void SetAreaVisible(bool visible)
        {
            if (slotContainer != null)
            {
                slotContainer.SetActive(visible);
            }
            else
            {
                gameObject.SetActive(visible);
            }

            Debug.Log($"[VRC-BoardGameKit] [PersonalHandArea] エリア表示切替: {visible} (Slots: {GetActiveSlotCount()})");
        }

        /// <summary>
        /// 空いているスロット（CardSnapZone）を1つ取得する（互換性維持用）
        /// </summary>
        /// <returns>空きスロット。全枠満杯ならnull</returns>
        public CardSnapZone GetFirstEmptySlot()
        {
            if (snapZones == null) return null;

            for (int i = 0; i < snapZones.Length; i++)
            {
                CardSnapZone zone = snapZones[i];
                if (zone != null && !zone.IsOccupied())
                {
                    return zone;
                }
            }
            return null;
        }

        /// <summary>
        /// 山札からカードを1枚引き、手札へ追加する（ドロー処理の一元管理・SSOT）。
        /// 物理スロット数を超える場合も論理手札配列に保持され、ページ送りで確認可能。
        /// </summary>
        /// <param name="deckManager">山札マネージャー</param>
        /// <returns>配備したカードID（山札切れや満杯の場合は -1）</returns>
        public int TryDrawCard(DeckManager deckManager)
        {
            if (deckManager == null) return -1;

            // 1. 山札切れの事前ガード
            if (deckManager.IsDeckEmpty())
            {
                Debug.LogWarning("[VRC-BoardGameKit] [Draw] 山札が切れています（残り0枚）。カードを引くことはできません。");
                return -1;
            }

            // 2. 手札キャパシティ上限チェック
            if (heldCardCount >= MAX_HAND_CAPACITY)
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [Draw] 手札が上限（{MAX_HAND_CAPACITY}枚）に達しています。");
                return -1;
            }

            // 3. 山札からカードIDを1枚ドロー
            int drawnCardId = deckManager.DrawCard();
            if (drawnCardId == -1)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [Draw] 山札からのドローに失敗しました。");
                return -1;
            }

            // 4. カード実体の取得
            CardController[] pool = deckManager.CardPool;
            if (pool == null || drawnCardId < 0 || drawnCardId >= pool.Length)
            {
                Debug.LogError($"[VRC-BoardGameKit] [Draw] カードプール内にカードID {drawnCardId} が存在しません。");
                return -1;
            }

            CardController card = pool[drawnCardId];
            if (card == null)
            {
                Debug.LogError($"[VRC-BoardGameKit] [Draw] カードID {drawnCardId} の実体がnullです。");
                return -1;
            }

            // 5. 論理手札配列へ追加
            heldCards[heldCardCount] = card;
            heldCardCount++;

            // 6. 表示スロットの再描画
            RefreshDisplayedSlots();

            Debug.Log($"<color=#00FF00><b>[VRC-BoardGameKit]</b> [Draw] ドロー成功: Card ID {drawnCardId} (手札総数: {heldCardCount}枚)</color>");
            return drawnCardId;
        }

        /// <summary>
        /// 手札から指定されたカードを取り除きます（カードプレイ時、捨て札時等）。
        /// </summary>
        /// <param name="card">取り除くカード</param>
        /// <returns>正常に取り除かれた場合はtrue</returns>
        public bool RemoveCard(CardController card)
        {
            if (card == null || heldCardCount == 0) return false;

            int foundIndex = -1;
            for (int i = 0; i < heldCardCount; i++)
            {
                if (heldCards[i] == card)
                {
                    foundIndex = i;
                    break;
                }
            }

            if (foundIndex == -1) return false;

            // 後続要素を前方へ詰める
            for (int i = foundIndex; i < heldCardCount - 1; i++)
            {
                heldCards[i] = heldCards[i + 1];
            }
            heldCards[heldCardCount - 1] = null;
            heldCardCount--;

            // ページ範囲の補正（もし現ページが空になったら前ページへ戻す）
            int activeSlots = GetActiveSlotCount();
            if (pageOffset >= heldCardCount && pageOffset > 0)
            {
                pageOffset = Mathf.Max(0, pageOffset - activeSlots);
            }

            RefreshDisplayedSlots();
            return true;
        }

        /// <summary>
        /// 次のページへ手札表示をスクロールします（次へ ▶）
        /// </summary>
        public void NextPage()
        {
            int activeSlots = GetActiveSlotCount();
            if (pageOffset + activeSlots < heldCardCount)
            {
                pageOffset += activeSlots;
                if (tableManager != null)
                {
                    tableManager.ClearAllSelections();
                }
                RefreshDisplayedSlots();
                Debug.Log($"[VRC-BoardGameKit] [PersonalHandArea] 次ページへスクロール: pageOffset={pageOffset}");
            }
        }

        /// <summary>
        /// 前のページへ手札表示をスクロールします（◀ 前へ）
        /// </summary>
        public void PrevPage()
        {
            int activeSlots = GetActiveSlotCount();
            if (pageOffset - activeSlots >= 0)
            {
                pageOffset -= activeSlots;
                if (tableManager != null)
                {
                    tableManager.ClearAllSelections();
                }
                RefreshDisplayedSlots();
                Debug.Log($"[VRC-BoardGameKit] [PersonalHandArea] 前ページへスクロール: pageOffset={pageOffset}");
            }
        }

        /// <summary>
        /// 現在のページオフセットに基づき、物理スロット（CardSnapZone）へのカード配置を再同期します。
        /// </summary>
        public void RefreshDisplayedSlots()
        {
            int activeSlots = GetActiveSlotCount();
            if (snapZones == null) return;

            for (int i = 0; i < activeSlots; i++)
            {
                CardSnapZone zone = snapZones[i];
                if (zone == null) continue;

                int cardIndex = pageOffset + i;
                if (cardIndex < heldCardCount)
                {
                    CardController targetCard = heldCards[cardIndex];
                    if (targetCard != null)
                    {
                        // 既にスロットに収まっているカードと異なる場合のみスナップ更新
                        if (zone.GetCurrentCard() != targetCard)
                        {
                            CardController oldCard = zone.GetCurrentCard();
                            if (oldCard != null)
                            {
                                zone.ReleaseCard(oldCard);
                                oldCard.gameObject.SetActive(false); // 画面外非表示
                            }

                            targetCard.gameObject.SetActive(true);
                            zone.TrySnap(targetCard);
                        }
                    }
                }
                else
                {
                    // 枠外（カードなし空きスロット）
                    CardController oldCard = zone.GetCurrentCard();
                    if (oldCard != null)
                    {
                        zone.ReleaseCard(oldCard);
                        oldCard.gameObject.SetActive(false);
                    }
                }
            }

            UpdateHandCountVisual();
        }

        /// <summary>
        /// 手元パネルの手札枚数・表示範囲テキスト（TextMeshPro）を更新します。
        /// </summary>
        public void UpdateHandCountVisual()
        {
            if (handCountText == null) return;

            int activeSlots = GetActiveSlotCount();
            if (heldCardCount == 0)
            {
                handCountText.text = "手札: 0枚";
            }
            else if (heldCardCount <= activeSlots)
            {
                handCountText.text = $"手札: {heldCardCount}枚";
            }
            else
            {
                int startNum = pageOffset + 1;
                int endNum = Mathf.Min(pageOffset + activeSlots, heldCardCount);
                handCountText.text = $"手札: {heldCardCount}枚 ({startNum}-{endNum})";
            }
        }

        /// <summary>
        /// 現在有効なスロット数を取得
        /// </summary>
        public int GetActiveSlotCount()
        {
            return (snapZones != null) ? snapZones.Length : slotCount;
        }

        /// <summary>
        /// 管理下の全手札スロットおよび手札配列を空にする（ゲームリセット時など）
        /// </summary>
        public void ClearAllSlots()
        {
            if (snapZones != null)
            {
                for (int i = 0; i < snapZones.Length; i++)
                {
                    if (snapZones[i] != null)
                    {
                        snapZones[i].ClearStack();
                    }
                }
            }

            for (int i = 0; i < heldCardCount; i++)
            {
                if (heldCards[i] != null)
                {
                    heldCards[i].gameObject.SetActive(false);
                    heldCards[i] = null;
                }
            }
            heldCardCount = 0;
            pageOffset = 0;

            UpdateHandCountVisual();
        }

        /// <summary>
        /// 現在プレイヤーが保持している総手札枚数を取得します。
        /// </summary>
        public int GetHeldCardCount()
        {
            return heldCardCount;
        }

        /// <summary>
        /// 現在のページオフセット（表示開始インデックス）を取得します。
        /// </summary>
        public int GetPageOffset() => pageOffset;

        // --- 幾何計算用ゲッター（エディタ拡張連携） ---
        public float GetRadius() => radius;
        public float GetAngleStep() => angleStep;
        public float GetSlotTiltAngle() => slotTiltAngle;
        public float GetSlotHeightY() => slotHeightY;
        public int GetConfiguredSlotCount() => slotCount;
    }
}
