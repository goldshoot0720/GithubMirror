# UI ↔ 服務層 交接契約

> UI 層（Views / ViewModels / Assets）已完成。
> 服務層只要實作本文件列出的三個介面，UI **一行都不用改**。

---

## 1. 唯一的接點

`Services/IGitService.cs` 定義了全部契約。UI 只透過 `AppServices` 取用服務：

```csharp
public sealed class AppServices
{
    public required IGitServiceProvider Git { get; init; }
    public required ICredentialStore    Credentials { get; init; }
    public required IMirrorRunner       Mirror { get; init; }
}
```

接上真實服務層的方式 —— 只改 `Program.cs` 這一段：

```csharp
App.Services = new AppServices
{
    Git         = new RealGitServiceProvider(),   // ← 服務層提供
    Credentials = new EncryptedCredentialStore(), // ← 服務層提供
    Mirror      = new GitMirrorRunner()           // ← 服務層提供
};
```

目前預設是 `AppServices.CreateSample()`（離線假資料），讓 UI 可以先跑起來看流程。

---

## 2. 要實作的三個介面

### 2.1 `IGitServiceProvider`

```csharp
IGitService Create(string platform);   // platform 為 PlatformIds 的常數
bool IsSupported(string platform);
```

### 2.2 `IGitService`（七個平台各一份）

| 成員 | 說明 |
|---|---|
| `string Platform` | 回傳 `PlatformIds.*` 其中之一 |
| `ProbeAsync` | 驗證 Token；**成功時必須回填 `ResolvedUsername`**（UI 靠這個省掉「輸入使用者名稱」欄位）。失敗請回 `ProbeResult.Fail("看得懂的中文訊息")` |
| `ListRepositoriesAsync` | 列出所有 repo（要處理分頁）。**`SizeInBytes` 請填 bytes**，平台沒提供就留 `null`，UI 會顯示「—」 |
| `BuildSourceUrlAsync` | 回傳含帳密的 https clone URL |
| `EnsureTargetAsync` | 目標 repo 不存在就建立，回傳含帳密的 push URL |

`RepositoryInfo` 至少要填：`Name / Owner / CloneUrl / WebUrl / Description / SizeInBytes /
Stars / IsPrivate / LastUpdated / Language / DefaultBranch`。
`AccountId / AccountDisplay / SourcePlatform` 由 UI 自動補，服務層不用管。

### 2.3 `ICredentialStore`

```csharp
Task<List<AccountCredential>> LoadAsync(CancellationToken ct);
Task SaveAsync(IEnumerable<AccountCredential> accounts, CancellationToken ct);
```

需求：存到 `%AppData%/GithubMirror/`，Token 加密（Windows 用 DPAPI，其他平台退回 AES + 本機金鑰檔）。
需要 `System.Security.Cryptography.ProtectedData` 套件（csproj 已註解好，取消註解即可）。

### 2.4 `IMirrorRunner`

```csharp
Task RunAsync(string sourceUrlWithAuth, string targetUrlWithAuth,
              IProgress<string>? log, IProgress<double>? percent, CancellationToken ct);
```

`Services/GitHelper.cs` **已經寫好可直接用**，包含：

* `clone --mirror` → 清掉 `refs/pull/*`、`refs/merge-requests/*` 等唯讀 ref → `push --mirror`
* `GIT_TERMINAL_PROMPT=0`（避免跳出帳密視窗把 UI 卡死）
* 逐行串流 stdout/stderr 到 `IProgress<string>`
* URL 內的密碼自動遮成 `***`
* 暫存目錄自動清理（含 Windows 唯讀屬性處理）

包成 `IMirrorRunner` 只要：

```csharp
public sealed class GitMirrorRunner : IMirrorRunner
{
    public Task RunAsync(string s, string t, IProgress<string>? log,
                         IProgress<double>? pct, CancellationToken ct)
        => GitHelper.MirrorAsync(s, t, log, pct, ct);
}
```

---

## 3. 各平台的欄位對應

`AccountCredential` 的欄位在不同平台代表的意義：

| 平台 | Username | Token | ApiUrl | Organization | Project | Region | GitHttps* |
|---|---|---|---|---|---|---|---|
| GitHub | 帳號（可自動偵測） | PAT | GHE 位址（選填） | 建立目標用的 org | — | — | — |
| GitLab | 帳號（可自動偵測） | PAT | 自架位址（選填） | namespace id | — | — | — |
| Bitbucket | 帳號（可自動偵測） | App Password | — | workspace | — | — | — |
| Codeberg | 帳號（可自動偵測） | Token | — | org | — | — | — |
| Gitea | 帳號（可自動偵測） | Token | **必填** | org | — | — | — |
| AWS CodeCommit | Access Key ID | Secret Access Key | — | — | — | **必填** | **推送用憑證** |
| Azure Repos | 選填 | PAT | — | **必填 org** | project（選填） | — | — |

`Models/PlatformIds.cs` 的 `PlatformDescriptor` 已描述好每個平台要顯示哪些欄位、
標題文字、Token 前綴（用來自動判斷平台）。**新增平台只要動這個檔 + 服務層實作**，UI 會自動長出對應欄位。

---

## 4. 已備好的基礎工具（可直接用或替換）

* `Services/RestClient.cs` —— `Rest.Get/PostJson/UseBearer/UseBasic/SendJsonAsync`，
  已把 401/403/404/409/422 轉成中文訊息，另有 `Rest.Str/Long/Int/Bool/Date/StringList` 讀 JSON 的小工具。
* `Services/GitHelper.cs` —— 見上。

---

## 5. 舊檔處理

`GithubMirror.csproj` 底部有一段 `<ItemGroup>` 把舊版半成品排除在編譯之外：

```
Models/Models.cs, Services/CloudServices.cs, Services/GitHubService.cs,
Services/GitLabService.cs, Services/OtherPlatformServices.cs,
ViewModels/MainViewModel.cs, Views/AddAccountWindow.*, Views/MirrorDialog.*
```

**建議服務層新檔寫在 `Services/Platforms/` 底下**（例如 `Services/Platforms/GitHubService.cs`），
避免和被排除的舊檔名撞名。確認新版沒問題後，把舊檔實體刪掉、再把那個 ItemGroup 移除即可。
