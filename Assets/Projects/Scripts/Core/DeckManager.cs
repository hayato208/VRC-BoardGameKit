using UdonSharp;
using UnityEngine;
using TMPro;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 山札（デッキ）と捨て札（ディスカード）の同期管理クラス。
    /// Manual Syncを採用し、状態変更時のみネットワークパケットを発行する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class DeckManager : UdonSharpBehaviour
    {
        [Header("Deck Settings")]
        [Tooltip("デッキに含まれる総カード枚数（デフォルト54枚: トランプ52枚+Joker2枚）")]
        [SerializeField] private int defaultCardCount = 54; // 【★】ゲームに応じて変更可能

        [Header("Visual Feedback")]
        [Tooltip("山札の3Dオブジェクト（残数0枚時の非表示制御用）")]
        [SerializeField] private Transform deckMeshTransform;

        [Tooltip("山札のインタラクトハンドラー（ツールチップInteractionText連動用）")]
        public DeckInteractHandler interactHandler;

        [Header("Card Object Pool")]
        [Tooltip("山札が管理するカード実体配列（オブジェクトプール）")]
        public CardController[] cardPool;

        // --- 同期変数 ---
        // 山札のカードID配列（インデックス 0 〜 deckTopIndex-1 が山札に残っているカード）
        [UdonSynced]
        private int[] deckCards;

        // 現在の山札の残り枚数（末尾インデックス）
        [UdonSynced]
        private int deckTopIndex = 0;

        // 捨て札のカードID配列
        [UdonSynced]
        private int[] discardCards;

        // 捨て札の総数
        [UdonSynced]
        private int discardCount = 0;

        // 初期化完了フラグ
        [UdonSynced]
        private bool isInitialized = false;

        private void Start()
        {
            // Owner（Master）のみ初期化を実行
            if (Networking.IsOwner(gameObject) && !isInitialized)
            {
                InitializeDeck(defaultCardCount);
            }
        }

        /// <summary>
        /// デッキの初期生成（0〜totalCards-1の連番IDを生成してシャッフル）
        /// </summary>
        public void InitializeDeck(int totalCards)
        {
            if (!TakeOwnership()) return;

            deckCards = new int[totalCards];
            discardCards = new int[totalCards];
            discardCount = 0;

            for (int i = 0; i < totalCards; i++)
            {
                deckCards[i] = i;
            }

            deckTopIndex = totalCards;
            ShuffleInternal();

            // プール内の全カードを山札位置へ整然と回収・初期化
            if (cardPool != null)
            {
                Vector3 deckPos = transform.position;
                Quaternion deckRot = transform.rotation;
                for (int i = 0; i < cardPool.Length; i++)
                {
                    if (cardPool[i] != null)
                    {
                        cardPool[i].cardId = i;
                        cardPool[i].ResetToDeck(deckPos, deckRot);
                    }
                }
            }

            isInitialized = true;
            RequestSerialization();
            UpdateVisuals();
        }

        /// <summary>
        /// 外部呼び出し用シャッフル（オーナー権限を取得して実行）
        /// </summary>
        public void ShuffleDeck()
        {
            if (deckTopIndex <= 0)
            {
                Debug.LogWarning("[VRC-BoardGameKit] 山札が空のためシャッフルできません。");
                return;
            }
            if (!TakeOwnership()) return;

            ShuffleInternal();
            RequestSerialization();
            Debug.Log($"[VRC-BoardGameKit] 山札をシャッフルしました。（残り: {deckTopIndex}枚）");
        }

        /// <summary>
        /// Fisher-Yatesアルゴリズムによる山札シャッフル（内部計算）
        /// </summary>
        private void ShuffleInternal()
        {
            if (deckCards == null || deckTopIndex <= 1) return;

            for (int i = deckTopIndex - 1; i > 0; i--)
            {
                int randomIndex = Random.Range(0, i + 1);
                int temp = deckCards[i];
                deckCards[i] = deckCards[randomIndex];
                deckCards[randomIndex] = temp;
            }
        }

        /// <summary>
        /// 山札から1枚引く（ドロー）
        /// </summary>
        /// <returns>引いたカードID（山札切れの場合は -1）</returns>
        public int DrawCard()
        {
            if (deckCards == null || !isInitialized)
            {
                InitializeDeck(defaultCardCount);
            }

            // 防護ガード: 山札切れ
            if (deckTopIndex <= 0)
            {
                Debug.LogWarning("[VRC-BoardGameKit] 山札が切れています（残り0枚）。");
                return -1;
            }

            if (!TakeOwnership()) return -1;

            deckTopIndex--;
            int drawnCardId = deckCards[deckTopIndex];

            RequestSerialization();
            UpdateVisuals();

            Debug.Log($"[VRC-BoardGameKit] カードを引きました: Card ID {drawnCardId} （山札残り: {deckTopIndex}枚）");
            return drawnCardId;
        }

        /// <summary>
        /// 指定されたスナップ枠（手元スロットなど）へカードプールから1枚配る（ドロー）
        /// </summary>
        /// <param name="targetZone">配備先のスナップ枠</param>
        /// <returns>配備したカードID（山札切れや満杯の場合は -1）</returns>
        public int DrawCardForZone(CardSnapZone targetZone)
        {
            if (targetZone == null || targetZone.IsOccupied()) return -1;

            int drawnCardId = DrawCard();
            if (drawnCardId == -1) return -1;

            if (cardPool != null && drawnCardId >= 0 && drawnCardId < cardPool.Length)
            {
                CardController card = cardPool[drawnCardId];
                if (card != null)
                {
                    targetZone.TrySnap(card);
                }
            }

            return drawnCardId;
        }

        /// <summary>
        /// カードを捨て札に追加する
        /// </summary>
        public void DiscardCard(int cardId)
        {
            if (cardId < 0) return;
            if (!TakeOwnership()) return;

            if (discardCards == null || discardCount >= discardCards.Length)
            {
                return;
            }

            discardCards[discardCount] = cardId;
            discardCount++;

            RequestSerialization();
            UpdateVisuals();
            Debug.Log($"[VRC-BoardGameKit] カードを捨て札に追加しました: Card ID {cardId} （捨て札計: {discardCount}枚）");
        }

        /// <summary>
        /// 捨て札を山札に戻して再シャッフル（リセット）
        /// </summary>
        public void ResetAndReshuffleDeck()
        {
            if (!TakeOwnership()) return;

            int totalActive = deckTopIndex + discardCount;
            if (totalActive <= 0) return;

            for (int i = 0; i < discardCount; i++)
            {
                deckCards[deckTopIndex + i] = discardCards[i];
            }

            deckTopIndex = totalActive;
            discardCount = 0;

            // プール内の全カードを山札へ回収・初期化
            if (cardPool != null)
            {
                Vector3 deckPos = transform.position;
                Quaternion deckRot = transform.rotation;
                for (int i = 0; i < cardPool.Length; i++)
                {
                    if (cardPool[i] != null)
                    {
                        cardPool[i].ResetToDeck(deckPos, deckRot);
                    }
                }
            }

            ShuffleInternal();
            RequestSerialization();
            UpdateVisuals();
            Debug.Log($"[VRC-BoardGameKit] 山札と捨て札をリセット・再シャッフルしました。（山札計: {deckTopIndex}枚）");
        }

        /// <summary>
        /// 他クライアントからの同期受信時コールバック
        /// </summary>
        public override void OnDeserialization()
        {
            UpdateVisuals();
        }

        /// <summary>
        /// 山札の見た目（厚みや残数テキストなど）を更新する内部処理
        /// </summary>
        private void UpdateVisuals()
        {
            if (deckMeshTransform != null)
            {
                // 山札が0枚のときはメッシュを非表示、残数があれば表示（縦横比は維持）
                deckMeshTransform.gameObject.SetActive(deckTopIndex > 0 || !isInitialized);
            }

            if (interactHandler == null)
            {
                interactHandler = GetComponentInChildren<DeckInteractHandler>();
            }

            if (interactHandler != null)
            {
                int count = isInitialized ? deckTopIndex : defaultCardCount;
                interactHandler.UpdateInteractionText(count);
            }
        }

        /// <summary>
        /// 所有権の取得（防護ガード）
        /// </summary>
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
        public int GetRemainingCount() => isInitialized ? deckTopIndex : defaultCardCount;
        public int GetDefaultCardCount() => defaultCardCount;
        public int GetDiscardCount() => discardCount;
        public bool IsDeckEmpty() => isInitialized && deckTopIndex <= 0;
    }
}
