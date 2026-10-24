namespace FileWorkbench.Localization;

// Why a check, download or install did not finish (ADR-013). The code on UpdateException stays a stable English id.
public static partial class AppText
{
  // i18n:en
  private static readonly Dictionary<string, string> UpdateEnglish = new(StringComparer.Ordinal)
  {
    ["update.notConfigured"] = "No valid update source is configured.",
    ["update.feedNotFound"] = "The release source was not found. The repository may be private or have no releases yet.",
    ["update.manifestNotFound"] = "Release {tag} has no Windows update manifest.",
    ["update.releaseMissing"] = "The release feed has no versioned release (vX.Y.Z) yet.",
    ["update.feedInvalid"] = "The release feed is not an Atom feed.",
    ["update.feedUnreadable"] = "The release feed could not be read.",
    ["update.manifestFormat"] = "The update manifest has an unsupported format.",
    ["update.manifestMismatch"] = "The update manifest does not match its release.",
    ["update.summaryTooLong"] = "The update manifest summary is too long.",
    ["update.noInstallers"] = "The update manifest lists no installers.",
    ["update.noArchitecture"] = "The update has no single installer for {architecture}.",
    ["update.unexpectedInstaller"] = "The update manifest names an unexpected installer.",
    ["update.installerDescription"] = "The update manifest describes the installer incorrectly.",
    ["update.manifestUnreadable"] = "The update manifest could not be read.",
    ["update.invalidFile"] = "The update manifest names an invalid file.",
    ["update.openFailed"] = "The downloaded installer could not be opened. Download it again.",
    ["update.changed"] = "The downloaded installer changed after it was checked. Download it again.",
    ["update.readFailed"] = "The downloaded installer could not be read. Download it again.",
    ["update.checksumMismatch"] = "The downloaded installer does not match its published checksum, so it was deleted.",
    ["update.sizeMismatch"] = "The downloaded installer has a different size than published, so it was deleted.",
    ["update.stalled"] = "The download stopped making progress and was cancelled.",
    ["update.saveFailed"] = "The installer could not be saved: {detail}",
    ["update.tooLarge"] = "The release source returned more data than expected.",
    ["update.noResponse"] = "The release source did not respond in time.",
    ["update.redirected"] = "The release source redirected the request unexpectedly.",
    ["update.redirectedDownload"] = "The download was redirected to an unexpected location.",
    ["update.refused"] = "The release source refused the request. The repository may be private.",
    ["update.providerUnavailable"] = "The release source is temporarily unavailable. Try again later.",
    ["update.providerStatus"] = "The release source answered with HTTP {status}.",
    ["update.rateLimited"] = "The release source is limiting requests from this network. Try again later.",
    ["update.offline"] = "Could not reach the release source, or the connection was interrupted. Check the internet connection and try again.",
    ["update.cancelled"] = "The update was cancelled.",
    ["update.failed"] = "The update didn't finish. Try again later.",
  };
  // i18n:end

  // i18n:zh-TW
  private static readonly Dictionary<string, string> UpdateChinese = new(StringComparer.Ordinal)
  {
    ["update.notConfigured"] = "沒有設定有效的更新來源。",
    ["update.feedNotFound"] = "找不到發佈來源。repository 可能是 private，或還沒有任何 release。",
    ["update.manifestNotFound"] = "版本 {tag} 沒有 Windows 的更新資訊。",
    ["update.releaseMissing"] = "發佈來源還沒有任何帶版本號（vX.Y.Z）的 release。",
    ["update.feedInvalid"] = "發佈來源不是 Atom feed。",
    ["update.feedUnreadable"] = "無法讀取發佈來源。",
    ["update.manifestFormat"] = "更新資訊的格式不受支援。",
    ["update.manifestMismatch"] = "更新資訊和它所屬的 release 不一致。",
    ["update.summaryTooLong"] = "更新資訊的摘要太長。",
    ["update.noInstallers"] = "更新資訊沒有列出任何安裝程式。",
    ["update.noArchitecture"] = "這個更新沒有唯一一個 {architecture} 的安裝程式。",
    ["update.unexpectedInstaller"] = "更新資訊指定了不預期的安裝程式。",
    ["update.installerDescription"] = "更新資訊對安裝程式的描述不正確。",
    ["update.manifestUnreadable"] = "無法讀取更新資訊。",
    ["update.invalidFile"] = "更新資訊指定的檔名不正確。",
    ["update.openFailed"] = "無法開啟下載的安裝程式，請重新下載。",
    ["update.changed"] = "下載的安裝程式在檢查後被改動過，請重新下載。",
    ["update.readFailed"] = "無法讀取下載的安裝程式，請重新下載。",
    ["update.checksumMismatch"] = "下載的安裝程式和公布的 SHA-256 不符，已刪除。",
    ["update.sizeMismatch"] = "下載的安裝程式大小和公布的不同，已刪除。",
    ["update.stalled"] = "下載停止進展，已取消。",
    ["update.saveFailed"] = "無法儲存安裝程式：{detail}",
    ["update.tooLarge"] = "發佈來源傳回的資料比預期多。",
    ["update.noResponse"] = "發佈來源沒有及時回應。",
    ["update.redirected"] = "發佈來源把請求轉到了不預期的位置。",
    ["update.redirectedDownload"] = "下載被轉到了不預期的位置。",
    ["update.refused"] = "發佈來源拒絕了請求。repository 可能是 private。",
    ["update.providerUnavailable"] = "發佈來源暫時無法使用，請稍後再試。",
    ["update.providerStatus"] = "發佈來源回應了 HTTP {status}。",
    ["update.rateLimited"] = "發佈來源正在限制這個網路的請求，請稍後再試。",
    ["update.offline"] = "連不上發佈來源，或連線中斷了。請檢查網路連線後再試。",
    ["update.cancelled"] = "更新已取消。",
    ["update.failed"] = "更新沒有完成，請稍後再試。",
  };
  // i18n:end
}
