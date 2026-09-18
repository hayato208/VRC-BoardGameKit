using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace BoardGameKit.Core
{
    /// <summary>
    /// クリック・インタラクト判定を確実に検知・視覚化するデバッグ用ボタン。
    /// 3D Interact / UI OnClick の両方でログを出力し、マテリアル色を変化させる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class DebugClickButton : UdonSharpBehaviour
    {
        [Tooltip("クリック回数カウント")]
        private int clickCount = 0;

        private MeshRenderer meshRenderer;
        private Color[] debugColors = new Color[]
        {
            Color.red,
            Color.green,
            Color.blue,
            Color.yellow,
            Color.magenta,
            Color.cyan
        };

        private void Start()
        {
            meshRenderer = GetComponent<MeshRenderer>();
            Debug.Log("<color=#00FF00><b>[VRC-BoardGameKit]</b> [DebugClickButton] 初期化完了。クリック待機中...</color>");
        }

        /// <summary>
        /// 3D直接インタラクト（Eキー / 左クリック）
        /// </summary>
        public override void Interact()
        {
            HandleClick("3D Interact (Eキー / 左クリック)");
        }

        /// <summary>
        /// UI Button OnClick
        /// </summary>
        public void OnButtonClick()
        {
           // HandleClick("UI Button.onClick");
        }

        private void HandleClick(string source)
        {
            clickCount++;
            Debug.Log($"<color=#FF00FF><b>★★★ [DEBUG CLICK 検知] ★★★</b> ソース: [{source}] / クリック回数: {clickCount} 回目 / オブジェクト: {gameObject.name}</color>");

            // メッシュの色を変更して視覚的フィードバック
            if (meshRenderer != null)
            {
                Color nextColor = debugColors[clickCount % debugColors.Length];
                meshRenderer.material.color = nextColor;
            }
        }
    }
}
