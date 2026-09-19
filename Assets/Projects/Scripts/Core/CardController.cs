using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// カードオブジェクト自身にアタッチされる中核コントローラー。
    /// Tell, Don't Ask原則に基づき、自身のTransform・物理挙動（Rigidbody/VRCPickup）を完全に自己管理する。
    /// 外部からは SnapTo() や FreezeInAir() 等の命令（Tell）を受け取って自律的に姿勢制御を行う。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CardController : UdonSharpBehaviour
    {
        [Header("Physics Settings")]
        [Tooltip("手で持っている間も isKinematic = true を維持するか（暴れ・ガタつき完全防止）")]
        public bool keepKinematicWhileHeld = true;

        [Header("Runtime State")]
        [Tooltip("現在収まっているスナップ枠")]
        public CardSnapZone currentZone = null;

        [Tooltip("現在接近中（最良候補）のスナップ枠")]
        public CardSnapZone candidateZone = null;

        [Header("Snap Threshold")]
        [Tooltip("スロット中心とカード中心の最大吸着許容距離 (m) ※カード幅0.7mに対し0.45m以内＝十分な重なりが必要")]
        public float maxSnapDistance = 0.45f;

        private Rigidbody rb;
        private bool isHeld = false;

        // 接触中の候補スロット配列（U#最適化: 最大8要素の固定長）
        private const int MAX_CANDIDATES = 8;
        private CardSnapZone[] candidateBuffer = new CardSnapZone[MAX_CANDIDATES];
        private int candidateCount = 0;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 初期状態: 空中でピタッと完全静止
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void Update()
        {
            // 手に持っている間、カード中心に最も近い（最も多く重なっている）最良スロットをリアルタイム追跡
            if (isHeld)
            {
                UpdateBestCandidateZone();
            }
        }

        #region Tell, Don't Ask 外部公開命令 (Commands)

        /// <summary>
        /// 指定されたワールド位置・回転へ自身を移動・整列させ、物理固定する命令。
        /// スナップ枠やテーブルシステムから命じられて実行する。
        /// </summary>
        /// <param name="targetPosition">目標ワールド座標</param>
        /// <param name="targetRotation">目標ワールド回転</param>
        public void SnapTo(Vector3 targetPosition, Quaternion targetRotation)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;

            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            Debug.Log($"[VRC-BoardGameKit] [CardController] カード自身が指定位置へ吸着整列しました: {gameObject.name}");
        }

        /// <summary>
        /// 現在の空中の位置でピタッと完全静止する自律命令。
        /// 手放された際に近くにスナップ枠がない場合や、枠から拒否された場合に実行される。
        /// </summary>
        public void FreezeInAir()
        {
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            Debug.Log($"[VRC-BoardGameKit] [CardController] カードが空中でピタッと完全静止しました: {gameObject.name}");
        }

        #endregion

        #region VRCPickup イベントハンドラ

        public override void OnPickup()
        {
            isHeld = true;
            ClearAllCandidates();

            // 掴んだプレイヤーに所有権を移行
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null)
            {
                Networking.SetOwner(localPlayer, gameObject);
            }

            // 以前収まっていたスナップ枠があれば解放を命じる (Tell)
            if (currentZone != null)
            {
                currentZone.ReleaseCard(this);
                currentZone = null;
            }

            if (rb != null)
            {
                // 持っている間もKinematicを維持すれば、壁やテーブルに当たっても絶対に暴れない
                rb.isKinematic = keepKinematicWhileHeld;
            }

            Debug.Log($"[VRC-BoardGameKit] [CardController] カードを掴みました: {gameObject.name}");
        }

        public override void OnDrop()
        {
            isHeld = false;

            // 最後に最良候補を確定更新
            UpdateBestCandidateZone();

            // 最も重なっており有効範囲内にあるスナップ枠に受け入れを要請する (Tell)
            if (candidateZone != null)
            {
                CardSnapZone targetZone = candidateZone;
                ClearAllCandidates();

                bool accepted = targetZone.TrySnap(this);
                if (accepted)
                {
                    currentZone = targetZone;
                    return;
                }
            }

            ClearAllCandidates();

            // 枠がない、または受け入れを拒否された場合は、その場で空中完全静止
            FreezeInAir();
        }

        #endregion

        #region 最短距離・重なり判定アルゴリズム (Best Fit Zone Detection)

        private void UpdateBestCandidateZone()
        {
            CardSnapZone bestZone = null;
            float minDistanceSqr = maxSnapDistance * maxSnapDistance;
            Vector3 myCenter = transform.position;

            for (int i = 0; i < candidateCount; i++)
            {
                CardSnapZone zone = candidateBuffer[i];
                if (zone == null || zone.IsOccupied()) continue;

                // カード中心とスロット中心の平面/空間距離を計算
                float distSqr = (zone.transform.position - myCenter).sqrMagnitude;
                if (distSqr < minDistanceSqr)
                {
                    minDistanceSqr = distSqr;
                    bestZone = zone;
                }
            }

            // 最良候補が変わった場合のみハイライトを切り替え
            if (candidateZone != bestZone)
            {
                if (candidateZone != null)
                {
                    candidateZone.SetGuideHighlighted(false);
                }

                candidateZone = bestZone;

                if (candidateZone != null)
                {
                    candidateZone.SetGuideHighlighted(true);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isHeld || other == null) return;

            CardSnapZone zone = other.GetComponent<CardSnapZone>();
            if (zone != null && !zone.IsOccupied())
            {
                AddCandidateZone(zone);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;

            CardSnapZone zone = other.GetComponent<CardSnapZone>();
            if (zone != null)
            {
                RemoveCandidateZone(zone);
            }
        }

        private void AddCandidateZone(CardSnapZone zone)
        {
            for (int i = 0; i < candidateCount; i++)
            {
                if (candidateBuffer[i] == zone) return;
            }

            if (candidateCount < MAX_CANDIDATES)
            {
                candidateBuffer[candidateCount] = zone;
                candidateCount++;
            }
        }

        private void RemoveCandidateZone(CardSnapZone zone)
        {
            for (int i = 0; i < candidateCount; i++)
            {
                if (candidateBuffer[i] == zone)
                {
                    if (candidateZone == zone)
                    {
                        zone.SetGuideHighlighted(false);
                        candidateZone = null;
                    }

                    candidateBuffer[i] = candidateBuffer[candidateCount - 1];
                    candidateBuffer[candidateCount - 1] = null;
                    candidateCount--;
                    break;
                }
            }
        }

        private void ClearAllCandidates()
        {
            if (candidateZone != null)
            {
                candidateZone.SetGuideHighlighted(false);
                candidateZone = null;
            }

            for (int i = 0; i < candidateCount; i++)
            {
                if (candidateBuffer[i] != null)
                {
                    candidateBuffer[i].SetGuideHighlighted(false);
                    candidateBuffer[i] = null;
                }
            }
            candidateCount = 0;
        }

        #endregion
    }
}
