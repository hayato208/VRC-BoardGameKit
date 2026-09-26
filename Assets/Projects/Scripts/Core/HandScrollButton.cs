using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// 手札トレイのページ送り（スクロール）を実行する3D物理ボタンコンポーネント。
    /// 物理スロット枠数を超える手札を所持している際、前後のページへ表示を切り替えます。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class HandScrollButton : UdonSharpBehaviour
    {
        [Tooltip("連動する手札エリアへの参照")]
        [SerializeField] private PersonalHandArea linkedHandArea;

        [Tooltip("trueなら次ページ（次へ ▶）、falseなら前ページ（◀ 前へ）")]
        [SerializeField] private bool isNext = true;

        public void SetLinkedHandArea(PersonalHandArea area) => linkedHandArea = area;
        public void SetIsNext(bool next) => isNext = next;

        public override void Interact()
        {
            if (linkedHandArea == null)
            {
                Debug.LogWarning("[VRC-BoardGameKit] [HandScrollButton] linkedHandArea が未設定です。");
                return;
            }

            if (isNext)
            {
                linkedHandArea.NextPage();
            }
            else
            {
                linkedHandArea.PrevPage();
            }
        }
    }
}
