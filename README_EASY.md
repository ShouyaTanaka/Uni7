# Uni7 Easy Guide

Uni7は、Unityのデバッグ、イベント作成、UI管理を便利にするパッケージです。

- **Diagnostics Console / UTLog**: ログを見る
- **Runtime Invoker**: Play Mode中にメソッドを実行する
- **Event Manager Generator**: イベント用コードを自動生成する
- **UI Workspace**: UIとButtonを登録・管理する

Runtime APIの名前空間は、ログとRuntime Invokerが`Uni7.Diagnostics`、イベントが`Uni7.Events`、UIが`Uni7.UI`です。

## 1. 最初にセットアップする

Unityメニューの **`Tools > Uni7 > Setup Manager`** を開きます。

1. **Run Full Setup** を押して、UniTask、UniRx、Addressablesを確認・導入する。
2. Addressablesを使う場合は、**Check / Create Addressables Settings** も確認する。

既存の設定やファイルは削除・上書きされません。

## 2. ログを出す

```csharp
using Uni7.Diagnostics;
using UnityEngine;

public class Sample : MonoBehaviour
{
    [SerializeField] private int currentHp = 100;
    [SerializeField] private int maxHp = 100;

    private void Start()
    {
        UTLog.Log("ゲーム開始");
        UTLog.Warning("警告メッセージ");
        UTLog.Error("エラーメッセージ");

        UTLog.LogTagged("Player", "プレイヤーを生成しました");
        UTLog.LogIf(transform.position.y < 0, "落下しました");
        UTLog.Log(transform.position, gameObject);
        UTLog.LogFormat("HP: {0}/{1}", currentHp, maxHp);
    }
}
```

例外を記録する場合:

```csharp
try
{
    // 処理
}
catch (System.Exception exception)
{
    UTLog.LogException(exception, this);
}
```

`UTLog`はUnity標準ログも初期状態で取り込み、UTLogのログはUnity標準Consoleにも出力します。

## 3. ログを見る

Unityメニューの **`Tools > Uni7 > Diagnostics > Console`** を開きます。

- Log / Warning / Errorの表示切り替え
- メッセージ、Tag、Stack Traceの検索
- Tagフィルター
- ログの選択、コピー、削除
- ダブルクリックで発生元スクリプトを開く
- ログの一時停止、自動スクロール、連続ログのまとめ表示
- **More** からExport / Import
- **More > Settings > Max entries** から保持数を変更

## 4. メソッドを画面から実行する

実行したいPublicメソッドに`[CanReflection]`を付けます。

```csharp
using Uni7.Diagnostics;
using UnityEngine;

public class PlayerDebug : MonoBehaviour
{
    [CanReflection("Player")]
    public void Heal(int amount)
    {
        UTLog.LogFormat("Heal: {0}", amount);
    }

    [CanReflection]
    public void ResetPlayer()
    {
        UTLog.Log("Player reset");
    }
}
```

Unityメニューの **`Tools > Uni7 > Diagnostics > Runtime Invoker`** を開きます。

1. Play Modeに入る
2. **Refresh** を押す
3. クラスとメソッドを選ぶ
4. 引数を入力する
5. **Invoke** を押す

`bool`、`string`、数値、`enum`、UnityのVector / Color / Rect / Bounds / Quaternion、`UnityEngine.Object`に対応しています。`ref`、`out`、`params`、generic method、optional parameterは対象外です。

## 5. 通常のクラスを登録する

`MonoBehaviour`ではないクラスは、`RuntimeRegistry.Register`で登録します。

```csharp
using Uni7.Diagnostics;

var service = new DebugService();
RuntimeRegistry.Register(service);
```

登録後、Runtime Invokerで**Refresh**を押してください。不要になった場合は`RuntimeRegistry.Unregister(service)`で解除できます。

## 6. イベントコードを作る

Unityメニューの **`Tools > Uni7 > Events > Generator`** を開きます。

1. Categoryを追加する（初回の生成時にはUIカテゴリとUI操作イベントも自動追加されます）
2. Event名とParameterを追加する
3. **Save + Generate** を押す

イベント定義は`Assets/Scripts/GenerateEvents/EventDefinitionDatabase.asset`に保存され、コードは同じフォルダ以下に生成されます。生成コードには購読用の`On...`、待機用の`Wait...`、発行用の`Emit...`があります。生成されたイベントは **`Tools > Uni7 > Events > Debugger`** からPlay Mode中に確認できます。

## 7. UIを登録して表示する

Unityメニューの **`Tools > Uni7 > UI > Generator`** を開きます。

- **UI**タブでWindow / Modalを登録する
- **Button**タブでボタンの動作を登録する
- **Settings**タブでPrefabや関連設定を確認する

AddressablesにPrefabを登録し、`UIRegistry`のAddressと一致させます。`UIRegistry`を`Assets/Resources/Uni7/UIRegistry.asset`に置くと、起動時にUIManagerとCanvasが自動生成されます。Sceneに手動配置することもできます。コードから次のように表示できます。

```csharp
using Uni7.UI;

await UIManager.Instance.OpenWindowAsync("Settings");
await UIManager.Instance.CloseUIAsync("Settings");
```

UI ID、初期プール数、最大プール数、アニメーションはUIの登録情報で設定できます。

## よく使う設定

```csharp
using Uni7.Diagnostics;

UTLogSettings.MaxLogEntries = 10000;
UTLogSettings.MirrorToUnityConsole = false;

UTLog.EnableUnityLogCapture();
UTLog.DisableUnityLogCapture();
```

`MaxLogEntries`を0以下にすると、ログ保持数が無制限になります。
