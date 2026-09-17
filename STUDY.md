# STUDY.md (ユーザー学習・技術知見・数理ノート)

本ドキュメントは、**「VRChat向けボードゲーム開発における技術仕様・ネットワーク数理・アーキテクチャの根拠」**を深く理解し、知識の蓄積と再現性を担保するための学習・解説集です。
※プロジェクトの憲章・規約は `AGENTS.md`、概要・ロードマップは `README.md` を参照してください。

---

## 1. VRChat UdonSharp ネットワーク同期の基礎と数理

### ① Continuous Sync（連続同期）vs Manual Sync（手動同期）
*   **Continuous Sync**:
    *   毎フレーム（または高頻度）で位置や変数を自動補間して送受信する。
    *   車両やボールなどの物理挙動には向くが、データパケットが常にネットワーク帯域を消費する。
*   **Manual Sync (`[UdonSynced(UdonSyncMode.Manual)]`)**:
    *   変数を書き換えた後、明示的に `RequestSerialization()` を呼んだ時だけパケットが送信される。
    *   **カードゲームにおける最適解**:
        *   カードゲームは「カードを引く」「出す」「シャッフルする」といった離散的なイベント（ターン制）で盤面が変化するため、Manual Sync を採用することで**通信パケットを極小化（平時は通信量ほぼゼロ）**できる。

### ② 所有権（Ownership）と競合防止
*   **VRChatの分散ネットワーク原則**:
    *   オブジェクトに設定された同期変数は、**「そのオブジェクトのOwner（所有者）」**しか書き換えて送信することができない。
    *   Owner以外のプレイヤーが同期変数を書き換えても、他のプレイヤーには反映されず、次のシリアライズで上書きされて巻き戻る。
*   **解決のプロトコル**:
    1.  操作を行うプレイヤーが `Networking.SetOwner(localPlayer, targetObject);` を呼ぶ。
    2.  変数を更新する。
    3.  `RequestSerialization();` を呼び、全員に伝播させる。
    4.  受信側のクライアントは `OnDeserialization()` コールバックで画面表示を更新する。

---

## 2. カードゲームにおける「手札の秘匿化（不完全情報）」技術

### ① 手札が見えてはいけない理由と技術的課題
*   将棋やチェス（完全情報ゲーム）と異なり、大富豪・ポーカー・多くのTCGでは「自分の手札は自分にしか見えず、他者には裏面（または非表示）に見える」状態を作らなければ成立しない。
*   しかし、通常の3Dメッシュをそのまま同期して配置すると、他プレイヤーのVR視点から覗き見られてしまう。

### ② 解決策の比較検討
| 手法 | 仕組み | メリット | デメリット・注意点 |
| :--- | :--- | :--- | :--- |
| **A. 視点判定シェーダー<br>(Peeking Guard Shader)** | カメラ位置とカード法線の内積を計算し、正面から見ている時のみテクスチャを表示 | 手軽・どのプレイヤーでも視覚的に隠蔽可能 | VRで首を横から回り込まれると見えてしまうリスク |
| **B. ローカル表示制御<br>(Local View Filtering)** | `Networking.LocalPlayer` のIDと手札スロットの所有者IDを比較し、一致する場合のみ表面マテリアルを適用 | **完全な秘匿性**（他人の画面では最初から裏面テクスチャが描画される） | スクリプト側でマテリアルやUVの出し分け処理が必要 |
| **C. 専用手札トレイ（物理遮蔽）** | 物理的な覆い（カバー）のあるトレイにカードを格納 | 直感的・ギミック不要 | VR視点での覗き込みに弱い |

*   **本プロジェクトの採用方針**:
    *   **「B（ローカル表示制御）」を主軸**とし、補助として「A（視認角度制限）」を組み合わせることで、**100%覗き見不可能な安全設計**を実現する。

---

## 3. カードデータ構造と省メモリ・低トラフィック設計

