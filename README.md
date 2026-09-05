# GitHub Mirror Tool

一個使用 Avalonia 構建的跨平台應用程式，允許用戶鏡像 GitHub 上的多個帳戶的項目到不同的 Git 服務提供者。

## 功能特性

- **多帳戶支持**: 管理多個 GitHub、GitLab、Bitbucket 等帳戶
- **項目列表**: 列出帳戶中的所有項目及詳細信息
  - 項目名稱、所有者、描述
  - 項目大小（KB）
  - Stars 和 Forks 數量
  - 最後更新時間
  - 主要編程語言
  
- **跨平台鏡像**: 支持鏡像到以下平台：
  - GitHub
  - GitLab
  - Bitbucket
  - Codeberg
  - Gitea
  - AWS CodeCommit
  - Azure Repos
  
- **任務追踪**: 實時監控鏡像任務進度和狀態
- **鏡像背景音樂**: 可選十一首 OpenMusic 歌曲，預設接續播放下一首（清單播完會從頭再來），也可改為單曲循環、手動切換、播放與暫停。勾選「鏡像時自動播放」即可在開始複製時播放，完成或取消時停止；手動播放可獨立於鏡像工作持續播放。內建播放支援 Windows。曲目如下：
  - 〈鋒兄的傳奇人生〉（黃馨鋒）：https://www.openmusic.ai/tw/song/7QFM8xkYlL
  - 〈水電進化論〉（黃馨鋒）：https://www.openmusic.ai/tw/song/JwYHPsrRr7
  - 〈鋒塗力一起拚〉（黃阿不點）：https://www.openmusic.ai/tw/song/p11bGIsK7Q
  - 〈排列組合的對話〉（馮思敏）：https://www.openmusic.ai/tw/song/ocAHNKqKvZ
  - 〈結婚理由〉（迪華敕擩）：https://www.openmusic.ai/tw/song/NHc6u5KDVD
  - 〈水電王子〉（鋒兄塗哥公關資訊鋒兄AI工作室塗哥建設）：https://www.openmusic.ai/tw/song/4HqKPWjhmN
  - 〈招財喵布布送祝福〉（鋒兄塗哥公關資訊鋒兄AI工作室塗哥建設）：https://www.openmusic.ai/tw/song/ygPzygIV00
  - 〈集中統一領導〉（feng feng）：https://www.openmusic.ai/tw/song/5M5cPaj54t
  - 〈鋒兄進化論〉（feng feng）：https://www.openmusic.ai/tw/song/0l7SVUmMPR
  - 〈塗神水電王子〉（Hsin Feng Huang）：https://www.openmusic.ai/tw/song/t0mtRi7uEZ
  - 〈喵布布本喵掉的毛〉（Hsin Feng Huang）：https://www.openmusic.ai/tw/song/sjMdsmLlAg
- **內建使用教學**: 啟動時提供七步驟新手導覽，並可從主畫面隨時重新開啟
- **設定檔 Google 雲端備份**: 主畫面右上角與右側都有專屬「備份設定檔」按鈕，可將帳號 Token 與應用程式設定加密上傳到 Google 雲端硬碟的「OAuth / GithubMirror」資料夾，換電腦後再還原

## 支持的平台

### 源平台
- GitHub (github.com)
- GitLab (gitlab.com 或自託管)
- Bitbucket
- Codeberg
- Gitea (自託管)
- AWS CodeCommit
- Azure Repos

### 目標平台
- 所有上述平台

## 安裝要求

### 前置條件
- .NET 8.0 SDK
- Git (用於鏡像操作)
- 各平台的 API Token 或個人訪問令牌

### 依賴包
- Avalonia 11.0.10 - 跨平台 UI 框架
- Octokit - GitHub API 客戶端
- GitLabApiClient - GitLab API 支持
- SharpBucket - Bitbucket API 支持
- Newtonsoft.Json - JSON 序列化

## 快速開始

### 1. 克隆項目
```bash
git clone <repository-url>
cd GithubMirror
```

### 2. 構建項目
```bash
dotnet build
```

### 3. 運行應用
```bash
dotnet run
```

## 使用指南

程式內建教學，不用先讀文件也能上手：

- **第一次啟動**：中間會直接顯示「三步驟連上 GitHub」，左側「新增帳號」面板與取得 Token 的步驟說明都已展開。
- **Token 欄位旁的「?」圖示**：切換平台後點一下，就會換成該平台的逐步取得方法，並附一個按鈕直接開啟該平台的設定頁面。
- **右上角「使用教學」**：七步驟完整導覽，隨時可重新開啟。
- **右上角與右側「備份設定檔」**：專屬按鈕，可把設定檔加密備份到 Google 雲端硬碟的「OAuth / GithubMirror」資料夾。

### 第一步：加入帳號

1. 左側點 **新增帳號**（第一次啟動已自動展開）
2. 選平台 —— 或直接貼上 Token，程式會依前綴（`ghp_`、`glpat-`…）自動辨識
3. 不知道 Token 哪裡拿？點 Token 欄位旁的 **「?」** 看該平台步驟
4. 自架站台或要建到組織底下，展開 **進階設定** 填伺服器位址 / 組織
5. 按 **驗證並加入** —— 通過後會自動抓出帳號名稱並載入專案清單

