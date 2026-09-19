using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 各プレイヤーの手札トレイを管理するクラス。
    /// 手札の追加・プレイおよび「本人のみ表面が見え、他人は裏面に見える」秘匿表示制御を司る。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class HandTrayController : UdonSharpBehaviour
    {
        [Header("Hand Settings")]
        [Tooltip("最大手札枚数")]
        [SerializeField] private int maxHandCount = 10; // 【★】ゲームに応じて調整

        [Header("Slot References")]
        [Tooltip("カードを配置するトレイ上のアンカー（Transform一覧）")]
        [SerializeField] private Transform[] cardSlotAnchors;

        [Tooltip("手札カードオブジェクトのMeshRenderer一覧（各スロットに対応）")]
        [SerializeField] private MeshRenderer[] cardMeshRenderers;

        [Header("Card Visual Materials")]
        [Tooltip("カード裏面用マテリアル（全員共通・他者視点）")]
        [SerializeField] private Material cardBackMaterial;

        [Tooltip("カード表面用マテリアル（テクスチャ動的差し替え用）")]
        [SerializeField] private Material cardFrontMaterial;

        // --- 同期変数 ---
        // 手札のスロットごとのカードID（-1は空きスロット）
        [UdonSynced]
        private int[] handCardIds;

        // このトレイを使用しているプレイヤーID（未着席時は -1）
        [UdonSynced]
        private int trayOwnerPlayerId = -1;

        // 手札の現在枚数
        [UdonSynced]
        private int currentCardCount = 0;

        private void Start()
        {
            if (Networking.IsOwner(gameObject))
            {
                InitializeHandSlots();
            }
            UpdateCardVisuals();
        }

        /// <summary>
        /// スロット配列の初期化
        /// </summary>
        private void InitializeHandSlots()
        {
            handCardIds = new int[maxHandCount];
            for (int i = 0; i < maxHandCount; i++)
            {
                handCardIds[i] = -1;
            }
            currentCardCount = 0;
            RequestSerialization();
        }

        /// <summary>
        /// トレイの所有プレイヤー（座席のプレイヤー）を設定
        /// </summary>
        public void AssignOwner(int playerId)
        {
            if (!TakeOwnership()) return;

            trayOwnerPlayerId = playerId;
            RequestSerialization();
            UpdateCardVisuals();
        }

        /// <summary>
        /// 座席から離席した際にトレイをリセット
        /// </summary>
        public void ReleaseOwner()
        {
            if (!TakeOwnership()) return;

            trayOwnerPlayerId = -1;
            ClearHandInternal();
            RequestSerialization();
            UpdateCardVisuals();
        }

        /// <summary>
        /// 手札にカードを追加する
        /// </summary>
        public int AddCard(int cardId)
        {
            if (cardId < 0) return -1;
            if (currentCardCount >= maxHandCount) return -1;
            if (!TakeOwnership()) return -1;

            if (handCardIds == null)
            {
                InitializeHandSlots();
            }

            int targetSlot = -1;
            for (int i = 0; i < maxHandCount; i++)
            {
                if (handCardIds[i] == -1)
                {
                    targetSlot = i;
                    break;
                }
            }

            if (targetSlot != -1)
            {
                handCardIds[targetSlot] = cardId;
                currentCardCount++;
                RequestSerialization();
                UpdateCardVisuals();
            }

            return targetSlot;
        }

        /// <summary>
        /// 指定スロットのカードを手札から出す（消費・プレイ）
        /// </summary>
        public int PlayCard(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= maxHandCount) return -1;
            if (handCardIds == null || handCardIds[slotIndex] == -1) return -1;
            if (!TakeOwnership()) return -1;

            int playedCardId = handCardIds[slotIndex];
            handCardIds[slotIndex] = -1;
            currentCardCount--;

            RequestSerialization();
            UpdateCardVisuals();

            return playedCardId;
        }

        /// <summary>
        /// 手札をすべてクリアする
        /// </summary>
        public void ClearHand()
        {
            if (!TakeOwnership()) return;

            ClearHandInternal();
            RequestSerialization();
            UpdateCardVisuals();
        }

        private void ClearHandInternal()
        {
            if (handCardIds == null) return;
            for (int i = 0; i < maxHandCount; i++)
            {
                handCardIds[i] = -1;
            }
            currentCardCount = 0;
        }

        /// <summary>
        /// 同期受信時のコールバック
        /// </summary>
        public override void OnDeserialization()
        {
            UpdateCardVisuals();
        }

        /// <summary>
        /// 手札の見た目・秘匿表示の更新
        /// 【重要】ローカルプレイヤーとトレイ所有者が一致する場合のみ表面を表示、それ以外は裏面表示
        /// </summary>
        public void UpdateCardVisuals()
        {
            if (cardMeshRenderers == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            bool isMe = (localPlayer != null && localPlayer.playerId == trayOwnerPlayerId);

            int slotCount = Mathf.Min(cardMeshRenderers.Length, maxHandCount);
            for (int i = 0; i < slotCount; i++)
            {
                MeshRenderer mr = cardMeshRenderers[i];
                if (mr == null) continue;

                int cardId = (handCardIds != null && i < handCardIds.Length) ? handCardIds[i] : -1;

                if (cardId == -1)
                {
                    mr.enabled = false;
                }
                else
                {
                    mr.enabled = true;

                    if (isMe)
                    {
                        if (cardFrontMaterial != null)
                        {
                            mr.material = cardFrontMaterial;
                        }
                    }
                    else
                    {
                        if (cardBackMaterial != null)
                        {
                            mr.material = cardBackMaterial;
                        }
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
        public int GetCurrentCardCount() => currentCardCount;
        public int GetTrayOwnerPlayerId() => trayOwnerPlayerId;
        public int GetCardIdAt(int slotIndex)
        {
            if (handCardIds == null || slotIndex < 0 || slotIndex >= maxHandCount) return -1;
            return handCardIds[slotIndex];
        }
    }
}
