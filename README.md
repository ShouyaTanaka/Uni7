# Uni7

Uni7は、Unityプロジェクトの診断、イベントコード生成、UI管理を支援するパッケージです。

- **Diagnostics Console / UTLog**: ログの記録・収集・検索・書き出し
- **Runtime Invoker**: Play Mode中に属性付きメソッドを検索して実行
- **Event Manager Generator**: イベント定義から型付きC#コードを生成
- **UI Workspace / UI runtime**: Window・Modal・Buttonの登録、Addressables経由の表示、プール管理
- **Setup Manager**: プロジェクト用フォルダ、外部Package、Addressables設定の確認

Runtime APIの名前空間は機能ごとに異なります。

- 診断: `Uni7.Diagnostics`
- イベント: `Uni7.Events`
- UI: `Uni7.UI`

## セットアップ

Unityメニューの **`Tools > Uni7 > Setup Manager`** を開きます。

1. **Create / Repair Project Folders** でプロジェクト用フォルダを作成する。
2. **Check & Install Dependencies** でUniTask、UniRx、Addressablesを確認・導入する。
3. Addressablesを使用する場合は、**Check / Create Addressables Settings** で設定を確認する。
4. まとめて実行する場合は **Run Full Setup** を使用する。

既存のフォルダ、ファイル、Addressables Settingsは削除・上書きされません。

## UTLog

### 基本的な使い方

```csharp
using Uni7.Diagnostics;
using UnityEngine;

public sealed class Player : MonoBehaviour
{
    [SerializeField] private int hp = 100;

    private void Start()
    {
        UTLog.Log("Game Started");
        UTLog.Warning("Low HP");
        UTLog.Error("Initialization failed");

        UTLog.Log(hp);
        UTLog.Log(transform.position, transform);
        UTLog.Log("Player spawned", gameObject);

        UTLog.LogFormat("HP: {0}/{1}", hp, 100);
        UTLog.WarningFormat("HP: {0}", hp);
        UTLog.ErrorFormat("HP: {0}", hp);

        UTLog.LogTagged("Player", "Spawned");
        UTLog.WarningTagged("Player", "HP is low");
        UTLog.ErrorTagged("Player", "Invalid state");

        UTLog.LogIf(hp <= 0, "Player is dead");
        UTLog.WarningIf(hp < 20, "HP is low");
        UTLog.ErrorIf(hp < 0, "HP is invalid");

        try
        {
            throw new System.Exception("Example exception");
        }
        catch (System.Exception exception)
        {
            UTLog.LogException(exception, this);
        }
    }
}
```

`Log`、`Warning`、`Error`は記録直後に`LogBuilder`を返します。既存コードとの互換性のため、次のチェーンAPIも利用できます。

```csharp
UTLog.Log("Player spawned").Tag("Player").Context(gameObject);
```

新規コードでは、TagやContextを直接指定できる`LogTagged`やContext付きの`Log`などを推奨します。

### API

#### 記録

- `UTLog.Log(object message)`
- `UTLog.Log(object message, UnityEngine.Object context)`
- `UTLog.Warning(object message)`
- `UTLog.Warning(object message, UnityEngine.Object context)`
- `UTLog.Error(object message)`
- `UTLog.Error(object message, UnityEngine.Object context)`
- `UTLog.LogTagged(string tag, object message)`
- `UTLog.WarningTagged(string tag, object message)`
- `UTLog.ErrorTagged(string tag, object message)`
- `UTLog.LogFormat(string format, params object[] args)`
- `UTLog.WarningFormat(string format, params object[] args)`
- `UTLog.ErrorFormat(string format, params object[] args)`
- `UTLog.LogIf(bool condition, object message)`
- `UTLog.WarningIf(bool condition, object message)`
- `UTLog.ErrorIf(bool condition, object message)`
- `UTLog.LogException(Exception exception, UnityEngine.Object context = null)`
- `UTLog.LogObject(object value, string tag = "Object")`

`LogException(null)`はnull例外を受け取ったことをエラーログとして記録します。書式文字列に不正な引数がある場合は例外を投げず、メッセージに`FormatException`の内容を付加します。

#### 履歴・収集設定

