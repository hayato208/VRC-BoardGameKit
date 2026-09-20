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
        public CardSnapZone[] snapZones;

        [Tooltip("スロット群をまとめるルートGameObject（非表示/表示トグル用）")]
        [SerializeField] private GameObject slotContainer;

        private void Start()
        {
            // 初期状態は非表示（未着席）
            SetAreaVisible(false);
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
        /// 空いているスロット（CardSnapZone）を1つ取得する（パターンBのUIドロー等で使用）
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
        /// 指定インデックスのスロットを取得
        /// </summary>
        public CardSnapZone GetSlotAt(int index)
        {
            if (snapZones == null || index < 0 || index >= snapZones.Length) return null;
            return snapZones[index];
        }

        /// <summary>
        /// 現在有効なスロット数を取得
        /// </summary>
        public int GetActiveSlotCount()
        {
            return (snapZones != null) ? snapZones.Length : slotCount;
        }

        /// <summary>
        /// 管理下の全手札スロットを空にする（ゲームリセット時など）
        /// </summary>
        public void ClearAllSlots()
        {
            if (snapZones == null) return;
            for (int i = 0; i < snapZones.Length; i++)
            {
                if (snapZones[i] != null)
                {
                    snapZones[i].ClearStack();
                }
            }
        }

        // --- 幾何計算用ゲッター（エディタ拡張連携） ---
        public float GetRadius() => radius;
        public float GetAngleStep() => angleStep;
        public float GetSlotTiltAngle() => slotTiltAngle;
        public float GetSlotHeightY() => slotHeightY;
        public int GetConfiguredSlotCount() => slotCount;
    }
}
