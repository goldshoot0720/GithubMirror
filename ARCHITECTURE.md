# GitHub Mirror Tool - 項目架構文檔

## 項目概述

GitHub Mirror Tool 是一個使用 Avalonia（跨平台 UI 框架）構建的桌面應用程序，用於自動鏡像和同步多個 Git 平台上的代碼倉庫。

## 核心功能

### 1. 帳戶管理
- 支持多個平台的帳戶
- 安全的 Token 管理
- 帳戶的添加、編輯、刪除
- 帳戶驗證和測試連接

### 2. 項目列表
- 實時獲取用戶在各平台的項目列表
- 顯示項目詳細信息：
  - 項目名稱、所有者、描述
  - 項目大小（KB）
  - Stars 和 Forks 數量
  - 編程語言
  - 最後更新時間
  - 標籤/主題
  - 可見性（公開/私有）

### 3. 鏡像功能
- 單向鏡像：從源倉庫複製到目標倉庫
- 支持的操作：
  - 完整 Git 倉庫克隆
  - 所有分支和標籤同步
  - 自動創建目標倉庫
  - 項目元數據同步（描述、可見性等）
  
### 4. 任務管理
- 實時任務進度跟踪
- 任務狀態監控（pending、進行中、已完成、失敗）
- 任務日誌和錯誤報告
- 批量鏡像任務隊列

## 架構設計

```
┌─────────────────────────────────────────────┐
│         Avalonia UI Layer                   │
│  (MainWindow, Dialogs, DataGrids)          │
└────────────────┬────────────────────────────┘
                 │
┌────────────────▼────────────────────────────┐
│      ViewModel Layer (MVVM)                 │
│  (MainViewModel, Command Handlers)          │
└────────────────┬────────────────────────────┘
                 │
┌────────────────▼────────────────────────────┐
│      Service Layer                          │
│  (IGitService Implementation)               │
│  ├─ GitHubService                           │
│  ├─ GitLabService                           │
│  ├─ BitbucketService                        │
│  ├─ CodebergService                         │
│  ├─ GiteaService                            │
│  ├─ AWSCodeCommitService                    │
│  └─ AzureReposService                       │
└────────────────┬────────────────────────────┘
                 │
┌────────────────▼────────────────────────────┐
│      API & Data Layer                       │
│  ├─ REST API Clients                        │
│  ├─ OAuth/Token Management                  │
│  ├─ Model/DTO Definitions                   │
│  └─ Data Serialization                      │
└────────────────┬────────────────────────────┘
                 │
┌────────────────▼────────────────────────────┐
│      External Services                      │
│  ├─ GitHub API                              │
│  ├─ GitLab API                              │
│  ├─ Bitbucket API                           │
│  ├─ Azure DevOps API                        │
│  ├─ Git CLI                                 │
│  └─ HTTP Client                             │
└─────────────────────────────────────────────┘
```

## 文件結構

```
GithubMirror/
│
├── Models/
│   └── Models.cs                    # 核心數據模型
│       ├── RepositoryInfo          # 倉庫信息
│       ├── AccountCredential       # 帳戶憑證
│       ├── MirrorTask              # 鏡像任務
│       └── MirrorStatus            # 任務狀態枚舉
│
├── Services/
│   ├── IGitService.cs              # 服務接口和工廠
│   │   ├── IGitService            # 統一服務接口
│   │   └── GitServiceFactory      # 工廠模式實現
│   │
│   ├── GitHubService.cs            # GitHub 平台實現
│   │   ├── GetRepositoriesAsync   # 獲取項目列表
│   │   ├── MirrorRepositoryAsync  # 鏡像項目
│   │   └── ExecuteMirrorCloneAsync # 執行 Git 操作
│   │
│   ├── GitLabService.cs            # GitLab 平台實現
│   │   └── (同上)
│   │
│   ├── OtherPlatformServices.cs     # 其他平台集合實現
│   │   ├── BitbucketService
│   │   ├── CodebergService
│   │   └── GiteaService
│   │
│   └── CloudServices.cs             # 雲服務提供商
│       ├── AWSCodeCommitService
│       └── AzureReposService
│
├── ViewModels/
│   └── MainViewModel.cs             # 主視圖模型
│       ├── Accounts               # 帳戶列表
│       ├── Repositories           # 項目列表
│       ├── MirrorTasks            # 任務列表
│       ├── LoadRepositoriesAsync  # 加載項目
│       └── StartMirrorAsync       # 啟動鏡像
│
├── Views/
│   ├── MainWindow.axaml            # 主窗口 UI
│   │   ├── 菜單欄
│   │   ├── 項目列表 (DataGrid)
│   │   └── 任務列表 (DataGrid)
│   │
│   ├── MainWindow.axaml.cs         # 主窗口代碼後置
│   │   ├── 事件處理
│   │   └── UI 邏輯
│   │
│   ├── AddAccountWindow.axaml      # 添加帳戶窗口
│   │   └── 帳戶配置表單
│   │
│   ├── AddAccountWindow.axaml.cs   # 代碼後置
│   │
│   ├── MirrorDialog.axaml          # 鏡像配置對話框
│   │   └── 鏡像選項表單
│   │
│   └── MirrorDialog.axaml.cs       # 代碼後置
│
├── App.axaml                       # 應用全局配置
├── App.axaml.cs                    # 應用啟動代碼
├── Program.cs                      # 應用入口點
│
├── GithubMirror.csproj             # 項目文件
├── README.md                       # 項目說明
├── INSTALLATION.md                 # 安裝指南
├── ARCHITECTURE.md                 # 本文檔
├── .gitignore                      # Git 忽略規則
├── run-windows.bat                 # Windows 快速啟動
├── run.sh                          # Linux/macOS 快速啟動
└── Makefile                        # 構建命令集
```