### ① カードIDの整数（Integer）エンコーディング
*   カード1枚ごとに「スート（マーク）」「数字」「固有能力」を文字列や巨大な構造体で同期すると、通信帯域とU#メモリを無駄に圧迫する。
*   **整数1つ（int: 32bit）への情報圧縮**:
    *   カードID = `0 〜 53`（標準トランプの場合: `スート = ID / 13`, `ランク = (ID % 13) + 1`）
    *   山札の配列は `int[] deck = new int[54];` の1本だけで全カードの順序と状態を表現可能。

### ② フィッシャー–イェーツ（Fisher-Yates）シャッフルのU#最適実装
*   山札を偏りなく均等な確率でシャッフルするためのアルゴリズム。
*   計算量 $\mathcal{O}(N)$ で、U#のシングルスレッド環境でも負荷なく 0.1ms 未満で実行可能。
```csharp
// U#向け最適化シャッフル
for (int i = deck.Length - 1; i > 0; i--)
{
    int randomIndex = UnityEngine.Random.Range(0, i + 1);
    int temp = deck[i];
    deck[i] = deck[randomIndex];
    deck[randomIndex] = temp;
}
```

---

## 4. Local LLM連携における「省トークン設計」の数理

### ① なぜUnity MCP直接操作ではトークンが爆発するのか？
*   Unity MCPでシーン内の全Hierarchyや全コンポーネント構造をLLMに渡すと、1回のコンテキストが 30,000〜80,000 トークンに達する。
*   これでは数回のやり取りでAPI制限やLocal LLMのメモリ限界（VRAM消費）を迎え、精度も劣化する。

### ② イベントドリブン・プラグイン方式によるトークン削減効果
*   基盤（VRC-BoardGameKit）が「山札・手札・通信」を完全に隠蔽。
*   Local LLMには以下の**「最小限のルール定義インターフェース（約20〜40行）」**だけを入力・出力させる：

```csharp
public class CustomRulePlugin : UdonSharpBehaviour
{
    // カードが出された時の判定
    public bool CanPlayCard(int playerId, int cardId, int targetSlot) { ... }

    // カード効果の解決
    public void OnCardPlayed(int playerId, int cardId) { ... }

    // 勝利条件のチェック
    public int CheckWinner() { ... }
}
```

*   **効果**:
    *   プロンプトの入出力が **500〜1,000 トークン以内** に収まる。
    *   Local LLM（Qwen2.5-CoderやDeepSeek系）でもハルシネーションを起こさず、100%正確なU#ロジックを生成できる。

---

## 5. コアクラスの責務分割と同期シーケンス（2層アーキテクチャの実践）

### ① クラス責務の対応表（SOLID原則）
| クラス | 分類サフィックス | 責務（単一責任） | 同期モード |
| :--- | :--- | :--- | :--- |
| **`DeckManager`** | Manager | 山札・捨て札配列の保持、Fisher-Yatesシャッフル、ドロー・リセット同期 | Manual Sync |
| **`HandTrayController`** | Controller | トレイ上の手札スロット管理、ローカル視点判定による表面/裏面マテリアル切替 | Manual Sync |
| **`SeatController`** | Controller | `VRCStation` と連動した着席・離席検知、座席と手札トレイの所有権バインド | Manual Sync |
| **`TableManager`** | Manager | ゲーム全体の進行（手番、勝敗、フェーズ）、各コントローラーの統括 | Manual Sync |
| **`TableUIController`** | Controller / UI | 卓上・手元ボタン（Draw, Shuffle, Deal, Pass）からManagerへの安全な橋渡し | None (ローカル) |
| **`RulePluginBase`** | Plugin | オリジナルルール固有の判定（CanPlayCard, OnCardPlayed, CheckWinCondition） | None (ロジック委譲) |

