using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 山札（デッキ）の3Dオブジェクトにアタッチされるインタラクトコンポーネント。
    /// プレイヤーが山札に視線を合わせて「Useキー（左クリック/VRトリガー）」を押すと、
    /// 自分の座席の手札トレイにカードを1枚ドローできる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DeckInteractHandler : UdonSharpBehaviour
    {
        [Tooltip("全体進行を司るTableManagerへの参照")]
        public TableManager tableManager;

        [Tooltip("各座席コントローラーへの参照")]
        public SeatController[] seatControllers;

        public override void Interact()
        {
            if (tableManager == null || seatControllers == null) return;

            VRCPlayerApi localPlayer = Networking.LocalPlayer;
            if (localPlayer == null) return;

            // ローカルプレイヤーが座っている席番号を探す
            int mySeat = -1;
            for (int i = 0; i < seatControllers.Length; i++)
            {
                if (seatControllers[i] != null && seatControllers[i].GetSeatedPlayerId() == localPlayer.playerId)
                {
                    mySeat = i;
                    break;
                }
            }

            // 着席していればカードを1枚引く
            if (mySeat != -1)
            {
                tableManager.DrawCardForPlayer(mySeat);
            }
        }
    }
}
