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

            // 着席していない場合
            if (mySeat == -1)
            {
                // テーブル連動なし（サンドボックス単体操作）の場合は直接1枚引く
                if (deckManager != null)
                {
                    int drawn = deckManager.DrawCard();
                    Debug.Log($"[VRC-BoardGameKit] [3D Interact] 単体ドローを実行しました (Card ID: {drawn})");
                }
                else
                {
                    Debug.LogWarning("[VRC-BoardGameKit] [3D Interact] プレイエリアに参加していないためドローできません。右脇のキューブをクリックして参加してください。");
                }
                return;
            }

            // 着席している場合
            Debug.Log($"[VRC-BoardGameKit] [3D Interact] 山札をクリックしてドローを実行 (Seat: {mySeat})");

            // PersonalHandArea（円弧スロット空間）へのプールカード自動配備
            if (mySeatCtrl != null && mySeatCtrl.GetLinkedHandArea() != null)
            {
                PersonalHandArea handArea = mySeatCtrl.GetLinkedHandArea();
                CardSnapZone emptySlot = handArea.GetFirstEmptySlot();

                if (emptySlot != null)
                {
                    if (deckManager != null)
                    {
                        deckManager.DrawCardForZone(emptySlot);
                    }
                }
                else
                {
                    Debug.LogWarning($"[VRC-BoardGameKit] [3D Interact] 手札スロットが満杯のためドローできません: Seat {mySeat}");
                }
            }
        }
    }
}