```csharp
UTLog.Clear();
UTLog.RemoveLogs(ids);
IReadOnlyList<LogEntry> logs = UTLog.GetAllLogs();

UTLog.EnableUnityLogCapture();
UTLog.DisableUnityLogCapture();
UTLogSettings.MaxLogEntries = 50000; // 0以下で無制限
UTLogSettings.MirrorToUnityConsole = false;
```

UTLogは自動初期化され、Unity標準ログの取り込みは初期状態で有効です。また、UTLogで記録したログは初期状態でUnity標準Consoleにも出力されます。

## Diagnostics Console

Unityメニューの **`Tools > Uni7 > Diagnostics > Console`** から開きます。

- Log / Warning / Errorの表示切り替えと件数表示
- メッセージ・Tag・Stack Traceのテキスト検索
- Tagフィルター
- `Ctrl` / `Cmd` + クリックで複数選択
- `Shift` + クリックで範囲選択
- `Ctrl` / `Cmd` + `A` で表示中ログを全選択
- `Ctrl` / `Cmd` + `C` で選択ログをコピー
- `Delete` / `Backspace` で選択ログを削除
- ダブルクリックで記録元スクリプトを開く
- 右クリックメニューからコピー・削除・表示中ログの全選択・全削除
- **Collapse**: 同じType・Tag・Messageの連続ログを集約表示
- **Auto**: 新しいログに合わせて末尾へ自動スクロール
- **Pause**: ウィンドウの再描画だけを一時停止（ログ収集は継続）
- **Details**: 選択ログの詳細パネルを表示
- 詳細パネルの境界をドラッグして幅を変更
- **More > Export / Save all logs as text...** で書き出し
- **More > Import / Import text log...** で取り込み
- **More > Settings > Max entries** で保持上限を1,000 / 10,000 / 50,000 / 無制限に変更

書き出しはUTF-8の`.txt`ファイルです。取り込んだ各行はTag `Imported` のログとして追加されます。

## Runtime Invoker

Play Mode中に、`[CanReflection]`を付けたPublicメソッドを検索して実行します。

### 対象メソッドの公開

```csharp
using Uni7.Diagnostics;
using UnityEngine;

[CanReflection("Player")]
public sealed class PlayerController : MonoBehaviour
{
    public void Heal(int amount)
    {
        UTLog.LogTagged("Player", "Heal: " + amount);
    }

    public void ResetPlayer()
    {
        UTLog.Log("Reset Player");
    }

    [CanReflection("Debug")]
    public void PrintPosition(Vector3 offset)
    {
        UTLog.Log(transform.position + offset);
    }
}
```

- クラスに`[CanReflection]`を付けると、そのクラスの対応するPublicメソッドが対象になります。
- メソッドに`[CanReflection]`を付けると、クラス属性がなくてもそのメソッドだけが対象になります。
- メソッド属性のCategoryはクラス属性より優先されます。
- Categoryを省略した場合は`General`になります。

Unityメニューの **`Tools > Uni7 > Diagnostics > Runtime Invoker`** を開き、Play Modeに入ってから**Refresh**を押します。Namespace、クラス、Category、メソッド名で検索し、対象と引数を指定して**Invoke**します。

Refreshは手動操作時だけReflectionによる検出を行います。検索・選択・再描画では再スキャンしません。Play Modeを終了した後や対象インスタンスが変わった後は、再度Refreshしてください。

### 通常のC#クラスを登録する

`MonoBehaviour`以外のインスタンスはSceneから自動検索されません。Play Mode中に登録してください。

```csharp
using Uni7.Diagnostics;

public sealed class CombatService
{
    public void AddDamage(int value)
    {
    }
}

public sealed class GameBootstrap
{
    private CombatService combatService;

    public void Initialize()
    {
        combatService = new CombatService();
        RuntimeRegistry.Register(combatService);
    }

    public void Shutdown()
    {
        RuntimeRegistry.Unregister(combatService);
    }
}
```

`RuntimeRegistry.Register`は同じインスタンスを重複登録しません。`Unregister`と`Clear`も利用できます。登録・解除後はRuntime Invokerで**Refresh**を押してください。