帳號會加密存在 `%AppData%/GithubMirror/`（Windows 用 DPAPI，其他系統用 AES-GCM + 本機金鑰檔），下次啟動自動帶回。

### 第二步：挑選專案

1. 預設只顯示此帳號自己的專案（例如 `goldshoot0720` 只列出 `goldshoot0720/…`）
2. 要看被邀請協作的倉庫，勾選 **協作的專案**；要含組織／群組等 Token 能存取的全部，勾選 **所有可能專案**
3. 大小欄預設是**最近一次提交**（目前檔案加總）。要看整份 Git 歷史（含已刪檔）請勾選 **所有提交的大小**
4. 專案清單上方的搜尋列可依名稱、擁有者、描述、語言過濾（也可按 Ctrl+F）；工具列可切 Public／Private，並用 **全選 / 清除** 快速調整
5. 勾選要鏡像的專案

### 第三步：設定目標並開始

1. 右側勾選一個或多個 **目標帳號**（可同時鏡像到多個平台）
2. 需要改名就調整 **目標名稱規則**，支援 `{name}`、`{owner}`、`{platform}`
3. 按 **開始鏡像**，底部任務區顯示每個任務的進度與紀錄

## 獲取 API Token

以下是各平台需要的權限；**程式內按 Token 欄位旁的「?」會顯示同一份逐步說明，並可直接開啟對應頁面**。

### GitHub — https://github.com/settings/tokens
Settings → Developer settings → Personal access tokens → Tokens (classic) → Generate new token (classic)。
勾選 `repo`；要把目標建在組織底下再加勾 `admin:org` 的 `write:org`。
Fine-grained token 也支援：Repository access 選 All repositories，權限給 Contents（Read and write）、Administration（Read and write）、Metadata（Read-only）。
GitHub Enterprise 在「進階設定」填 `https://github.你的公司.com`，程式自動接上 `/api/v3`。

### GitLab — https://gitlab.com/-/user_settings/personal_access_tokens
Edit profile → Access tokens → Add new token，Scopes 勾 `api`。
自架 GitLab 在「進階設定」填站台網址；要建到群組底下，填群組頁面上的數字 Namespace ID。

### Bitbucket — https://bitbucket.org/account/settings/app-passwords/
Personal settings → App passwords → Create app password，勾 Repositories 的 **Read、Write、Admin**。
使用者名稱要填 Bitbucket username（不是 email）。

### Codeberg — https://codeberg.org/user/settings/applications
Settings → Applications → Manage Access Tokens，`repository` 設為 Read and Write（要建到組織再加 `organization`）。

### Gitea（自架）
右上頭像 → Settings → Applications → Manage Access Tokens，權限同 Codeberg。
**伺服器位址為必填**（填到網站根目錄即可，程式自動接 `/api/v1`）。

### AWS CodeCommit — https://console.aws.amazon.com/iam/home#/users
需要兩組憑證：
1. **Access Key ID / Secret access key**（IAM → 使用者 → Security credentials → Create access key）—— 呼叫 API 列出／建立 repository
2. **HTTPS Git credentials for AWS CodeCommit**（同一頁下方 Generate credentials）—— `git push` 用的帳密

IAM 使用者至少需要 `AWSCodeCommitPowerUser`，並填入 repository 實際所在的區域（例：`ap-northeast-1`）。

### Azure Repos — https://dev.azure.com
User settings → Personal access tokens → New Token，Scopes 展開 **Code** 勾 `Read, write, & manage`。
Organization 為必填（`dev.azure.com/【這一段】`）；Project 留空會掃描該組織所有專案。

## 項目結構

```
GithubMirror/
├── Models/
│   └── Models.cs                 # 數據模型
├── Services/
│   ├── IGitService.cs           # 服務接口
│   ├── GitHubService.cs         # GitHub 實現
│   ├── GitLabService.cs         # GitLab 實現
│   ├── OtherPlatformServices.cs # 其他平台服務
│   └── CloudServices.cs         # AWS/Azure 服務
├── ViewModels/
│   └── MainViewModel.cs         # 主視圖模型
├── Views/
│   ├── MainWindow.axaml         # 主窗口
│   ├── AddAccountWindow.axaml   # 添加帳戶窗口
│   └── MirrorDialog.axaml       # 鏡像對話框
├── App.axaml                    # 應用配置
├── Program.cs                   # 入口點
└── GithubMirror.csproj         # 項目文件
```

## 架構說明

### 設計模式
- **工廠模式**: `GitServiceFactory` 根據平台名稱動態創建服務實例
- **策略模式**: 每個平台服務實現 `IGitService` 接口，提供統一的操作方式
- **MVVM 模式**: Avalonia UI 使用 ViewModel 模式進行數據綁定

### 核心流程

1. **帳戶管理**: 用戶添加多個平台的帳戶憑證
2. **項目列表**: 通過各平台 API 獲取用戶的項目列表
3. **鏡像操作**:
   - 使用 `git clone --mirror` 克隆源倉庫
   - 使用 `git push --mirror` 推送到目標平台
   - 支持自動創建目標倉庫

