using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace BoardGameKit.Core
{
    /// <summary>
    /// プレイエリア（座席）とプレイヤー・手札トレイを連動させるコントローラー。
    /// VRCStationによる移動拘束を行わず、オブジェクトクリック（Interact）でプレイエリアへの参加・離席をトグル管理する。
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
        [SerializeField] private TableManager tableManager;

        [Header("Visual Feedback (Optional)")]
        [Tooltip("着席・空席時に色を変更するRenderer（未設定時は自身のRendererを使用）")]
        [SerializeField] private MeshRenderer seatRenderer;
        [SerializeField] private Color vacantColor = new Color(0.3f, 0.3f, 0.35f, 1.0f);
        [SerializeField] private Color occupiedColor = new Color(0.1f, 0.6f, 0.9f, 1.0f);

        // --- 同期変数 ---
        // 現在着席しているプレイヤーID（空席時は -1）
        [UdonSynced]
        private int seatedPlayerId = -1;

        // 直前の状態（差分検知用）
        private int prevSeatedPlayerId = -1;

        private void Start()
        {
            if (seatRenderer == null)
            {
                seatRenderer = GetComponent<MeshRenderer>();
            }
            UpdateVisualAndInteraction();
        }

        /// <summary>
        /// オブジェクトをクリック（Interact）したときに参加 / 離席をトグル
        /// </summary>
        public override void Interact()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            int myId = localPlayer.playerId;

            // 1. 空席の場合：参加（プレイエリアにつく）
            if (seatedPlayerId == -1)
            {
                JoinSeat(localPlayer);
            }
            // 2. 自分が着席中の場合：離席（プレイエリアから離れる）
            else if (seatedPlayerId == myId)
            {
                LeaveSeat(localPlayer);
            }
            // 3. 他人が着席中の場合：何もしない
            else
            {
                Debug.Log($"[VRC-BoardGameKit] Seat_{seatIndex} は既に他のプレイヤー (ID: {seatedPlayerId}) が使用中です。");
            }
        }

        /// <summary>
        /// プレイエリアに参加登録
        /// </summary>
        public void JoinSeat(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;

            Debug.Log($"<color=#00FF00>[VRC-BoardGameKit] プレイエリア Seat_{seatIndex} につきました: {player.displayName} (ID: {player.playerId})</color>");

            if (TakeOwnership())
            {
                seatedPlayerId = player.playerId;
                RequestSerialization();
                ApplySeatStateChange(prevSeatedPlayerId, seatedPlayerId);
                prevSeatedPlayerId = seatedPlayerId;
            }
        }

        /// <summary>
        /// プレイエリアから離席
        /// </summary>
        public void LeaveSeat(VRCPlayerApi player)
        {
            if (player == null || !player.isLocal) return;

            Debug.Log($"<color=#FFFF00>[VRC-BoardGameKit] プレイエリア Seat_{seatIndex} から離れました: {player.displayName} (ID: {player.playerId})</color>");

            if (TakeOwnership())
            {
                int oldId = seatedPlayerId;
                seatedPlayerId = -1;
                RequestSerialization();
                ApplySeatStateChange(oldId, -1);
                prevSeatedPlayerId = -1;
            }
        }

        /// <summary>
        /// ネットワーク同期受信時の処理
        /// </summary>
        public override void OnDeserialization()
        {
            if (seatedPlayerId != prevSeatedPlayerId)
            {
                ApplySeatStateChange(prevSeatedPlayerId, seatedPlayerId);
                prevSeatedPlayerId = seatedPlayerId;
            }
        }

        /// <summary>
        /// 状態変化を関連コンポーネントおよびUIに反映
        /// </summary>
        private void ApplySeatStateChange(int oldPlayerId, int newPlayerId)
        {
            UpdateVisualAndInteraction();

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            int localId = (localPlayer != null) ? localPlayer.playerId : -1;

            // 新しく着席した場合
            if (newPlayerId != -1)
            {
                if (linkedHandTray != null && newPlayerId == localId)
                {
                    linkedHandTray.AssignOwner(newPlayerId);
                }

                if (tableManager != null)
                {
                    tableManager.OnPlayerSeated(seatIndex, newPlayerId);
                }
            }
            // 離席した場合
            else if (oldPlayerId != -1)
            {
                if (linkedHandTray != null && oldPlayerId == localId)
                {
                    linkedHandTray.ReleaseOwner();
                }

                if (tableManager != null)
                {
                    tableManager.OnPlayerLeftSeat(seatIndex, oldPlayerId);
                }
            }
        }

        /// <summary>
        /// 外観（マテリアルカラー）およびインタラクトテキストの更新
        /// </summary>
        private void UpdateVisualAndInteraction()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            int myId = (localPlayer != null) ? localPlayer.playerId : -1;

            if (seatedPlayerId == -1)
            {
                // 空席
                InteractionText = $"席 {seatIndex + 1} につく (Join Seat {seatIndex + 1})";
                if (seatRenderer != null)
                {
                    seatRenderer.material.color = vacantColor;
                }
            }
            else if (seatedPlayerId == myId)
            {
                // 自分が着席中
                InteractionText = $"席 {seatIndex + 1} を離れる (Leave Seat {seatIndex + 1})";
                if (seatRenderer != null)
                {
                    seatRenderer.material.color = occupiedColor;
                }
            }
            else
            {
                // 他人が着席中
                InteractionText = $"席 {seatIndex + 1} 使用中 (Occupied)";
                if (seatRenderer != null)
                {
                    seatRenderer.material.color = occupiedColor;
                }
            }
        }

        /// <summary>
        /// プレイヤーがワールドから退出した際の安全解放
        /// </summary>
        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (player == null) return;

            // 退出したプレイヤーがこの席についていた場合
            if (seatedPlayerId == player.playerId)
            {
                Debug.Log($"[VRC-BoardGameKit] Seat_{seatIndex} の着席プレイヤー (ID: {player.playerId}) がワールドを退出したため空席にします。");
                if (Networking.IsMaster)
                {
                    if (TakeOwnership())
                    {
                        int oldId = seatedPlayerId;
                        seatedPlayerId = -1;
                        RequestSerialization();
                        ApplySeatStateChange(oldId, -1);
                        prevSeatedPlayerId = -1;
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