### 対応する引数

- `bool` / `string` / `char`
- `int` / `long` / `float` / `double`
- `enum`
- `Vector2` / `Vector3` / `Vector4`
- `Vector2Int` / `Vector3Int`
- `Color` / `Rect` / `Bounds` / `Quaternion`
- `UnityEngine.Object`派生型（Scene Objectを指定可能）

generic method、`ref` / `out` / `params`、ポインタ型、optional parameter、未対応の引数型、特殊メソッド、コンストラクター、型引数を含むメソッドは対象外です。戻り値はInvoke後にステータスバーへ表示されます。`Task` / `UniTask`などを自動でawaitする機能はありません。

## Event Manager Generator

イベント定義から、カテゴリごとのイベントクラスと`EventManager`を生成します。生成されたイベントには、購読用の`On...`、1回だけ待機する`Wait...`、発行用の`Emit...`が用意されます。

1. Unityメニューの **`Tools > Uni7 > Events > Generator`** を開く。
2. Categoryを追加する（初回の生成時にはUIカテゴリとUI操作イベントも自動追加されます）。
3. Event名とParameter（`None` / `Int` / `Float` / `String` / `Bool` / `Vector2` / `Vector3` / `GameObject`）を追加する。
4. **Save + Generate** を押す。

定義は`Assets/Scripts/GenerateEvents/EventDefinitionDatabase.asset`に保存され、コードは`Assets/Scripts/GenerateEvents/`以下に生成されます。生成ファイルは手動編集せず、Generatorから再生成してください。

生成後は、プロジェクト側で次のようにイベントを発行できます。

```csharp
EventManager.UI.EmitOpenWindow("Settings");
```

購読や1回だけの待機もできます。

```csharp
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

IDisposable subscription = EventManager.UI.OnOpenWindow(id => Debug.Log(id));
UniTask<string> nextWindow = EventManager.UI.WaitOpenWindow();
```

実際のカテゴリ名・イベント名はプロジェクトで登録した定義に置き換えてください。生成コードは`Cysharp.Threading.Tasks`と`UniRx`を使用します。

**`Tools > Uni7 > Events > Debugger`** のEvent Debuggerでは、生成済みのイベントを選択してPlay Mode中に発行できます。

## UI WorkspaceとUI runtime

### Editorで登録する

Unityメニューの **`Tools > Uni7 > UI > Generator`** を開きます。

- **UI**: Window / ModalのID、Prefab、プール数、アニメーションを管理
- **Button**: Button IDとOpen / Close / Emit EventなどのActionを管理
- **Settings**: UIとButtonのPrefab生成先や関連設定を管理

UIは`UIRegistry`、Buttonは`UIButtonRegistry`として保存されます。Prefabは自動で上書きされません。UIのロードにはAddressablesを使用するため、PrefabをAddressablesに登録し、RegistryのAddressを一致させてください。

### Runtimeから開閉する

`UIRegistry`を`Assets/Resources/Uni7/UIRegistry.asset`に作成すると、`UIRuntimeBootstrap`が起動時にCanvas、Window root、Modal root、`UIManager`を自動生成します。Sceneに手動配置する場合は、`UIManager`にRegistry、Window root、Modal rootを設定してください。`UIWindow`または`UIModal`を継承したPrefabをRegistryに登録してください。

```csharp
using Cysharp.Threading.Tasks;
using Uni7.UI;

public async UniTask ShowSettings()
{
    await UIManager.Instance.OpenWindowAsync("Settings");
}

public async UniTask CloseSettings()
{
    await UIManager.Instance.CloseUIAsync("Settings");
}
```

UI IDでWindow / Modalを開閉でき、`PreloadAsync`、`CloseTopModalAsync`、`CloseAllWindowsAsync`、`CloseAllAsync`、`ReleasePoolAsync`も利用できます。Addressablesのロード、アニメーション、初期プール、最大プール数はUIEntryの設定に従います。

ButtonやコードからUIイベントを発行する場合は、`UIEventHub.PublishOpenWindow`、`PublishOpenModal`、`PublishCloseUI`、`PublishCloseTopModal`、`PublishCloseAll`を使用できます。
