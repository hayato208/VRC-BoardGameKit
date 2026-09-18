# UdonSharp実装規約 (docs/UdonSharp実装規約.md)

本ドキュメントは、VRChat向け汎用ボードゲーム・カードゲーム制作パッケージ「VRC-BoardGameKit」における **UdonSharp (U#) 特有のコーディング規程・インタラクション設計・ネットワーク同期プロトコル** を定めた現場レベルの実装規範です。

AI（Antigravity / LLM）および人間の開発者は、コードの作成・修正時に本規約を厳格に遵守しなければなりません。

---

## 1. インタラクション設計の鉄則（C#・一般Unityとの決定的差異）

一般のUnity（uGUI）開発とVRChatワールド開発における最大の罠は「UIボタンの取り扱い」にあります。VRChat環境における操作系は以下の原則を絶対規範とします。

### 1.1 Unity標準 `UnityEngine.UI.Button`（uGUI）の原則禁止
* **禁止理由**:
  * World Space Canvasにおける `Button.onClick`、`GraphicRaycaster`、`EventSystem`、`VRCUiShape` の組み合わせは、Prefab保存時のターゲット参照欠落や、Canvas全面コライダーによるRaycast遮断を引き起こす最大の温床となります。
* **標準実装方針**:
  * 卓上パネルやボタンを実装する際は、uGUIの `Button` コンポーネントを全廃し、**「薄型Cube（板ポリゴン）＋ Collider ＋ UdonSharpBehaviour (Interact)」による3D物理ボタン構造** を採用してください。

### 1.2 すべての入力は `public override void Interact()` に集約する
* 椅子、山札、手札スロット、卓上ボタン、スイッチなどのあらゆる操作トリガーは、対象オブジェクト自身に `Collider`（Trigger可）と `UdonSharpBehaviour` を付与し、`Interact()` をオーバーライドして実装します。
* これにより、PC（マウス左クリック / Eキー）および VR（コントローラー直接タップ / レーザーTrigger）の全環境で100%確実に動作します。

### 1.3 `UdonBehaviour.interactText` の設定義務付け
* プレイヤーが視線（レティクル）やコントローラーのポインターを重ねた際に、画面中央のHUDに操作案内（例: `座る (Sit)`, `カードを引く (Draw)`, `配る (Deal)`）が表示されるよう、すべてのインタラクト可能オブジェクトに必ず `UdonBehaviour.interactText` を設定してください。

---

## 2. ネットワーク同期プロトコル（Manual Sync標準）

### 2.1 Manual Sync とオーナーシップの徹底
* 同期モードは `[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]` を基本とします。
* 同期変数を変更する際は、必ず **「オーナー権限の取得 ➜ 変数の変更 ➜ `RequestSerialization()`」** の順序を厳守してください。

```csharp
if (!Networking.IsOwner(gameObject))
{
    Networking.SetOwner(Networking.LocalPlayer, gameObject);
}
// 同期変数の変更
isToggled = true;
RequestSerialization();
```

### 2.2 配列の遅延初期化と防護プログラミング（Late Joiner対策）
* 配列データ（山札、手札スロット等）は、ネットワーク同期の受信順序や途中参加者（Late Joiner）によって `null` の状態でメソッドが呼ばれる可能性があります。
* メソッドの先頭で必ず `null` チェックを行い、未初期化時は自動で配列を生成するガード節を設けてください。

```csharp
if (handCardIds == null)
{
    InitializeHandSlots();
}
```

---

## 3. TextMeshPro (TMP) とフォント管理

### 3.1 日本語フォント（NotoSansJP）の標準アタッチ義務付け
* プロジェクト内で使用するすべての TextMeshPro（3Dテキスト、UIテキスト問わず）には、**標準で `NotoSansJP-Medium SDF` をアタッチすること** を義務付けます。
  * フォントアセットパス: `Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset`
* スクリプトやエディタ拡張からテキストを生成する際も、必ず上記アセットとそのマテリアル（`jpFont.material`）を `font` および `fontSharedMaterial` に明示代入してください。

### 3.2 SDFグリフ欠落対策とバイリンガル表記
* 事前テクスチャベイク型のSDFフォントアセットでは、未収録の難解な漢字が四角（□）で文字化けするリスクがあります。
* 重要なステータス表示やボタンラベルには、確実に収録されている文字セット（ひらがな・基本漢字・英数）を採用し、視認性向上のため英語併記（例: `配る (Deal)`, `山札: 54 / すて札: 0`）を行ってください。

---

## 4. 言語・構文制約（UdonSharp / C#）

### 4.1 LINQおよびラムダ式の禁止
* UdonSharpの仮想マシン制限により、`System.Linq`（`Where`, `Select`, `OrderBy` 等）は使用できません。
* 配列の探索・ソート・カウントは古典的な `for` または `foreach` ループで実装してください。

### 4.2 継承より委譲（Composition）の徹底
* 仕様変更への耐久性を高め、UdonSharpの型解決エラーを防ぐため、クラスの多重継承を避け、コンポーネントの委譲（参照バインド）によって機能を組み合わせます。

---

## 5. Prefab運用とエディタ自動生成コードの同期

### 5.1 ユーザーによる微調整（Overrides / Apply All）
* VR空間における見やすさ・手の届きやすさは、人間（ユーザー）がシーン上で調整するのが最も高精度です。
* ユーザーが位置・角度を調整した後は、Inspector の `[Overrides] -> [Apply All]` でPrefabに保存します。

### 5.2 エディタ自動生成コードへのTransform逆反映
* PrefabのTransformが更新された際、AIは即座にその数値を検知し、エディタ生成コード（`CardTableBuilder.cs` 等）の初期座標値へ逆反映して固定化します。
* これにより、「Prefab配置」でも「コード再構築」でも、常に同一のベスト配置が再現されます。

### 5.3 【再発防止】全階層Transformスキャンの義務（子要素の抽出漏れ根絶）
* ユーザーによるシーン・Prefab調整をコードに逆同期する際、親オブジェクト（HandTrayやDeck）のみを部分的に抽出することは厳禁とします。
* 必ず `[Tools] -> [VRC-BoardGameKit] -> [Dump Table Hierarchy Transforms]` または再帰走査スクリプトを用い、**子要素（`CardSlot_0`〜`CardSlot_4` のカード本体メッシュ等）や孫要素を含めた全階層のTransform（Position, Rotation, Scale）を網羅的に走査・照合** してコードへ固定化しなければなりません。

### 5.4 Prefabアセットおよび.metaのGit追跡義務
* `Assets/Projects/Prefabs/` 配下のすべてのプレハブファイル（`.prefab`）および対応する `.meta` は、常にGitのバージョン管理対象に含めます。
* シーン調整やPrefab更新を行った際は、コード修正と同時にPrefabも必ずGitコミットを行い、いつでも以前の配置状態を差分比較・ロールバックできるようにします。

