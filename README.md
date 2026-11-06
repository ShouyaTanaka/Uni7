# Uni7

Unity向けの診断機能をまとめたパッケージです。

- **UTLog**: ログの記録・検索・選択・書き出しを行う診断コンソール
- **Runtime Invoker**: Play Mode中に属性付きメソッドを検索して実行するEditorWindow

すべてのRuntime APIは `Uni7.Diagnostics` 名前空間にあります。

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

        // object / UnityEngine.Object / context
        UTLog.Log(hp);
        UTLog.Log(transform.position, transform);
        UTLog.Log("Player spawned", gameObject);

        // 書式付きログ
        UTLog.LogFormat("HP: {0}/{1}", hp, 100);
        UTLog.WarningFormat("HP: {0}", hp);
        UTLog.ErrorFormat("HP: {0}", hp);

        // Tag付きログ
        UTLog.LogTagged("Player", "Spawned");
        UTLog.WarningTagged("Player", "HP is low");
        UTLog.ErrorTagged("Player", "Invalid state");

        // 条件付きログ（conditionがtrueのときだけ記録）
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

`UTLog.Log`、`Warning`、`Error`はログを記録した直後に `LogBuilder` を返します。
既存コードとの互換性のため、次のチェーンAPIも利用できます。新規コードではTagやContextを直接指定できるAPIを推奨します。

```csharp
UTLog.Log("Player spawned").Tag("Player").Context(gameObject);
```

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

`LogException(null)`はエラーログとして記録されます。書式文字列に不正な引数がある場合は、例外を投げずにメッセージへエラー内容を付加します。

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

`UTLog`は自動初期化され、Unity標準ログも初期状態では取り込みます。
また、UTLogで記録したログは初期状態ではUnity標準Consoleにも出力されます。

## Diagnostics Console

Unityメニューの **`Tools > Uni7 > Diagnostics Console`** から開きます。

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
- **More**からログのテキスト書き出し・取り込み
- **More > Settings > Max entries**から保持上限を1,000 / 10,000 / 50,000 / 無制限に変更

書き出しはUTF-8の `.txt` ファイルです。取り込んだ各行はTag `Imported` のログとして追加されます。

## Runtime Invoker

Play Mode中に、`[CanReflection]` を付けたPublicメソッドを検索して実行します。
デバッグ用メソッドをInspectorや一時的なコードから呼び出す代わりに、引数を入力して実行できます。

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

属性のルール:

- クラスに `[CanReflection]` を付けると、そのクラスの対応するPublicメソッドが対象になります。
- メソッドに `[CanReflection]` を付けると、クラス属性がなくてもそのメソッドだけが対象になります。
- メソッド属性のCategoryはクラス属性より優先されます。
- Categoryを省略した場合は `General` になります。

### 通常のC#クラスを登録する

`MonoBehaviour`以外のインスタンスは、自動ではSceneから検索されません。Play Mode中に登録してください。

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

`RuntimeRegistry.Register`は同じインスタンスを重複登録せず、`Unregister`、`Clear`も利用できます。
登録・解除後はRuntime Invokerで **Refresh** を押してください。

### ウィンドウの使い方

1. Unityメニューの **`Tools > Developer Toolkit > Runtime Invoker`** を開く。
2. Play Modeに入る。
3. **Refresh** を押してメソッドと対象インスタンスを検出する。
4. Namespace、クラス、Category、メソッド名で検索する。
5. クラスとメソッドを選択し、引数を入力して **Invoke** を押す。

Refreshは手動操作時だけ検出処理を行います。検索・選択・再描画ではReflectionスキャンを行いません。
Play Modeを終了した後や対象インスタンスが変わった後は、再度Play Modeに入りRefreshしてください。

### 対応する引数

- `bool` / `string` / `char`
- `int` / `long` / `float` / `double`
- `enum`
- `Vector2` / `Vector3` / `Vector4`
- `Vector2Int` / `Vector3Int`
- `Color` / `Rect` / `Bounds` / `Quaternion`
- `UnityEngine.Object`派生型（Scene Objectを指定可能）

次のメソッドは対象外です。

- generic method
- `ref` / `out` / `params`
- ポインタ型・ポインタを返すメソッド
- optional parameter
- 未対応の引数型
- 特殊メソッド・コンストラクター・型引数を含むメソッド

戻り値はInvoke後にステータスバーへ表示されます。`Task` / `UniTask`などの非同期処理を自動でawaitする機能はありません。

### 検出範囲と負荷

- 属性付き候補の取得にはUnityの`TypeCache`を使用します。
- Scene上の`MonoBehaviour`は、非アクティブなObjectも含めてRefresh時に一度だけ検索します。
- `RuntimeRegistry`に登録された通常クラスのインスタンスもRefresh時に取得します。
- static methodはインスタンスなしで実行できます。
- メソッド情報の構築と対象検索はRefresh時だけ行います。
