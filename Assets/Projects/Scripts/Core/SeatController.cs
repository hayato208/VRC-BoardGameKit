using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace BoardGameKit.Core
{
    /// <summary>
    /// VRCStation（椅子）とプレイヤー・手札トレイを連動させるコントローラー。
    /// プレイヤーの着席・離席を検知し、対応する手札トレイの所有権を割り当てる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SeatController : UdonSharpBehaviour
    {
        [Header("Seat Identity")]
        [Tooltip("座席番号（0〜座席数-1）")]
        [SerializeField] private int seatIndex = 0;

        [Header("References")]
        [Tooltip("この座席に紐付く手札トレイ")]
        [SerializeField] private HandTrayController linkedHandTray;

        [Tooltip("全体進行を司るTableManagerへの参照")]
        [SerializeField] private TableManager tableManager; // 【★】Inspectorで設定

        // --- 同期変数 ---
        // 現在着席しているプレイヤーID（空席時は -1）
        [UdonSynced]
        private int seatedPlayerId = -1;

        /// <summary>
        /// VRCStationに着席したときのコールバック
        /// </summary>
        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (player == null) return;

            if (player.isLocal)
            {
                if (TakeOwnership())
                {
                    seatedPlayerId = player.playerId;
                    RequestSerialization();

                    if (linkedHandTray != null)
                    {
                        linkedHandTray.AssignOwner(player.playerId);
                    }

                    if (tableManager != null)
                    {
                        tableManager.OnPlayerSeated(seatIndex, player.playerId);
                    }
                }
            }
        }

        /// <summary>
        /// VRCStationから立ち上がった（離席した）ときのコールバック
        /// </summary>
        public override void OnStationExited(VRCPlayerApi player)
        {
            if (player == null) return;

            if (player.isLocal)
            {
                if (TakeOwnership())
                {
                    seatedPlayerId = -1;
                    RequestSerialization();

                    if (linkedHandTray != null)
                    {
                        linkedHandTray.ReleaseOwner();
                    }

                    if (tableManager != null)
                    {
                        tableManager.OnPlayerLeftSeat(seatIndex, player.playerId);
                    }
                }
            }
        }

        private bool TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject))
            {
                VRCPlayerApi localPlayer = Networking.LocalPlayer;
                if (localPlayer != null)
                {
                    Networking.SetOwner(localPlayer, gameObject);
                }
            }
            return Networking.IsOwner(gameObject);
        }

        // --- ゲッター ---
        public int GetSeatIndex() => seatIndex;
        public int GetSeatedPlayerId() => seatedPlayerId;
        public bool IsOccupied() => seatedPlayerId != -1;
        public HandTrayController GetLinkedHandTray() => linkedHandTray;
    }
}
