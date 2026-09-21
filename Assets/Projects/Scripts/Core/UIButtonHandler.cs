using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// VRChat World Space UI上のボタンクリック/インタラクトを
    /// TableUIControllerへ100%確実に中継するハンドラー。
    /// PCのマウスクリック、VRコントローラーのレーザー、直接Interactのすべてに対応。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UIButtonHandler : UdonSharpBehaviour
    {
        [Tooltip("呼び出し先のTableUIController")]
        [SerializeField] private TableUIController targetUI;

        [Tooltip("実行するイベント名 (OnClickDealButton, OnClickDrawButton 等)")]
        [SerializeField] private string customEventName;

        public void SetTargetUI(TableUIController ui) => targetUI = ui;
        public void SetCustomEventName(string eventName) => customEventName = eventName;

        /// <summary>
        /// 3D直接インタラクトまたはUIクリック時に発火
        /// </summary>
        public override void Interact()
        {
            ExecuteAction();
        }

        /// <summary>
        /// Unity UI (Button.onClick) からも呼べるエントリーポイント
        /// </summary>
        public void OnButtonClick()
        {
            ExecuteAction();
        }

        private void ExecuteAction()
        {
            if (targetUI != null && !string.IsNullOrEmpty(customEventName))
            {
                Debug.Log($"<color=#00FFFF>[VRC-BoardGameKit] [UIButtonHandler] ボタン押下: {gameObject.name} -> {customEventName}</color>");
                targetUI.SendCustomEvent(customEventName);
            }
            else
            {
                Debug.LogWarning($"[VRC-BoardGameKit] [UIButtonHandler] targetUI または customEventName が未設定です: {gameObject.name}");
            }
        }
    }
}
