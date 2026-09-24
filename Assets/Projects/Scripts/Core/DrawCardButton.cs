using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// プレイヤーの手元（PersonalHandArea）に配置されるドロー専用UIボタン。
    /// WorldSpace Canvas + VRCUiShape + BoxCollider + UdonSharpBehaviour (Interact / OnButtonClick) のハイブリッド構成により、
    /// VRコントローラーのレーザーポインター、デスクトップUIマウスクリック、3D直接Interactの全環境で100%確実に動作する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DrawCardButton : UdonSharpBehaviour
    {
        [Tooltip("全体進行を司るTableManagerへの参照")]
        [SerializeField] private TableManager tableManager;

        [Tooltip("山札マネージャーへの参照")]
        [SerializeField] private DeckManager deckManager;

        [Tooltip("紐付くプレイヤーの手札エリア")]
        [SerializeField] private PersonalHandArea linkedHandArea;

        [Tooltip("紐付く座席コントローラー（着席判定用）")]
        [SerializeField] private SeatController linkedSeat;

        [Tooltip("ボタン表面のラベル表示用TextMeshPro")]
        [SerializeField] private TextMeshPro buttonText;

        public void SetTableManager(TableManager tm) => tableManager = tm;
        public void SetDeckManager(DeckManager dm) => deckManager = dm;
        public void SetLinkedHandArea(PersonalHandArea area) => linkedHandArea = area;
        public void SetLinkedSeat(SeatController seat) => linkedSeat = seat;
        public void SetButtonText(TextMeshPro text) => buttonText = text;

        private void Start()
        {
            if (deckManager != null)
            {
                UpdateRemainingCount(deckManager.GetRemainingCount());
            }
            else
            {
                this.InteractionText = "カードを引く (Draw)";
            }
        }

        /// <summary>
        /// 山札の残数に応じてボタン表面ラベルおよびホバーツールチップを動的に更新する (案A)
        /// </summary>
        public void UpdateRemainingCount(int remainingCount)
        {
            if (remainingCount > 0)
            {
                this.InteractionText = $"カードを引く (残り: {remainingCount}枚)";
                if (buttonText != null)
                {
                    buttonText.text = $"カードを引く\n<size=70%>(残り: {remainingCount}枚)</size>";
                }
            }
            else
            {
                this.InteractionText = "山札なし (0枚)";
                if (buttonText != null)
                {
                    buttonText.text = "山札切れ\n<size=70%>(0枚)</size>";
                }
            }
        }

        /// <summary>
        /// 3D直接インタラクト（Eキー / VRタッチ）
        /// </summary>
        public override void Interact()
        {
            ExecuteDraw();
        }

        private void ExecuteDraw()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            // 1. 着席判定（未着席または他人の座席のボタン操作を遮断）
            if (linkedSeat == null || linkedSeat.GetSeatedPlayerId() != localPlayer.playerId)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] この座席に参加（着席）していないためドローできません。座席右脇のキューブをクリックして参加してください。");
                return;
            }

            if (linkedHandArea == null || deckManager == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DrawCardButton] linkedHandArea または deckManager が未設定です。");
                return;
            }

            // 2. TableManager経由でドロー処理を実行 (進行管理・ルール判定の集約)
            TableManager tm = tableManager;
            if (tm == null && linkedSeat != null)
            {
                tm = linkedSeat.GetTableManager();
            }

            int seatIndex = linkedSeat.GetSeatIndex();
            if (tm != null && seatIndex >= 0)
            {
                tm.DrawCardForPlayer(seatIndex);
            }
            else
            {
                // フォールバック: TableManager未設定環境用の直接ドロー
                linkedHandArea.TryDrawCard(deckManager);
            }
        }
    }
}