### ② カードを引く（ドロー）時の同期シーケンス
```mermaid
sequenceDiagram
    actor Player as 着席プレイヤー (Seat 0)
    participant UI as TableUIController
    participant Table as TableManager
    participant Deck as DeckManager
    participant Tray as HandTrayController (Seat 0)

    Player->>UI: 「Draw」ボタンを押下
    UI->>Table: DrawCardForPlayer(0)
    Table->>Deck: DrawCard() [所有権取得 ➜ deckTopIndex減算]
    Deck-->>Table: 引いたカードID (例: 14)
    Table->>Tray: AddCard(14) [空きスロットに格納]
    Tray-->>Tray: UpdateCardVisuals() [本人視点: 表面マテリアル表示]
    Note over Deck,Tray: RequestSerialization() で全員に同期伝播
    Note over Tray: 他プレイヤーの画面では裏面マテリアルが描画される
```

---

## 6. Unity UI自動生成における「スケール逆数膨張（1250倍の罠）」の知見

### ① 現象のメカニズム
*   Unityにおいて、親オブジェクト（Canvasなど）の `localScale` が極小（例: `0.0008`）に設定されている状態で、新規作成した子オブジェクト（デフォルトで `worldScale = 1`）を以下のように追加すると発生する：
    ```csharp
    childObj.transform.SetParent(parentTransform); // worldPositionStays = true（デフォルト）
    ```
*   Unityは「子オブジェクトのワールド見た目サイズを維持しよう」と配慮するため、親のスケールで割った値（逆数）を `localScale` に自動設定する：
    $$\text{子オブジェクトの localScale} = \frac{1}{0.0008} = \mathbf{1250}$$
*   この結果、子要素（パネル、ボタン、文字）がすべて **1250倍の超巨大看板** としてレンダリングされてしまう。

### ② 正しい対策コード
*   UI生成時は必ず第二引数に `false`（ローカル座標系維持）を渡し、明示的に `localScale = Vector3.one` を指定する：
    ```csharp
    childObj.transform.SetParent(parentTransform, false); // ★ worldPositionStays を無効化
    childObj.transform.localScale = Vector3.one;           // ★ スケールを 1.0 に固定
    ```

---

## 7. VRChat World Space UI でボタンを確実に反応させる3大要件

### ① BoxCollider と VRCUiShape の不可分な関係
*   VRChatのレーザーポインター（ClientSim / VRコントローラー）は、物理的なコライダーを介してUI当たり判定を行います。
*   Canvasに `VRCUiShape` を追加するだけでは不十分で、**CanvasのRectTransformと同じサイズ（例: 50×10）の `BoxCollider`（`isTrigger = true`）を明示的にアタッチ** しないと、レーザーが完全にすり抜けてクリックできません。

### ② Udon VM へのイベント伝達（SendCustomEvent 必須原則）
*   Unity UI Buttonの `onClick` に直接 C# のデリゲートを登録すると、VRChat実行時にUdon VMへイベントが届かず無視されます。
*   必ず **`UdonBehaviour.SendCustomEvent (string)`** を `onClick` リスナーに登録することで、Udon仮想マシンが安全にメソッドを呼び出せます。

### ③ レイヤーと Navigation の干渉排除
*   Canvasのレイヤーは `UI` ではなく **`Default` レイヤー（0）** を使用します。
*   Buttonの `Navigation` を `None` に設定し、プレイヤーの移動キー入力（WASD / スティック）でボタン選択フォーカスが暴走するのを防ぎます。

---

## 8. 3D直接インタラクト (Udon Interact) と TextMeshPro (SDF) によるVRネイティブ設計

### ① TextMeshPro (SDF) がVRで必須である理由
*   Unity標準の `Text` (Legacy UI) はビットマップフォントであるため、Scaleが小さい環境（0.01等）ではサンプリング解像度が極端に低下し、文字がモザイク状に潰れてしまう。
*   **`TextMeshProUGUI` (TMP)** は **SDF (Signed Distance Field)** ベクター技術を採用しており、どれだけ縮小しても、VR視点でどれだけ至近距離から覗き込んでも輪郭が絶対に滲まず・潰れず、毛筆のようにシャープに描画される。