## 功能詳細說明

### 支持的鏡像類型

#### 完整鏡像（git clone --mirror）
- 克隆所有分支
- 克隆所有標籤
- 克隆所有引用

#### 項目信息同步
- 項目名稱和描述
- 可見性設置（公開/私有）
- 項目大小

### 錯誤處理

應用程序包含以下錯誤處理機制：
- 網絡錯誤重試
- Token 過期檢測
- Git 命令失敗報告
- UI 友好的錯誤消息顯示

## 安全性考慮

- Token 加密後存在 `%AppData%/GithubMirror/accounts.dat`：Windows 使用目前使用者範圍的 DPAPI，其他系統使用 AES-GCM + 僅限本人讀寫的金鑰檔
- 換一個 Windows 使用者或換一台電腦都無法解密該檔，不建議在公開電腦上使用
- Git 命令使用 HTTPS + Token 認證，並設定 `GIT_TERMINAL_PROMPT=0` 避免跳出帳密視窗
- 紀錄中的網址會自動把密碼遮成 `***`

## 性能優化

- 異步 API 調用防止 UI 卡頓
- 批量項目加載
- 背景任務隊列管理

## 已知限制

1. **AWS CodeCommit**: 需要 AWS SDK 配置或本地 AWS CLI 認證
2. **自託管平台**: 需要正確的 API URL
3. **大型倉庫**: 鏡像大型倉庫可能需要較長時間
4. **令牌存儲**: 不支持本地加密存儲，每次啟動需要重新輸入

## 未來改進方向

- [ ] Token 本地加密存儲
- [ ] 批量鏡像操作
- [ ] 定時自動鏡像
- [ ] Issues 和 PRs 遷移
- [ ] Wiki 頁面遷移
- [ ] 進度條實時顯示
- [ ] 日誌文件導出
- [ ] 多語言支持

## 貢獻指南

歡迎提交 Issue 和 Pull Request！

## 許可證

本項目採用 MIT 許可證

## 技術支持

- GitHub Issues: <項目 Issue 頁面>
- 文檔: 查看 README.md

## 更新日誌

### v1.0.0 (2026-09-05)
- 初始版本發佈
- 支持 7 個主要 Git 平台
- 完整的項目鏡像功能

### GitHub 手把手設定

選擇 GitHub 後，依左側四步引導登入、填寫 Token 名稱與期限、設定權限，再貼上 Token 並驗證。可返回上一步，已有 Token 可直接跳到填寫。一般個人帳號只需 Token，帳號名稱自動取得；組織與 Enterprise 網址為選填。驗證並加入後會載入專案，再選來源與目標開始鏡像。

### Google Drive 加密備份與還原

主畫面右上角或右側按專屬的「備份設定檔」按鈕。備份包含所有平台帳號與 Token、鏡像命名規則、保留私有選項、音樂選擇與自動播放設定，以及啟動教學偏好；不包含 Git 倉庫內容、專案清單或工作記錄。

1. 在 [Google Cloud](https://console.cloud.google.com/apis/credentials) 啟用 Google Drive API、設定 OAuth 同意畫面。測試模式需將登入帳號加入測試使用者，再建立「桌面應用程式」OAuth 用戶端並下載 JSON。
2. 按「選擇 OAuth JSON 並登入」，在瀏覽器授權。使用 `drive.file` 權限，只存取此應用程式建立或獲授權的檔案。登入資料只留在記憶體，關閉備份視窗或登入過期後需重新登入。
3. 輸入並確認四位數字備份密碼，按「加密並上傳至 OAuth/GithubMirror」。檔案會放到 Google 雲端硬碟的「OAuth / GithubMirror」資料夾（第一次上傳時自動建立）。每次建立獨立的 `.gmbak` 檔案，不覆寫舊備份。密碼不會儲存或上傳，忘記密碼無法還原。
4. 還原時，在同一 Google 帳號下使用同一組 OAuth 設定檔，重新整理備份清單、選擇檔案，輸入該備份的密碼並解密預覽。確認摘要後按「確認取代本機帳號與設定」。此操作取代全部本機帳號，不合併；不修改遠端 Git 倉庫。
5. 關閉備份視窗，按「重新整理」載入還原帳號的專案，重新選擇來源與目標。還原的設定會保存到本機，重新啟動後仍可使用。

備份採 AES-256-GCM 與 PBKDF2-SHA256（600,000 次）加密及驗證，可跨電腦還原；本機帳號仍使用既有的 DPAPI／AES 憑證儲存方式。錯誤密碼、遭修改或超過 8 MiB 的備份會被拒絕。鏡像、帳號驗證或清單載入期間需等待工作結束才能開啟備份視窗。上傳中斷或取消時可能已在雲端建立檔案，請先重新整理清單再重試。

Google 官方設定參考：[桌面 OAuth](https://developers.google.com/identity/protocols/oauth2/native-app)、[Drive 權限](https://developers.google.com/workspace/drive/api/guides/api-specific-auth)。
