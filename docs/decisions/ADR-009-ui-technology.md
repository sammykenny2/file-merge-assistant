# ADR-009：正式版的 UI 技術（D-03）

## 狀態

Proposed（草稿，待產品負責人決定）

## 背景

- 現行 GUI 使用 WPF（`net8.0-windows`），搭配 CommunityToolkit.Mvvm 與 Microsoft.Extensions.Hosting。畫面分成 Scan、Results、Reports 三個 View。核心邏輯在共用的 `src/Core` 類別庫，CLI 也使用同一套。
- 產品定位是 Windows 原生工具，沒有跨平台需求。
- [GUI 流程 mock](../gui-flow-mock.md) 需要的介面能力：
  - 數千個疊、數千列對照表的清單虛擬化與捲動。
  - 背景掃描時，介面仍保持回應。
  - 鍵盤操作、焦點順序與螢幕閱讀器支援。
  - Windows 整合：在檔案總管中開啟、照片縮圖、偵測磁碟接上與拔除。
- mock 是用 HTML 畫的，只用來呈現設計，不代表要採用網頁技術。

## 決策（提議）

- 正式版維持 WPF 與 MVVM，依新的資訊架構重寫 View；不沿用現有的 Scan／Results／Reports 分頁。
- 大量清單與對照表使用 WPF 的 UI 虛擬化。
- 先做一個技術驗證：用 [ADR-011](./ADR-011-acceptance-dataset-and-measurement.md) 的中型資料集，實作「工作台」與「一疊」兩個畫面，量測捲動、篩選與掃描進行中的回應時間。
- 驗證不達標時，重新評估這個決定，再比較下面的替代方案。

## 影響

### 正面

- 沿用現有的程式碼、工具鏈與團隊經驗，移植成本最低。
- 與 Windows 整合成熟，核心類別庫不需要調整。

### 負面

- 預設外觀較舊，要達到 mock 的視覺需要自訂樣式。
- 新的 Windows 視覺元件主要在 WinUI 推出，WPF 需要自行實作或使用第三方元件。

### 中性

- .NET 版本升級與 UI 技術無關，另見 [ADR-012](./ADR-012-release-packaging.md)。

## 考慮過的替代方案

### 替代方案一：WinUI 3（Windows App SDK）

- **優點**：現代 Windows 外觀與控制項。
- **缺點**：從 WPF 移植成本高；發佈時要處理 Windows App SDK 執行環境；工具鏈與控制項成熟度需要評估。
- **不採用的原因**：以目前需求來看，移植成本大於收益；若 WPF 驗證不達標，作為第一個候選。

### 替代方案二：Avalonia

- **優點**：XAML 寫法接近 WPF，可以跨平台。
- **缺點**：產品不需要跨平台；多一個第三方 UI 框架相依；Windows 專屬整合要另外處理。
- **不採用的原因**：跨平台的好處用不到。

### 替代方案三：網頁技術介面（WebView2 內嵌網頁，或 Electron 類框架）

- **優點**：版面設計自由，可以直接沿用 mock 的設計語言。
- **缺點**：多一套 JavaScript 技術與 C# 核心之間的通訊層；Electron 類框架的安裝檔與記憶體用量大。
- **不採用的原因**：增加的架構複雜度，大於版面自由度的好處。

## 待決定事項

- 是否接受「維持 WPF，先做技術驗證」。
- 技術驗證的達標標準，例如捲動與篩選的回應時間。

## 參考

- [GUI 流程 mock](../gui-flow-mock.md)
- [GUI 設計交接](../ux-design-brief.md)
- [產品需求：D-03](../requirements.md)

---

*提出日期：2026-10-23*
*決策日期：待定*
*決策者：產品負責人*