### ② 2Dキャンバスボタン vs 3D直接インタラクトのハイブリッド構成
*   **3D直接インタラクト（`UdonBehaviour.Interact()`）**:
    *   山札（`DeckObject`）に視線を合わせて「Useキー（左クリック/トリガー）」を押すと即座にドロー（`DeckInteractHandler`）。
    *   手札のカード（`CardSlot`）を直接クリックするとそのカードが場に出る（`CardSlotController`）。
    *   VRChatのホバーポップアップ（`interactText = "カードを引く (Draw)"`）が表示され、直感的で圧倒的な没入感を実現。
*   **卓上UIパネルとの両立**:
    *   手元で直接オモチャのように触る操作（3D）と、全員に配る・リセットするなどの進行操作（UIパネル）を綺麗に共存させる。

---

## 9. TextMeshPro における日本語フォントアセット（Noto Sans JP SDF）の自動運用

### ① デフォルトフォント（LiberationSans）の日本語欠落問題
*   TextMeshProに標準添付されている `LiberationSans SDF` は欧文フォントであり、日本語グリフ（ひらがな・カタカナ・漢字）が含まれていないため、日本語テキストが空白（または豆腐文字）になる。
*   日本語を正しく描画するには、Googleフォントの `Noto Sans JP` 等から生成された専用の **TMP_FontAsset（`.asset`）** を指定する必要がある。

### ② エディタスクリプトからの日本語フォント自動バインド
*   手動でInspectorにドラッグ＆ドロップする手間を省くため、`AssetDatabase.LoadAssetAtPath<TMP_FontAsset>` を用いて `Assets/Projects/Components/Fonts/NotoSansJP-Medium SDF.asset` を動的に取得・アタッチする。
*   これにより、テーブル自動生成時にすべてのボタンのテキストに日本語SDFフォントが100%自動適用され、ユーザーの手作業ゼロで美麗な日本語UIが即座に立ち上がる。

---

## 10. TextMeshPro スクリプト生成における font と fontSharedMaterial の分離バグ

### ① 現象のメカニズム
*   C#コードから `AddComponent<TextMeshProUGUI>()` を実行し、直後に `tmp.font = jpFont;` のみ代入すると、内部の `m_sharedMaterial`（フォントマテリアル）が自動更新されず、デフォルトの欧文マテリアル（または未設定）のまま残留する。
*   この結果、フォントアセット（NotoSansJP）のアトラス画像とマテリアルのシェーダー設定が乖離し、**Unity画面上でピンク色のマテリアルエラー（または文字の消失）** が発生する。

### ② Metafes2025 の実績設計に学ぶ解決法
*   元プロジェクト `Metafes2025` の `PlayerNameTexts` のYAMLシリアライズ構造を解析：
    ```yaml
    m_fontAsset: {fileID: 11400000, guid: c3e0f6a222f5ced40b7452227dd9d953, type: 2}
    m_sharedMaterial: {fileID: 1506394687846326273, guid: c3e0f6a222f5ced40b7452227dd9d953, type: 2}
    ```
*   コード側でも `font` のみならず **`fontSharedMaterial`** を明示代入することで、マテリアルエラーを100%遮断する：
    ```csharp
    tmp.font = jpFont;
    tmp.fontSharedMaterial = jpFont.material; // ★不可欠な同期処理
    ```

---

## 11. UnityのGUID参照メカニズムと `.meta` ファイル再生成の原則

### ① UnityにおけるGUIDの役割
*   Unityはファイルパスではなく、すべてのファイル・フォルダに付与される32桁の16進数文字列 **`guid`** をキーとしてアセット間の依存関係（マテリアル ⇄ シェーダー、プレハブ ⇄ スクリプト等）を内部管理している。
*   このGUIDは各アセットと同階層の `.meta` ファイル内にYAML形式で保存されている。

### ② プロジェクト間のファイル移植で起きるトラブル
*   別プロジェクトから単体ファイル（テクスチャ、フォント、モデル等）を移行する際に、旧プロジェクトの `.meta` をそのまま持ち込むと以下の問題が発生する：
    1. **Missing Reference (参照の幽霊化)**: 旧プロジェクト固有の環境設定や、移行先に存在しない外部アセットのGUIDを参照し続け、Inspector上で `Missing` やシェーダーのピンクエラー（マテリアル不整合）を引き起こす。
    2. **GUIDの重複・衝突**: 移行先で同じGUIDを持つ別のアセットが存在した場合、アセットデータベースのインデックスが破損するリスクがある。

