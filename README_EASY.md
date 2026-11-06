# Uni7 Easy Guide

Uni7は、Unityのデバッグを便利にする機能をまとめたパッケージです。

- **UTLog**: ログを記録し、専用ウィンドウで確認する
- **Runtime Invoker**: Play Mode中にメソッドを画面から実行する

Runtime APIの名前空間は `Uni7.Diagnostics` です。

## 1. ログを出す

```csharp
using Uni7.Diagnostics;
using UnityEngine;

public class Sample : MonoBehaviour
{
    private void Start()
    {
        UTLog.Log("ゲーム開始");
        UTLog.Warning("警告メッセージ");
        UTLog.Error("エラーメッセージ");

        UTLog.LogTagged("Player", "プレイヤーを生成しました");
        UTLog.LogIf(transform.position.y < 0, "落下しました");
    }
}
```

値やGameObjectも記録できます。

```csharp
UTLog.Log(transform.position);
UTLog.Log("Player", gameObject);
UTLog.LogFormat("HP: {0}/{1}", currentHp, maxHp);
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

## 2. ログを見る

Unityメニューから **`Tools > Uni7 > Diagnostics Console`** を開きます。

主な機能:

- Log / Warning / Errorの表示切り替え
- メッセージやTagの検索
- ログの選択、コピー、削除
- ダブルクリックで発生元スクリプトを開く
- ログの一時停止、自動スクロール、連続ログのまとめ表示
- **More** からテキスト形式でExport / Import

UTLogのログは、初期状態ではUnity標準Consoleにも出力されます。

## 3. メソッドを画面から実行する

実行したいPublicメソッドに `[CanReflection]` を付けます。

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

Unityメニューから **`Tools > Developer Toolkit > Runtime Invoker`** を開きます。

1. Play Modeに入る
2. **Refresh** を押す
3. クラスとメソッドを選ぶ
4. 引数を入力する
5. **Invoke** を押す

検索欄では、クラス名・メソッド名・Categoryを検索できます。

### 対応している主な引数

`bool`、`string`、数値、`enum`、`Vector2/3/4`、`Color`、`Rect`、`Bounds`、`Quaternion`、`UnityEngine.Object`

`ref`、`out`、`params`、generic method、optional parameterは対象外です。

## 4. 通常のクラスをRuntime Invokerに登録する

`MonoBehaviour`ではないクラスは、`RuntimeRegistry.Register`で登録します。

```csharp
using Uni7.Diagnostics;

public class DebugService
{
    public void Test(int value)
    {
    }
}

// Play Mode中の初期化処理などで
var service = new DebugService();
RuntimeRegistry.Register(service);
```

登録後、Runtime Invokerで **Refresh** を押してください。
不要になった場合は `RuntimeRegistry.Unregister(service)` で解除できます。

## よく使う設定

```csharp
using Uni7.Diagnostics;

UTLogSettings.MaxLogEntries = 10000;
UTLogSettings.MirrorToUnityConsole = false;

UTLog.EnableUnityLogCapture();
UTLog.DisableUnityLogCapture();
```

`MaxLogEntries`を `0` 以下にすると、ログ保持数が無制限になります。
