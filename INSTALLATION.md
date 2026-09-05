# GitHub Mirror Tool - 安裝與配置指南

## 系統要求

### 最小要求
- 操作系統：Windows 10+, macOS 10.15+, Linux (Ubuntu 18.04+)
- 內存：2GB RAM
- 磁盤空間：500MB（不含鏡像倉庫）
- 網絡連接：穩定的互聯網連接

### 開發要求
- .NET 8.0 SDK 或更新版本
- Git 2.20 或更新版本
- Visual Studio 2022 (可選) 或 Visual Studio Code

## 安裝步驟

### Windows

1. **安裝 .NET 8.0 SDK**
   ```powershell
   # 使用 winget
   winget install dotnet-sdk-8.0
   
   # 或從以下網址下載
   # https://dotnet.microsoft.com/download/dotnet/8.0
   ```

2. **安裝 Git for Windows**
   ```powershell
   winget install git.git
   ```

3. **克隆並構建項目**
   ```powershell
   git clone https://github.com/yourusername/GithubMirror.git
   cd GithubMirror
   dotnet build --configuration Release
   ```

4. **運行應用**
   ```powershell
   dotnet run --configuration Release
   ```

### macOS

1. **安裝 .NET 8.0 SDK**
   ```bash
   # 使用 Homebrew
   brew install dotnet
   
   # 或從以下網址下載
   # https://dotnet.microsoft.com/download/dotnet/8.0
   ```

2. **安裝 Git（如果未安裝）**
   ```bash
   brew install git
   ```

3. **克隆並構建項目**
   ```bash
   git clone https://github.com/yourusername/GithubMirror.git
   cd GithubMirror
   dotnet build --configuration Release
   ```

4. **運行應用**
   ```bash
   dotnet run --configuration Release
   ```

### Linux (Ubuntu/Debian)

1. **安裝 .NET 8.0 SDK**
   ```bash
   wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
   chmod +x dotnet-install.sh
   ./dotnet-install.sh --version 8.0
   
   # 或使用包管理器
   sudo apt-get update
   sudo apt-get install dotnet-sdk-8.0
   ```

2. **安裝 Git**
   ```bash
   sudo apt-get install git
   ```

3. **克隆並構建項目**
   ```bash
   git clone https://github.com/yourusername/GithubMirror.git
   cd GithubMirror
   dotnet build --configuration Release
   ```

4. **運行應用**
   ```bash
   dotnet run --configuration Release
   ```

## 配置指南

### 環境變量（可選）

創建 `.env` 文件或設置以下環境變量：

```bash
# Git 配置
GIT_AUTHOR_NAME=GitHub Mirror Tool
GIT_AUTHOR_EMAIL=mirror@example.com

# 代理配置（如果需要）
HTTP_PROXY=http://proxy.example.com:8080
HTTPS_PROXY=https://proxy.example.com:8443
```

### 應用配置文件

在應用主目錄創建 `config.json`（未來版本支持）：

```json
{
  "defaultPlatform": "GitHub",
  "maxConcurrentMirrors": 2,
  "mirrorTimeout": 3600,
  "tempDirectory": "/tmp/github-mirror",
  "logLevel": "Info",
  "features": {
    "autoMirror": false,
    "scheduleSync": false,
    "compressArchive": false
  }
}
```

## API Token 配置

### GitHub Token 配置

1. 訪問 https://github.com/settings/tokens
2. 點擊 "Generate new token (classic)"
3. 賦予以下權限：
   - ✓ `repo` - 完整控制私有和公開倉庫
   - ✓ `admin:repo_hook` - 完整管理鉤子
   - ✓ `admin:org` - 完整管理組織（如果需要）
4. 複製生成的 Token（只顯示一次）
5. 在應用中添加帳戶時粘貼 Token

### GitLab Token 配置

1. 訪問 https://gitlab.com/-/profile/personal_access_tokens
2. 填寫 Token 名稱
3. 設置過期日期（建議 90 天）
4. 選擇以下權限：
   - ✓ `api` - 訪問 API
   - ✓ `read_repository` - 讀取倉庫
   - ✓ `write_repository` - 寫入倉庫
5. 點擊 "Create personal access token"
6. 複製並保存 Token

### Bitbucket Token 配置

1. 訪問 https://bitbucket.org/account/settings/personal-password-app-passwords/
2. 點擊 "Create App Password"
3. 設置應用名稱（如 "GitHub Mirror"）
4. 選擇權限：
   - ✓ `repositories:read`
   - ✓ `repositories:write`
   - ✓ `account:read`