### ③ `.meta` 再生成（作り直し）の運用ルール
*   **個別アセットの移植時**: 外部プロジェクトから持ち込むファイルは、`.meta` を削除した状態で移行先プロジェクトの `Assets/` 内に配置する。これにより、移行先Unityエディタのインポーターがその環境に合わせた最適な `.meta`（新規GUIDおよびインポーター設定）を安全に自動再生成する。
*   **パッケージ配布時（例外）**: `.unitypackage` や VPM (VRChat Package Manager) を通じた配布時は、パッケージ内の相互参照（Prefabが参照するスクリプトやマテリアル）を維持するため、同一パッケージ内の `.meta` は一括して管理・保持する。

---

## 12. 卓上UIの視認性パラメータ（実機インスペクタ最適値）とPrefab化

### ① 実機視認性に基づくUIパラメータ
*   卓上のボタンUI（World Space Canvas）は、着席時のプレイヤー目線（高さ約1.2m〜1.4m）から見下ろす形で自然に操作できるように調整された：
    *   **Scale**: `(0.02, 0.02, 0.02)`（微小サイズによる潰れを防ぎ、ボタン文字が明瞭に視認できる絶妙なスケール）
    *   **LocalPosition**: `(0, 1.0f, -0.25f)`（テーブル面 0.7m より少し上、プレイヤー寄りにチルト配置）
    *   **LocalRotation**: `Quaternion.Euler(35f, 0, 0)`（見下ろし角35度で光の反射や視野角を最適化）
    *   **Collider Size**: `(50, 10, 1)`（CanvasのRectTransformと完全一致させ、Raycast判定を確保）

### ② シーン調整からパッケージPrefabへの保存パイプライン
*   Unityエディタのシーン上で微調整した結果をワンクリックでパッケージ資産（`Assets/Projects/Prefabs/`）に昇格させるため、`CardTableBuilder` に `[Tools] -> [VRC-BoardGameKit] -> [Save Current Table to Prefabs]` を新設。
*   これにより、コード生成ロジックと実機Prefabの両輪で最新のインスペクタ状態を永続化できる。

---

## 13. VRCStation 連動における着席インタラクトと PlayerMobility 設計

### ① VRCStation 単体アタッチ時の「着席不能」落とし穴
*   `GameObject.CreatePrimitive(PrimitiveType.Cube)` などで生成したオブジェクトに `VRCStation` コンポーネントを追加しただけでは、VRChat/ClientSim実行時にプレイヤーがクリック（Useキー）しても自動で着席しない。
*   **解決プロトコル**:
    *   `SeatController`（UdonSharp）に **`public override void Interact()`** を実装し、その内部で **`Networking.LocalPlayer.UseAttachedStation()`** を明示的に呼び出す。
    *   これにより、VRChatのレイザー/視線ホバーで「座る (Sit)」と表示され、クリックで確実に着席ステート（`OnStationEntered`）へ移行できる。

### ② PlayerMobility の最適値（Immobilize の必然性）
*   **`PlayerMobility.Immobilize` の採用理由**:
    *   `Mobile` にすると着席判定（所有権）を持ったままプレイヤーが遠くへ歩いていけてしまい、手札トレイや卓上UIとの位置不整合・同期ズレを引き起こす。
    *   `Immobilize` に設定することで、着席中はプレイヤーの移動入力を座席に固定し、手札トレイの正面で安定してゲームをプレイできる。
    *   離席は `disableStationExit = false` の設定により、ジャンプ（Spaceキー / VRジャンプボタン）を押すだけでいつでも自然に立ち上がることができる。

---

## 14. エディタスクリプトにおける UdonSharpBehaviour のアタッチとシリアライズ同期の鉄則

