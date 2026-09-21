using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 山札（デッキ）の3Dオブジェクトにアタッチされるインタラクトコンポーネント。
    /// プレイヤーが山札に視線を合わせて「Useキー（左クリック/VRトリガー）」を押すと、
    /// 自分の座席の手札トレイ/円弧スロットにカードを1枚ドローする。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DeckInteractHandler : UdonSharpBehaviour
    {
        [Tooltip("山札マネージャーへの直接参照")]
        [SerializeField] private DeckManager deckManager;

        [Tooltip("各座席コントローラーへの参照")]
        [SerializeField] private SeatController[] seatControllers;

        public void SetDeckManager(DeckManager dm) => deckManager = dm;
        public void SetSeatControllers(SeatController[] sc) => seatControllers = sc;

        private void Start()
        {
            int count = (deckManager != null) ? deckManager.GetRemainingCount() : 0;
            UpdateInteractionText(count);
        }

        /// <summary>
        /// 山札の残数に応じてホバー時のツールチップテキストを動的に更新する (Zero-Traffic)
        /// </summary>
        public void UpdateInteractionText(int remainingCount)
        {
            if (remainingCount > 0)
            {
                this.InteractionText = $"カードを引く (残り: {remainingCount}枚)";
            }
            else
            {
                this.InteractionText = "山札なし (0枚)";
            }
        }

        public override void Interact()
        {
            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            // ローカルプレイヤーが座っている席番号を探す
            int mySeat = -1;
            SeatController mySeatCtrl = null;
            if (seatControllers != null)
            {
                for (int i = 0; i < seatControllers.Length; i++)
                {
                    if (seatControllers[i] != null && seatControllers[i].GetSeatedPlayerId() == localPlayer.playerId)
                    {
                        mySeat = i;
                        mySeatCtrl = seatControllers[i];
                        break;
                    }
                }
            }

            // 着席していない場合（未参加プレイヤーのドローを遮断）
            if (mySeat == -1 || mySeatCtrl == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [DeckInteractHandler] プレイエリアに参加（着席）していないためドローできません。座席右脇のキューブをクリックして参加してください。");
                return;
            }

            // 着席しているプレイヤーの手札エリアへドロー処理を一括委譲 (Tell, Don't Ask & SSOT)
            PersonalHandArea handArea = mySeatCtrl.GetLinkedHandArea();
            if (handArea != null && deckManager != null)
            {
                handArea.TryDrawCard(deckManager);
            }
            else
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [DeckInteractHandler] handArea または deckManager が未設定です: Seat {mySeat}");
            }
        }
    }
}