5. 複製密碼並保存

### Azure Repos Token 配置

1. 訪問 https://dev.azure.com/_usersSettings/tokens
2. 點擊 "New Token"
3. 設置以下選項：
   - 名稱：GitHub Mirror
   - 組織：選擇你的組織
   - 過期時間：90 天
4. 選擇作用域：
   - ✓ `Code (Full)` - 完整代碼訪問
5. 創建並複製 Token

## 代理配置

### 使用代理服務器

如果在公司網絡或受限網絡中：

1. **設置系統代理**
   ```bash
   # Windows (PowerShell)
   $Env:HTTP_PROXY = "http://proxy.company.com:8080"
   $Env:HTTPS_PROXY = "https://proxy.company.com:8443"
   
   # macOS/Linux (Bash)
   export HTTP_PROXY=http://proxy.company.com:8080
   export HTTPS_PROXY=https://proxy.company.com:8443
   ```

2. **Git 代理配置**
   ```bash
   git config --global http.proxy http://proxy.company.com:8080
   git config --global https.proxy https://proxy.company.com:8443
   ```

### SSL/TLS 證書配置

如果使用自簽名證書：

```bash
# 禁用 SSL 驗證（不推薦用於生產環境）
git config --global http.sslVerify false
```

## 故障排除

### 常見問題

#### 1. "git 不是內部或外部命令"
```bash
# 解決方案：確保 Git 已安裝並在 PATH 中
git --version

# Windows：重新安裝 Git for Windows
# macOS/Linux：安裝 Git
brew install git  # macOS
sudo apt install git  # Ubuntu/Debian
```

#### 2. "Unable to connect to API"
```
原因：
- Token 過期或無效
- 網絡連接問題
- 代理配置不正確

解決方案：
- 驗證 Token 是否有效
- 檢查網絡連接
- 檢查代理設置
```

#### 3. "Access denied" 錯誤
```
原因：
- Token 權限不足
- 倉庫不可訪問

解決方案：
- 重新生成具有正確權限的 Token
- 驗證帳戶是否有倉庫訪問權限
```

#### 4. "鏡像進度卡住"
```
原因：
- 網絡連接不穩定
- 倉庫過大
- 磁盤空間不足

解決方案：
- 檢查磁盤空間（`df -h` 或 Windows 磁盤管理器）
- 檢查網絡連接
- 查看臨時目錄
```

## 性能優化

### 鏡像大型倉庫

```bash
# 增加 git 緩衝區
git config --global http.postBuffer 524288000

# 增加超時時間
git config --global http.lowSpeedLimit 0
git config --global http.lowSpeedTime 999999
```

### 並發鏡像

應用支持配置最大並發任務數。編輯 `config.json`：

```json
{
  "maxConcurrentMirrors": 4
}
```

## 升級指南

### 從舊版本升級

```bash
# 1. 更新源代碼
git pull origin main

# 2. 清理舊的構建文件
dotnet clean

# 3. 重新構建
dotnet build --configuration Release

# 4. 運行應用
dotnet run --configuration Release
```

## 卸載

### Windows
```powershell
# 刪除應用目錄
Remove-Item -Recurse -Force "C:\path\to\GithubMirror"

# （可選）卸載 .NET SDK
winget uninstall dotnet-sdk-8.0
```

### macOS/Linux
```bash
# 刪除應用目錄
rm -rf ~/GithubMirror

# （可選）卸載 .NET SDK
brew uninstall dotnet  # macOS
```

## 日誌配置

應用日誌位置：
- Windows: `%APPDATA%\GithubMirror\logs\`
- macOS: `~/Library/Application Support/GithubMirror/logs/`
- Linux: `~/.config/GithubMirror/logs/`

### 查看日誌

```bash
# 實時查看日誌
tail -f ~/.config/GithubMirror/logs/app.log  # macOS/Linux
Get-Content -Path "C:\Users\...\AppData\Local\GithubMirror\logs\app.log" -Wait  # Windows
```

## 下一步

- 閱讀 [README.md](./README.md) 了解功能
- 查看 [使用指南](#) 學習基本操作
- 提交 Issue 或 PR 貢獻代碼

## 獲取幫助

- 📖 [文檔](./README.md)
- 🐛 [報告 Bug](../../issues/new?template=bug.md)
- 💡 [功能請求](../../issues/new?template=feature.md)
- 💬 [討論區](../../discussions)