### ① AddComponent<T> では UdonBehaviour が正しく構築されない問題
*   Unity標準の `gameObject.AddComponent<T>()` を用いて UdonSharpBehaviour 派生クラスを追加した場合、UdonSharp 1.x の内部コンパイラフックが機能せず、`UdonBehaviour` が生成されないか、`UdonSharpProgramAsset` の割り当てが不完全になる。
*   **正式なAPI**:
    *   **`UdonSharpEditor.UdonSharpEditorUtility.AddUdonSharpComponent<T>(gameObject)`** を使用する。
    *   これにより、`UdonBehaviour` の追加、ProgramAsset のバインド、C# プロキシコンポーネントの初期化が一括で安全に行われる。

### ② CopyProxyToUdon による Udonヒープ変数の同期
*   C# のプロキシコンポーネントのフィールド（`tableManager`, `seatControllers` など）に値を設定しただけでは、UdonBehaviour 内部のシリアライズストレージ（Udon仮想マシンの変数テーブル）に値が反映されない。
*   プロパティ設定後に必ず **`UdonSharpEditorUtility.CopyProxyToUdon(proxyComponent)`** を呼び出すことで、エディタでの設定値が UdonBehaviour の実行時メモリに 100% 確実に同期される。

### ③ UdonSharpProgramAsset（.asset）の存在保証と自動生成
*   外部スクリプト（`.cs`）を新規作成した際、Unityプロジェクト内に対応する `UdonSharpProgramAsset`（`.asset` ファイル）が存在しない状態で `AddUdonSharpComponent` を呼ぶと、「`Program asset on XXX is not valid`」というエラーが発生する。
*   エディタ自動生成スクリプト内で `ScriptableObject.CreateInstance<UdonSharpProgramAsset>()` を用いて対応する `.asset` を自動生成し、`UdonSharpCompilerV1.CompileSync()` で同期コンパイルを行うことで、エラーを 100% 根絶できる。

---

## 15. パッケージ化・Prefabファースト設計によるアタッチ完全解決の数理と構造

### ① なぜUnityパッケージはコード生成ではなく「Prefab」を配布するのか？
*   Unityにおいて、動的にコードから `AddComponent` を連打してインスペクタ配線を行う方式は、アセンブリのリロード順序、UdonSharpのプロキシ内部キャッシュ、シリアライズ順序によって壊れやすい。
*   **Prefab（`.prefab`）の数学的・静的構造**:
    *   Prefabは全GameObject・コンポーネント間の参照関係を **GUID（アセット識別子）** と **FileID（オブジェクト識別子）** によるグラフ構造として完全にシリアライズ（YAML化）した静的データである。
    *   一度Unityエディタ上で正常に配線された状態で保存されたPrefabは、ドラッグ＆ドロップまたは `PrefabUtility.InstantiatePrefab` を行うだけで、コード実行なしに 100% 確実にすべての参照（TableManager ⇄ SeatController ⇄ UI ⇄ Button OnClick）が最初から繋がった状態で復元される。

### ② VRChat / VPM パッケージングのベストプラクティス
*   VRChatの市販ギミック（QvPen, UdonChips, 各種ワールドアセット）はすべてこの **「シリアライズ済みPrefab配布方式」** を採用している。
*   本キットにおいても、`CardTable_4Players.prefab` を中心としたPrefabファースト設計を採用することで、ユーザーがシーンに配置するだけで即座に完動する堅牢な基盤を実現する。

---

## 16. UdonSharp 1.x のエディタ拡張 API 構造（AddUdonSharpComponent & ProgramAsset）

### ① `AddUdonSharpComponent` の正確な API 仕様
*   UdonSharp 1.x (VRCSDK 3.x) では、`UdonSharpEditorUtility.AddUdonSharpComponent` ではなく、**`UdonSharpEditor.UdonSharpComponentExtensions` に定義された拡張メソッド `gameObject.AddUdonSharpComponent<T>()`** を使用する。
*   `using UdonSharpEditor;` をインポートした上で `gameObject.AddUdonSharpComponent<T>()` を呼び出すことで、GameObject に `UdonSharpBehaviour` のプロキシと実体 `UdonBehaviour` を一括生成・バインドできる。

