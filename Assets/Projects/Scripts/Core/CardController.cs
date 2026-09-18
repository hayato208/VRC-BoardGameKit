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

        [Tooltip("現在接近中（候補）のスナップ枠")]
        public CardSnapZone candidateZone = null;

        private Rigidbody rb;
        private bool isHeld = false;

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

        /// <summary>
        /// 候補となるスナップ枠を登録する命令
        /// </summary>
        public void RegisterCandidateZone(CardSnapZone zone)
        {
            if (zone == null) return;
            candidateZone = zone;
        }

        /// <summary>
        /// 候補となっているスナップ枠の登録を解除する命令
        /// </summary>
        public void UnregisterCandidateZone(CardSnapZone zone)
        {
            if (candidateZone == zone)
            {
                candidateZone = null;
            }
        }

        #endregion

        #region VRCPickup イベントハンドラ

        public override void OnPickup()
        {
            isHeld = true;

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

            // スナップ枠の範囲内で手放された場合、スナップ枠に受け入れを要請する (Tell)
            if (candidateZone != null)
            {
                bool accepted = candidateZone.TrySnap(this);
                if (accepted)
                {
                    currentZone = candidateZone;
                    candidateZone = null;
                    return;
                }
            }

            // 枠がない、または受け入れを拒否された場合は、その場で空中完全静止
            FreezeInAir();
        }

        #endregion

        #region 近接トリガー検知 (Candidate Zone Detection)

        private void OnTriggerEnter(Collider other)
        {
            if (!isHeld) return;
            if (other == null) return;

            CardSnapZone zone = other.GetComponent<CardSnapZone>();
            if (zone != null && !zone.IsOccupied())
            {
                RegisterCandidateZone(zone);
                zone.SetGuideHighlighted(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other == null) return;

            CardSnapZone zone = other.GetComponent<CardSnapZone>();
            if (zone != null)
            {
                zone.SetGuideHighlighted(false);
                UnregisterCandidateZone(zone);
            }
        }

        #endregion
    }
}