## 設計模式

### 1. 工廠模式 (Factory Pattern)

**位置**: `GitServiceFactory`

```csharp
public static IGitService CreateService(string platform)
{
    // 根據平台名稱動態創建服務實例
}
```

**優點**:
- 集中管理服務創建邏輯
- 易於添加新平台支持
- 解耦業務邏輯和服務實現

### 2. 策略模式 (Strategy Pattern)

**位置**: `IGitService` 及其實現

```csharp
public interface IGitService
{
    Task<List<RepositoryInfo>> GetRepositoriesAsync();
    Task<bool> MirrorRepositoryAsync();
}
```

**優點**:
- 統一的服務接口
- 運行時動態選擇實現
- 易於測試和擴展

### 3. MVVM 模式 (Model-View-ViewModel)

**組件**:
- **Model**: `Models/Models.cs` - 數據模型
- **View**: `Views/*.axaml` - UI 界面
- **ViewModel**: `ViewModels/MainViewModel.cs` - 業務邏輯

**優點**:
- 數據和 UI 分離
- 易於測試
- 支持數據綁定

### 4. 依賴注入模式 (Dependency Injection)

**實現**:
- 通過構造函數傳遞依賴
- 工廠模式提供實例

## API 集成

### GitHub API
- **端點**: `https://api.github.com`
- **認證**: OAuth Token
- **庫**: Octokit

### GitLab API
- **端點**: `https://gitlab.com/api/v4` (可自定義)
- **認證**: Personal Access Token
- **實現**: HTTP Client + JSON

### Bitbucket API
- **端點**: `https://api.bitbucket.org/2.0`
- **認證**: Basic Auth (username:password)
- **庫**: SharpBucket

### 其他平台
- **Codeberg/Gitea**: 使用 RESTful API
- **AWS CodeCommit**: AWS SDK
- **Azure Repos**: Azure DevOps REST API

## 鏡像流程

```
┌─────────────────────────────────────────┐
│ 用戶選擇源倉庫和目標平台                 │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 驗證源和目標帳戶憑證                     │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 在目標平台創建新倉庫                     │
│ (如果不存在)                            │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 執行 Git 命令序列:                      │
│ 1. git clone --mirror <source>          │
│ 2. git push --mirror <target>           │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 同步項目元數據                           │
│ (描述、標籤、主題等)                    │
└──────────────┬──────────────────────────┘
               │
               ▼
┌─────────────────────────────────────────┐
│ 清理臨時文件並報告結果                   │
└─────────────────────────────────────────┘
```

## 錯誤處理策略

### 級別 1: API 層錯誤
```csharp
try {
    // API 調用
} catch (HttpRequestException) {
    // 網絡錯誤
} catch (JsonSerializationException) {
    // 數據解析錯誤
}
```

### 級別 2: 服務層錯誤
```csharp
try {
    // 服務操作
} catch (InvalidOperationException) {
    // 業務邏輯錯誤
}
```

### 級別 3: UI 層錯誤
```csharp
try {
    // UI 操作
} catch (Exception ex) {
    StatusMessage = $"Error: {ex.Message}";
}
```

## 性能考慮

### 異步編程
- 所有 API 調用使用 `async/await`
- UI 線程不被阻塞
- 支持後台鏡像任務

### 內存管理
- 使用 `using` 語句管理資源
- 及時清理臨時目錄
- 避免大型對象在內存中留存

### 網絡優化
- 批量 API 請求
- 連接復用
- 超時和重試機制

## 安全考慮

### Token 安全
- Token 存儲在內存中（應用關閉後清除）
- 不進行本地持久化
- 每次運行需重新輸入

### 通信安全
- 使用 HTTPS
- SSL/TLS 證書驗證
- 不記錄敏感信息

### 權限管理
- 最小權限原則
- Token 範圍限制
- 帳戶隔離

## 擴展性

### 添加新平台支持

1. **創建新的服務類**
   ```csharp
   public class NewPlatformService : IGitService
   {
       public string GetPlatformName() => "NewPlatform";
       public async Task<List<RepositoryInfo>> GetRepositoriesAsync() { }
       public async Task<bool> MirrorRepositoryAsync() { }
   }
   ```

2. **更新工廠**
   ```csharp
   ServiceMap.Add("NewPlatform", typeof(NewPlatformService));
   ```

3. **測試集成**
   - 驗證 API 連接
   - 測試項目列表加載
   - 測試鏡像操作

## 測試策略

### 單元測試
- 服務邏輯測試
- 模型驗證測試

### 集成測試
- API 集成測試
- 端到端鏡像流程測試

### 用戶測試
- UI 交互測試
- 跨平台兼容性測試

## 部署

### 發布過程
1. 代碼審查和測試
2. 版本標記和發佈說明
3. 構建發布版本
4. 發布到各平台

### 支持的平台
- Windows 10+ (x64)
- macOS 10.15+ (x64, arm64)
- Linux (Ubuntu 18.04+)

## 未來改進方向

- [ ] 本地 Token 加密存儲
- [ ] 定時自動鏡像 (計劃任務)
- [ ] Issues 和 PRs 遷移
- [ ] Wiki 頁面遷移
- [ ] 實時進度條顯示
- [ ] 日誌分析和導出
- [ ] 多語言支持
- [ ] 插件系統
- [ ] Web 界面版本