### ② ProgramAsset のキャッシュリセット API
*   UdonSharp 1.x では `UdonSharpProgramAsset.ClearProgramAssetCache()` や `UdonSharpEditorUtility.ResetCaches()`（internal）ではなく、公開メソッド **`UdonSharpEditorUtility.ResetAssemblyCaches()`** を使用する。
---

## 17. World Space UI における VRCUiShape・UIButtonHandler による確実なイベントルーティング

### ① UnityEvent の PersistentListener と VRChat の不整合問題
*   Unity標準の `Button.onClick.AddPersistentListener(udon.SendCustomEvent, ...)` は、Prefab保存時やインスタンス化時に参照解決が破綻しやすく、ClientSim / VRChat 内でボタンを押しても `SendCustomEvent` が発火しないトラブルが多発する。
*   さらに、Canvas 全面に手動で `BoxCollider` を置くと、`VRCUiShape` の自動レイキャスト判定と競合してボタンの `raycastTarget` が遮断される。

### ② UIButtonHandler（Udonネイティブ）による二重トリガー解決
*   各ボタンオブジェクト自身に個別コライダーと `UIButtonHandler`（UdonSharp）を付与する。
*   ボタンクリック（`Button.onClick`）と 3D直接インタラクト（`Interact()`）の両方を `UIButtonHandler` が受け取り、`targetUI.SendCustomEvent(customEventName)` を確実に実行する設計により、PC・VRの全環境で 100% 確実に動作する。

---

## 19. VRCStation依存の脱却と非固定型プレイエリア連動アーキテクチャ

### ① なぜVRCStationではなく「非拘束クリック連動」が必要なのか？
*   **VRCStationの限界とUX課題**:
    *   従来の `VRCStation` はアバターの移動能力を停止（`PlayerMobility = Immobilize`）させ、固定位置・固定姿勢に縛る。
    *   ボードゲームやカードゲームにおいて、プレイヤーは「立って見渡す」「手元をのぞき込む」「歩き回って相手の表情を見る」といった自由な移動・ポーズ調整を行いたい場面が多い。
    *   Stationによる拘束は、VRプレイヤーにとって視点移動の制限や閉塞感を生み、デスクトッププレイヤーにとっても操作感を損ねる要因となる。

### ② 非Station型座席管理（SeatController）の論理連動設計
*   **物理拘束から論理登録へのシフト**:
    *   プレイヤーの移動や姿勢は一切固定せず、座席オブジェクトへの `Interact()` を通じて「その座席（プレイエリア）の担当者（所有者）」としての登録・解除をトグル管理する。
    *   **状態遷移**:
        1.  **空席（Vacant: `seatedPlayerId == -1`）**:
            *   誰でもクリックして「参加（Join）」可能。
            *   クリックしたローカルプレイヤーがオブジェクトの所有権（Ownership）を取得し、`seatedPlayerId = localPlayer.playerId` をセットして `RequestSerialization()`。
            *   手札トレイの所有権割り当て (`linkedHandTray.AssignOwner(...)`) と `TableManager.OnPlayerSeated(...)` を実行。
        2.  **参加中（Occupied by Local: `seatedPlayerId == localPlayer.playerId`）**:
            *   自分が参加中の席を再度クリックすると「離席（Leave）」となる。
            *   所有権を取得し `seatedPlayerId = -1` を同期。手札トレイの解放 (`ReleaseOwner`) と `TableManager.OnPlayerLeftSeat(...)` を実行。
        3.  **他人が使用中（Occupied by Other）**:
            *   他のプレイヤーが着席中の席はクリックしても重複参加を防止。
    *   **プレイヤー退出（OnPlayerLeft）の安全解放**:
        *   参加中のプレイヤーが途中でインスタンスを抜けた場合、Masterクライアントが検知して自動的に席を空席（`-1`）に解放し、デッドロックを防止。
    *   **視覚フィードバックと動的テキスト**:
        *   席の状態（空席/参加中/他者使用中）に応じて、`InteractionText` およびマテリアル色を即座に動的更新。


