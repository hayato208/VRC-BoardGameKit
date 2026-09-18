using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// カード側にアタッチされる物理Pickup＆スナップ制御コンポーネント。
    /// VRCPickupと連動し、手で持った際の暴れ防止、手放した瞬間の空中完全静止、
    /// およびCardSnapZone（スナップ枠）への磁石吸着を司る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CardPickupHandler : UdonSharpBehaviour
    {
        [Header("Physics Settings")]
        [Tooltip("手で持っている間も isKinematic = true を維持するか（暴れ・ガタつき完全防止）")]
        public bool keepKinematicWhileHeld = true;

        [Header("Current Snap State")]
        [Tooltip("現在接近しているスナップ枠")]
        public CardSnapZone nearbySnapZone = null;

        [Tooltip("現在嵌まっているスナップ枠")]
        public CardSnapZone currentSnapZone = null;

        private Rigidbody rb;
        private bool isHeld = false;

        private void Start()
        {
            rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                // 初期状態は空中でピタッと静止
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        public override void OnPickup()
        {
            isHeld = true;

            // 掴んだプレイヤーに所有権を移行
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer != null)
            {
                Networking.SetOwner(localPlayer, gameObject);
            }

            // 以前収まっていたスロットから離脱
            if (currentSnapZone != null)
            {
                currentSnapZone.OnCardRemoved();
                currentSnapZone = null;
            }

            if (rb != null)
            {
                // 持っている間もKinematicを維持すれば、壁やテーブルに当たっても絶対に暴れない
                rb.isKinematic = keepKinematicWhileHeld;
            }

            Debug.Log("[VRC-BoardGameKit] [CardPickup] カードを掴みました。");
        }

        public override void OnDrop()
        {
            isHeld = false;

            if (rb != null)
            {
                // 手の振りの勢い（慣性）を完全に消滅させて空中でピタッと静止
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.isKinematic = true;
            }

            // スナップ枠の範囲内にいれば、磁石のように定位置へ吸着
            if (nearbySnapZone != null && !nearbySnapZone.isOccupied)
            {
                transform.position = nearbySnapZone.transform.position;
                transform.rotation = nearbySnapZone.transform.rotation;

                currentSnapZone = nearbySnapZone;
                currentSnapZone.OnCardSnapped(gameObject);
                nearbySnapZone = null;

                Debug.Log($"[VRC-BoardGameKit] [CardPickup] カードがスロットに吸着しました: {currentSnapZone.slotName}");
            }
            else
            {
                Debug.Log("[VRC-BoardGameKit] [CardPickup] カードを手放しました。（空中でピタッと静止）");
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isHeld) return;

            // 接触相手がスナップゾーンか確認
            CardSnapZone zone = other.GetComponent<CardSnapZone>();
            if (zone != null && !zone.isOccupied)
            {
                nearbySnapZone = zone;
                zone.SetGuideHighlighted(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            CardSnapZone zone = other.GetComponent<CardSnapZone>();
            if (zone != null && zone == nearbySnapZone)
            {
                zone.SetGuideHighlighted(false);
                nearbySnapZone = null;
            }
        }
    }
}
