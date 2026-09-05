# GithubMirror — UI 設計說明

設計目標：**步驟最少、零彈窗、圖示化。**

---

## 一、操作流程（從零到完成鏡像＝ 4 個動作）

```
①  貼上 Token          → 自動判斷平台、自動抓帳號名稱、自動列出全部專案
②  勾選要鏡像的專案      → 表格第一欄勾選，或按「全選」
③  勾選目標帳號（可複選） → 右側面板
④  按「開始鏡像 N × M」   → 底部即時顯示每一條任務的進度
```

沒有任何一步需要開新視窗。第二次開啟程式時 ①③ 都會被記住，只剩 ②④。

### 為什麼可以少一步
* **Token 前綴自動辨識平台**：`ghp_` / `github_pat_` → GitHub，`glpat-` → GitLab。
* **自動抓使用者名稱**：GitHub / GitLab / Bitbucket / Codeberg / Gitea 只要 Token，
  服務層的 `ProbeAsync` 會回填帳號名稱，使用者不用自己打。
* **加完帳號立刻載入專案**，不用再按一次「重新整理」。
* 只有 Gitea（伺服器位址）、Azure（organization）、AWS（Access Key + 區域）
  才會多長出必填欄位 —— 由 `PlatformDescriptor` 驅動，其餘欄位收在「進階設定」摺疊區。

---

## 二、版面

```
┌─────────────────────────────────────────────────────────────────────────┐
│ ▣ GithubMirror            [🔍 搜尋專案…]                    ⟳ 重新整理    │
├─────────────────────────────────────────────────────────────────────────┤
│ ⓘ 狀態訊息列（載入中會顯示進度條，可關閉）                                  │
├──────────────┬──────────────────────────────────────┬───────────────────┤
│ 👤 帳號       │ ▣全選 ✕清除  ▽Public▾   📁128 🗄4.2GB ✓已選3          │ ➜ 鏡像到       │
│ ──────────── │ ──────────────────────────────────── │ ───────────────── │
│ 全 全部帳號 12│ ☑ 🔒 web-portal      🗄12.4MB ‹›C# ★48 🕐2d           │ ☑ GL work        │
│ GH alice   8 │ ☐ 🌐 docs-site       🗄 2.1MB ‹›MD ★ 3 🕐1w           │ ☐ BB team        │
│ GL work    4 │ ☑ 🔒 api-gateway     🗄88.0MB ‹›Go ★12 🕐3h           │ ☐ CB personal    │
│ BB team   —  │                                      │ 名稱規則 {name}    │
│              │                                      │ → web-portal      │
│ ＋ 新增帳號   │                                      │ ☑ 🔒 私有維持私有  │
│  （內嵌表單） │                                      │ ┌───────────────┐ │
│              │                                      │ │ ▶ 開始鏡像 3×1 │ │
│              │                                      │ └───────────────┘ │
├──────────────┴──────────────────────────────────────┴───────────────────┤
│ ▤ 鏡像任務                                              🗑 清除已完成      │
│ ✓ GH→GL web-portal   ▓▓▓▓▓▓▓▓▓▓ 鏡像完成          完成 100%              │
│ ⟳ GH→BB api-gateway  ▓▓▓▓▓░░░░░ Receiving objects  進行中 62%            │
└─────────────────────────────────────────────────────────────────────────┘
```

* **左欄**：帳號清單。每列 = 平台色徽章 + 名稱 + 專案數，滑過出現刪除鈕。
  第一列是「全部帳號」，可一次看所有平台的專案。新增帳號是**內嵌表單**，不開視窗。
* **中欄**：專案表格。每欄都有圖示；可點欄位標題排序；預設依大小由大到小。
* **右欄**：鏡像目標（可複選 → 一個專案可同時推到多個平台）、命名規則（即時預覽）、選項。
* **底欄**：任務清單，有任務時才出現；滑鼠停留可看完整 git 日誌。

---

## 三、圖示

`Assets/Icons.axaml` 收錄 **31 個自繪 24×24 向量圖示**（全部自己畫，不含任何品牌商標）：

| 分類 | 圖示 |
|---|---|
| 動作 | Plus, Close, Check, Refresh, Play, Stop, Trash, Pencil, Search, Filter, ArrowRight, ChevronDown, ChevronRight, External, Copy, SelectAll |
| 狀態 | CheckCircle, AlertCircle, Clock, Info, Pause |
| 資訊 | Lock, Globe, Star, Fork, Database, Code, Branch, Account, Key, Server, Cloud, Folder, Eye, EyeOff, Settings, Layers |

**平台識別採「色票徽章 + 縮寫」**，而不是官方 logo（避免商標問題，也讓縮放永遠清晰）：

| 平台 | 徽章 | 色票 |
|---|---|---|
| GitHub | `GH` | `#24292F` |
| GitLab | `GL` | `#E24329` |
| Bitbucket | `BB` | `#0052CC` |
| Codeberg | `CB` | `#2185D0` |
| Gitea | `GT` | `#5A9E28` |
| AWS CodeCommit | `AWS` | `#E08A00` |
| Azure Repos | `AZ` | `#0078D4` |

徽章顏色與縮寫由 `Views/Converters.cs` 的 `PlatformBrushConverter` / `PlatformInitialsConverter`
自動產生，**新增平台只要在這兩張表加一列**。

---

## 四、檔案結構（UI 部分）

```
Assets/
  Icons.axaml               31 個向量圖示
  Theme.axaml               色票 + 4 種按鈕外觀（Primary / Secondary / Icon / DangerIcon）
App.axaml(.cs)              全域樣式（文字、卡片、徽章、chip、DataGrid、ListBox）
Program.cs                  進入點；在這裡把服務層接上
Models/
  ObservableObject.cs       極簡 INotifyPropertyChanged 基底
  AccountCredential.cs      帳號憑證
  PlatformIds.cs            平台常數 + PlatformDescriptor（驅動表單欄位）
  RepositoryInfo.cs         專案資料（含大小格式化）
  MirrorJob.cs              鏡像任務（狀態 / 進度 / 日誌）
ViewModels/
  RelayCommand.cs           ICommand 實作
  AccountFilter.cs          左欄一列 / 右欄一個目標
  MainWindowViewModel.cs    全部畫面邏輯
Views/
  MainWindow.axaml(.cs)     唯一的視窗
  Converters.cs             平台色 / 縮寫 / 狀態圖示 / 私有圖示
```

---

## 五、目前狀態

* **Models / ViewModels / 契約 / 示範服務**：已用 `dotnet build` 驗證，**0 error 0 warning**。
* **Avalonia XAML 與 Converters**：因雲端環境無法連 NuGet（Avalonia 套件抓不到），
  尚未實機編譯過，請在本機第一次 `dotnet build` 時留意 XAML 錯誤。
* 目前跑起來會顯示**離線示範資料**，可完整走完整個流程（含假的鏡像進度動畫），
  方便先確認操作手感；服務層接上後改 `Program.cs` 一行即可切換。
