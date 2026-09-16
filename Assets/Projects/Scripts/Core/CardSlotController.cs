using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 手札の各カードオブジェクトにアタッチされるインタラクトコンポーネント。
    /// プレイヤーがカードに視線を合わせて「Useキー（左クリック/VRトリガー）」を押すと、
    /// そのカードを場に出す（プレイする）ことができる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class CardSlotController : UdonSharpBehaviour
    {
        [Tooltip("このカードのスロット番号（0〜最大手札数-1）")]
        public int slotIndex = 0;

        [Tooltip("所属する手札トレイへの参照")]
        public HandTrayController handTray;

        [Tooltip("全体進行を司るTableManagerへの参照")]
        public TableManager tableManager;

        [Tooltip("このトレイが紐付く座席番号")]
        public int seatIndex = 0;

        public override void Interact()
        {
            if (handTray == null || tableManager == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            // 防護ガード: 自分の手札トレイでなければ操作できない
            if (handTray.GetTrayOwnerPlayerId() != localPlayer.playerId)
            {
                return;
            }

            // カードが存在するかチェック
            int cardId = handTray.GetCardIdAt(slotIndex);
            if (cardId == -1) return;

            // TableManager経由でカードを場に出す
            tableManager.PlayCard(seatIndex, slotIndex);
        }
    }
}
