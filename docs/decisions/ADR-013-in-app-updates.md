# ADR-013：從 GitHub Releases 檢查並安裝更新

## 狀態

Accepted（2026-10-23，產品負責人要求加入「檢查更新」，並指示參考 desktop-work-fleet 的作法）。目前還沒有任何 release，而且 repository 是 private，所以實際只能驗證錯誤路徑；第一次發佈前的條件見 [deployment.md](../deployment.md)。

## 背景

- 產品以 NSIS 安裝檔發佈（[ADR-012](./ADR-012-release-packaging.md)），使用者需要知道是否有新版，並能直接更新。
- GitHub REST API 的未登入額度是每個來源 IP 每小時 60 次；同一個 NAT 出口的使用者共用這個額度。desktop-work-fleet 實際遇過檢查更新被限流，但同一台電腦讀公開的 `releases.atom` 正常。
- 安裝範圍是目前使用者（不需要系統管理員權限），所以更新可以靜默安裝、不跳 UAC。
- 本 repository 目前是 private。private repository 的 release feed 與檔案，未登入就讀不到。

## 決策

- 來源：`Directory.Build.props` 的 `UpdateRepository`（`owner/name`），`FWB_UPDATE_REPOSITORY` 可以覆寫，供測試使用。
- 檢查：讀 `https://github.com/<repo>/releases.atom`，不帶憑證或 cookie，也不跟隨轉址；只接受同一個 repository 的 release 頁面連結，以及 `v<major>.<minor>.<patch>` 格式的 tag，取比目前版本高的最高版本，不看發佈順序。feed 有 release、卻沒有任何符合格式的 tag 時，回報資訊不足，不會回答「已是最新」。
- 更新資訊：每個 release 附 `file-workbench-update-windows.json`（`schemaVersion`、`platform`、`version`、`tag`、`summary`，以及各架構安裝檔的 `name`、`size`、`sha256`）。內容必須和 release 一致，而且唯一能指定的安裝檔是 `FileWorkbench-<version>-x64-setup.exe`，release 裡的其他檔案都不會被下載或執行。
- 下載：從同一個 release 下載，只允許 HTTPS 轉址到 GitHub 的檔案主機，最多 5 次；邊下載邊算 SHA-256，大小或雜湊不符就刪除檔案。超過 60 秒沒有進度就停止。檔案放在 `%LOCALAPPDATA%\FileWorkbench\updates`。
- 安裝：只有安裝程式裝的副本（執行位置等於登錄檔記錄的 `InstallDir\app`）才提供「下載並安裝」。下載完成後先詢問使用者，確定後鎖住安裝檔、再核對一次 SHA-256，以 `/S /WAITPID=<pid> /RELAUNCH` 啟動安裝檔，然後結束；安裝檔等它結束、更新完再重新開啟程式，更新失敗時則重新開啟原本的版本。關閉更新視窗會取消進行中的下載。其他副本只顯示版本，並提供 releases 頁面的連結。
- `FWB_UPDATE_REPOSITORY` 只在 Debug 建置有效，Release 版一律使用內建的來源。
- 錯誤以固定代碼分類（`not_found`、`offline`、`timeout`、`rate_limited`、`provider_rejected`、`provider_unavailable`、`metadata_invalid`、`release_missing`、`size_mismatch`、`checksum_mismatch`），畫面顯示可以理解的說明，不顯示原始回應。
- Debug 建置不產生更新資訊檔，所以 Debug 安裝檔不可能被當成更新提供。
- 第一版只在使用者按下「Check for updates…」時檢查，沒有自動或定期檢查，也沒有「略過這個版本」。

## 影響

### 正面

- 不受 REST API 額度影響，也不需要權杖。
- 安裝檔的來源、大小與雜湊都有核對；外部 feed 或 manifest 無法讓程式下載或執行其他 repository 的檔案。
- 個人安裝不需要 UAC，更新可以一鍵完成。

### 負面

- repository 必須公開（或改指向一個只放 release 的公開 repository），檢查更新才可能成功。
- 安裝檔沒有簽章，只靠 HTTPS、同一個 release 與 SHA-256 確認；簽章仍待 ADR-012 決定。
- Atom feed 只列最近的 release，而且沒有可靠的 prerelease 標記，所以所有 release 都視為更新候選，目前只有一個預覽通道。

### 中性

- 規則與 desktop-work-fleet 的 ADR-044 相同（Atom feed、每個 release 一份 manifest、tag `vX.Y.Z`），只是程式碼依本專案重寫，安裝方式改為 NSIS。

## 考慮過的替代方案

### 替代方案一：GitHub REST API（`releases/latest`）

- **優點**：有完整的檔案資訊與 prerelease 標記。
- **缺點**：未登入時每個 IP 每小時 60 次，公司網路共用出口時很容易用完；`releases/latest` 也排除 prerelease。
- **不採用的原因**：檢查更新不能取決於共用的 API 額度。

### 替代方案二：Velopack、Squirrel 等更新框架

- **優點**：提供差量更新與完整的更新流程。
- **缺點**：要改用它們的安裝與目錄結構，取代剛建立的 NSIS 安裝檔；多一個框架相依。
- **不採用的原因**：目前規模用簡單的流程就足夠。

### 替代方案三：MSIX 搭配 App Installer

- **優點**：由 Windows 處理更新與簽章檢查。
- **缺點**：一定要簽章，而且資料位置與檔案系統存取受封裝限制；產品負責人選擇了 NSIS。
- **不採用的原因**：不符合目前的安裝決定。

## 參考

- [deployment.md：In-app updates、Publishing a release](../deployment.md)
- [ADR-012：發佈格式與資料位置](./ADR-012-release-packaging.md)
- [UpdateFeed.cs](../../src/Core/Updates/UpdateFeed.cs)、[UpdateService.cs](../../src/Core/Updates/UpdateService.cs)

---

*提出與決策日期：2026-10-23*
*決策者：產品負責人*
