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
        [Header("Card Identity")]
        [Tooltip("カードの一意識別ID (0〜N-1)")]
        public int cardId = 0;

        [Header("System References")]
        [Tooltip("テーブル統括マネージャーへの参照")]
        public TableManager tableManager;

        [Header("Selection Visual")]
        [Tooltip("選択時の浮上オフセット量 (m)")]
        public float selectionElevation = 0.15f;

        [Header("Physics Settings")]
        [Tooltip("手で持っている間も isKinematic = true を維持するか（暴れ・ガタつき完全防止）")]
        public bool keepKinematicWhileHeld = true;

        [Header("Snap Threshold")]
        [Tooltip("スロット中心とカード中心の最大吸着許容距離 (m) ※カード幅0.7mに対し0.45m以内＝十分な重なりが必要")]
        public float maxSnapDistance = 0.45f;

        // --- 実行時動的ステート（シリアライズ除外・カプセル化） ---
        private bool isSelected = false;
        private CardSnapZone currentZone = null;
        private CardSnapZone candidateZone = null;

        private Rigidbody rb;
        private bool isHeld = false;

        // 非選択時のベース座標・回転（浮上から元に戻るためのSSOT）
        private Vector3 normalPosition;
        private Quaternion normalRotation;
        private bool hasNormalTransform = false;

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

        #region Tell, Don't Ask 外部公開命令 (Commands) ＆ カプセル化アクセサ (Getters)

        /// <summary>
        /// 現在収まっているスナップ枠を取得する（読み取り専用Getter）
        /// </summary>
        public CardSnapZone GetCurrentZone()
        {
            return currentZone;
        }

        /// <summary>
        /// 現在選択中（浮上中）かどうかを取得する（読み取り専用Getter）
        /// </summary>
        public bool IsSelected()
        {
            return isSelected;
        }

        /// <summary>
        /// 所属スナップ枠の割り当てを解除する命令（Tell）
        /// </summary>
        public void ClearZone()
        {
            this.currentZone = null;
        }

        /// <summary>
        /// 指定されたスナップ枠へカード自身を吸着整列させ、所属ゾーンを確実に記憶する（Tell原則）。
        /// </summary>
        /// <param name="zone">配置先のスナップ枠</param>
        /// <param name="targetPosition">目標ワールド座標</param>
        /// <param name="targetRotation">目標ワールド回転</param>
        public void SnapToZone(CardSnapZone zone, Vector3 targetPosition, Quaternion targetRotation)
        {
            this.currentZone = zone;
            this.normalPosition = targetPosition;
            this.normalRotation = targetRotation;
            this.hasNormalTransform = true;
            this.isSelected = false;

            SnapTo(targetPosition, targetRotation);
            Debug.Log($"[VRC-BoardGameKit] [CardController] カードがゾーンへ移動・吸着し基準位置を確定更新しました: {gameObject.name} -> {(zone != null ? zone.slotName : "None")} (Pos: {targetPosition})");
        }

        /// <summary>
        /// 外部のスナップ枠（CardSnapZone）等から、目標姿勢への吸着を命じられたときの処理（Tell）。
        /// カード自身が Rigidbody や Transform を制御して指定位置へ整列する。
        /// </summary>
        /// <param name="targetPosition">目標ワールド座標</param>
        /// <param name="targetRotation">目標ワールド回転</param>
        public void SnapTo(Vector3 targetPosition, Quaternion targetRotation)
        {
            normalPosition = targetPosition;
            normalRotation = targetRotation;
            hasNormalTransform = true;
            isSelected = false;

            transform.position = targetPosition;
            transform.rotation = targetRotation;

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            gameObject.SetActive(true);
            Debug.Log($"[VRC-BoardGameKit] [CardController] カード自身が指定位置へ吸着整列しました: {gameObject.name}");
        }

        /// <summary>
        /// 選択状態に応じた視覚フィードバック（浮上演出）を切り替える。
        /// Tell, Don't Ask原則に基づき、カード自身が自身の姿勢を制御する。
        /// </summary>
        /// <param name="selected">trueで15cm浮上、falseで通常位置復帰</param>
        public void SetSelectedVisual(bool selected)
        {
            isSelected = selected;
            if (!hasNormalTransform)
            {
                normalPosition = transform.position;
                normalRotation = transform.rotation;
                hasNormalTransform = true;
            }

            if (isSelected)
            {
                // スロットの板に沿った上方向（transform.up）に15cm浮上
                transform.position = normalPosition + (transform.up * selectionElevation);
                transform.rotation = normalRotation;
            }
            else
            {
                transform.position = normalPosition;
                transform.rotation = normalRotation;
            }

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            Debug.Log($"[VRC-BoardGameKit] [CardController] 選択視覚状態を更新しました: {gameObject.name} (Selected: {isSelected})");
        }

        /// <summary>
        /// 現在の空中の位置でピタッと完全静止する自律命令。
        /// 手放された際に近くにスナップ枠がない場合や、枠から拒否された場合に実行される。
        /// </summary>
        public void FreezeInAir()
        {
            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            Debug.Log($"[VRC-BoardGameKit] [CardController] カードが空中でピタッと完全静止しました: {gameObject.name}");
        }

        /// <summary>
        /// ゲームリセット時に山札の位置へ回収・初期化する命令。
        /// 以前収まっていたスナップ枠を安全に解放し、山札の待機位置へ戻す。
        /// </summary>
        public void ResetToDeck(Vector3 deckPos, Quaternion deckRot)
        {
            isSelected = false;
            hasNormalTransform = false;

            if (currentZone != null)
            {
                currentZone.ReleaseCard(this);
                currentZone = null;
            }

            ClearAllCandidates();

            transform.position = deckPos;
            transform.rotation = deckRot;

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                }
            }

            gameObject.SetActive(false);
            Debug.Log($"[VRC-BoardGameKit] [CardController] カードを山札へ回収・初期化しました: {gameObject.name} (ID: {cardId})");
        }

        #endregion

        #region VRChat Interaction イベントハンドラ

        /// <summary>
        /// 視線を合わせてクリック（ネイティブInteract）されたときの処理。
        /// TableManager へクリック通知を発行する。
        /// </summary>
        public override void Interact()
        {
            if (tableManager != null)
            {
                tableManager.OnCardClicked(this);
            }
            else
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [CardController] TableManager 参照が未設定です: {gameObject.name}");
            }
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
                if (zone == null) continue;
                if (zone.IsOccupied() && !zone.allowStack) continue;

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
