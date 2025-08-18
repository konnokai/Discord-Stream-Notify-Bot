# Discord Stream Notify Bot Coordinator-Shard 架構轉換 PRD

## 文件資訊

**專案名稱**: Discord Stream Notify Bot Coordinator-Shard 架構轉換  
**文件類型**: 產品需求文件 (PRD)  
**版本**: 1.0  
**建立日期**: 2025年8月18日  
**最後更新**: 2025年8月18日 (新增 Docker 容器化需求)  
**建立者**: BMad Master  
**專案範圍**: Brownfield 架構現代化轉換  

---

## 專案概述

### 專案背景
Discord Stream Notify Bot 是一個多平台直播監控和通知系統，目前採用單體架構運行所有功能。隨著使用者和 Discord 伺服器數量增長，系統面臨擴展性和可維護性挑戰，需要轉換為 Coordinator-Shard 分散式架構。

### 核心問題
- **單體架構限制**: 所有平台爬蟲和 Discord Bot 功能在同一進程中，無法獨立擴充
- **缺乏 Shard 管理**: 目前手動管理多個 Discord Shard，無自動化生命週期管理
- **資源耦合**: 爬蟲邏輯和 Discord API 操作競爭相同的系統資源
- **故障影響範圍大**: 單一元件失效會影響整個系統運作

### 解決方案概述
將現有系統重構為三個獨立服務，並支援 Docker 容器化部署：
1. **Crawler 服務**: 專責所有平台的直播監控和會員驗證
2. **Discord Shard 實例**: 純 Discord Bot 功能，專注於使用者互動和通知發送
3. **Coordinator 服務**: 管理 Crawler 和多個 Discord Shard 的生命週期
4. **Docker 容器化**: 支援標準化容器部署和動態 Shard 縮放

---

## 技術現狀分析

### 當前技術棧
- **執行環境**: .NET 8.0
- **Discord 框架**: Discord.Net 3.17.2
- **資料庫**: MySQL + Entity Framework Core 9.0.3
- **快取系統**: Redis (PubSub 和狀態管理)
- **API 整合**: YouTube Data API v3, Twitch API, Twitter API, TwitCasting API

### 現有架構特徵
- **專案結構**: 單一 DiscordStreamNotifyBot.csproj 專案
- **核心服務**: SharedService/ 目錄包含所有平台爬蟲邏輯
- **Discord 互動**: Command/ 和 Interaction/ 處理使用者指令
- **資料存取**: DataBase/ 使用 EF Core Code-First 模式
- **服務間通訊**: 單進程內直接調用 + Redis PubSub 對外通訊

### 架構轉換挑戰
1. **服務分離複雜度**: 需要將緊耦合的爬蟲邏輯和 Discord 功能分離
2. **狀態同步**: 多個服務間的追蹤狀態和會員驗證狀態同步
3. **會員驗證路由**: Crawler 服務需要透過正確的 Discord Shard 執行 Guild 特定操作
4. **部署複雜度增加**: 從單一進程變為多服務協調管理

---

## 需求定義

### 功能性需求 (FR)

**FR1: 獨立 Crawler 服務**
- 建立專責的 StreamNotifyBot.Crawler 服務
- 統一管理所有平台 (YouTube, Twitch, Twitter, TwitCasting) 的直播監控
- 處理所有外部 API Webhook 回調 (YouTube PubSubHubbub, Twitch EventSub)
- 執行定時會員身份驗證和 API 配額管理

**FR2: Discord Shard 重構**
- 移除現有 SharedService/ 中的爬蟲邏輯
- 改為純事件驅動架構，監聽 Redis PubSub 事件
- 保持現有 Discord 指令介面不變，確保使用者體驗一致
- 使用 Discord Embed 格式統一回應使用者操作

**FR3: 服務間事件通訊**
- Crawler 服務透過 Redis PubSub 廣播直播狀態變化
- Discord Shard 透過 PubSub 通知 Crawler 追蹤管理請求
- 支援批量事件處理以提高通訊效率
- 維持與外部錄影工具的 Redis 事件相容性

**FR4: Coordinator 生命週期管理**
- 管理 Crawler 服務和多個 Discord Shard 進程
- 確保服務啟動順序：Crawler 先啟動並穩定，然後啟動 Discord Shard
- 提供 gRPC API 接受心跳回報和健康檢查
- 支援服務故障自動檢測和重啟機制
- 暴露 Prometheus 指標端點，提供系統監控數據

**FR5: 動態追蹤管理**
- 使用者新增/移除追蹤時，Discord Shard 即時通知 Crawler 服務
- Crawler 服務動態調整監控目標，避免不必要的 API 調用
- 支援全域追蹤計數器，當無 Guild 追蹤時停止該目標的爬蟲

**FR6: 會員驗證跨服務協調**
- 會員驗證邏輯從 Discord Shard 轉移至 Crawler 服務
- Crawler 透過 shard 路由計算，請求正確的 Discord Shard 執行 Guild 特定 API 操作
- 避免 "Guild not found on this shard" 錯誤
- 支援 OAuth2 token 集中管理和自動續期

**FR7: 資料庫架構保持不變**
- 繼續使用現有 MySQL 資料庫和 Entity Framework 配置
- 各服務獨立管理資料庫連接池
- 無需修改現有 Table 結構或 Migration 腳本

**FR8: Docker 容器化基礎設施**
- 建立 Coordinator、Crawler、Discord Shard 的 Docker 容器映像
- 建立統一的 Docker Compose 編排配置
- 支援外部 Redis 和 MySQL 服務連接
- 實作動態 Shard 縮放機制（基於 Discord Gateway 建議）
- 建立容器管理腳本和健康檢查機制

### 非功能性需求 (NFR)

**NFR1: 通知即時性**
- 接收到平台推送通知後，能立即發送 Discord 通知
- 不要求固定延遲時間，依據不同平台推送機制的自然延遲

**NFR2: 系統可用性**
- 單一服務故障不影響其他服務正常運作
- Coordinator 支援自動故障檢測和服務重啟
- 目標系統整體可用性 > 99%

**NFR3: 可擴展性**
- Discord Shard 數量使用 `BaseDiscordClient.GetRecommendedShardCountAsync()` 動態決定
- 支援根據 Discord Gateway 建議動態調整 Shard 數量
- Crawler 服務支援獨立資源配置和效能優化

**NFR4: 資源使用效率**
- Crawler 服務專注 CPU 密集型操作 (API 爬蟲、資料處理)
- Discord Shard 專注網路密集型操作 (WebSocket 連接、訊息發送)
- 記憶體使用最佳化，避免不必要的狀態重複

**NFR5: 日誌和監控**
- 關鍵操作使用結構化日誌輸出至 console
- 支援不同日誌等級 (INFO/WARN/ERROR) 區分事件嚴重程度
- 整合 Prometheus 指標暴露，提供詳細的系統監控數據
- 依賴 console 日誌和 tmux session 管理為主要監控方式

**NFR6: 部署簡化**
- 使用 tmux 背景進程管理，避免容器化複雜度
- 支援單機部署模式，無需分散式基礎設施
- 配置檔案使用 JSON 格式，與現有配置保持一致

**NFR7: Docker 容器化支援**
- 提供標準化的容器部署選項
- 支援動態 Shard 縮放（基於 Discord Gateway 建議）
- 容器間網路和服務發現自動化配置
- 外部 Redis 和 MySQL 服務整合

### 相容性需求 (CR)

**CR1: 外部錄影工具相容性**
- 維持現有 Redis PubSub 事件格式和頻道名稱
- 確保外部錄影工具無需任何修改即可繼續使用

**CR2: Discord 使用者介面相容性**
- 保持現有 Discord 指令介面不變
- 使用者無需學習新的操作方式
- 統一採用 Discord Embed 格式提升回應品質

**CR3: 資料庫結構相容性**
- 無需修改現有資料庫 Schema
- 支援現有資料無縫遷移
- 維持 Entity Framework Code-First 開發模式

**CR4: 配置管理相容性**
- 新服務配置格式與現有 `bot_config.json` 保持一致
- 支援現有 API 金鑰和連線字串格式
- 最小化配置檔案修改需求

**CR5: Docker 部署選項相容性**
- 提供 Docker 容器化部署作為額外選項
- 保持原有 tmux 部署方式的完整支援
- 兩種部署方式使用相同的配置格式

---

## 技術約束和限制

### 平台 API 限制
- **YouTube Data API v3**: 每日配額 10,000 單位，需要配額監控和多金鑰輪替策略
- **Twitch API**: Rate limit 為每分鐘 800 請求，需要請求排程機制
- **Twitter API**: 目前使用非官方 Cookie 認證方式，存在不穩定風險
- **TwitCasting API**: 請求頻率限制較為寬鬆，但需要處理間歇性服務中斷

### Discord API 約束
- **Gateway 連接限制**: 每個 shard 需要獨立 WebSocket 連接
- **Rate Limiting**: Discord API 有嚴格的 rate limit，需要在各 shard 間協調
- **Shard 數量計算**: 使用 `BaseDiscordClient.GetRecommendedShardCountAsync()` 動態決定 shard 數量
- **Guild 分配**: Guild 分配基於 Discord 的 shard 算法：`(guild_id >> 22) % num_shards`

### 基礎設施約束
- **Redis 依賴性**: 所有服務間通訊依賴 Redis PubSub，Redis 故障會導致整個系統失效
- **資料庫連接**: MySQL 連接池需要在各服務間合理分配，避免連接耗盡
- **記憶體使用**: 爬蟲服務需要維護大量追蹤目標的狀態資訊
- **網路頻寬**: 多個 shard 同時運行時的網路頻寬消耗需要考慮

### 部署約束
- **服務啟動順序**: Crawler 服務必須先啟動並穩定運行，Discord Shard 才能啟動
- **檔案系統依賴**: 配置檔案和日誌檔案需要本地檔案系統支援
- **進程管理**: 使用 tmux 進行背景進程管理，不使用容器化部署
- **單機部署限制**: 目前不支援多主機分散式部署

**Docker 容器化約束** (可選部署模式):
- **外部服務依賴**: 需要獨立的 Redis 和 MySQL 容器或服務
- **動態縮放權限**: Coordinator 容器需要 Docker socket 存取權限
- **網路配置**: 需要建立自定義 Docker 網路支援服務間通訊
- **儲存管理**: 配置檔案和持久化資料需要適當的 Volume 掛載

---

## 系統架構設計

### 服務架構概覽
```
┌─────────────────────┐    ┌─────────────────────┐    ┌─────────────────────┐
│   Coordinator       │    │   Crawler Service   │    │  Discord Shard 1-N  │
│   Service           │    │                     │    │                     │
│  ┌───────────────┐  │    │  ┌───────────────┐  │    │  ┌───────────────┐  │
│  │ gRPC Server   │◄─┼────┼──┤ gRPC Client   │  │    │  │ gRPC Client   │  │
│  │               │  │    │  │               │  │    │  │               │  │
│  │ Process Mgr   │  │    │  │ Platform      │  │    │  │ Event         │  │
│  │               │  │    │  │ Monitors      │  │    │  │ Listeners     │  │
│  │ Health Check  │  │    │  │               │  │    │  │               │  │
│  └───────────────┘  │    │  │ Member        │  │    │  │ Command       │  │
└─────────────────────┘    │  │ Verification  │  │    │  │ Handlers      │  │
                           │  │               │  │    │  └───────────────┘  │
                           │  └───────────────┘  │    └─────────────────────┘
                           └─────────────────────┘
                                    │ ▲                           │ ▲
                                    ▼ │                           ▼ │
┌─────────────────────────────────────────────────────────────────────────────┐
│                            Redis PubSub                                    │
│  Channels: streams.online, streams.offline, stream.follow,                 │
│           stream.unfollow, shard-request:{N}, member.revokeToken           │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │ ▲
                                    ▼ │
┌─────────────────────────────────────────────────────────────────────────────┐
│                          MySQL Database                                    │
│  Tables: FollowedStreams, StreamData, GuildConfig, YoutubeMember*          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 核心服務職責

**Coordinator 服務**:
- 管理 Crawler 服務和 Discord Shard 進程生命週期
- 接收各服務的 gRPC 心跳回報
- 執行健康檢查和自動故障恢復
- 提供統一的服務管理 API

**Crawler 服務**:
- 執行所有平台的直播監控 (YouTube, Twitch, Twitter, TwitCasting)
- 處理外部 API Webhook 回調
- 管理會員驗證流程，透過 shard 路由請求 Discord API 操作
- 透過 Redis PubSub 廣播直播狀態變化

**Discord Shard 實例**:
- 處理 Discord 使用者指令和互動
- 監聽 Redis PubSub 事件發送通知
- 協助 Crawler 執行特定 Guild 的 Discord API 操作
- 使用 Discord Embed 格式回應使用者

### 服務間通訊協定

**gRPC 通訊 (Coordinator ↔ Services)**:
```protobuf
service Coordinator {
  rpc Heartbeat(HeartbeatRequest) returns (HeartbeatReply);
  rpc GetStatus(GetStatusRequest) returns (GetStatusReply);
  rpc RestartService(RestartServiceRequest) returns (RestartServiceReply);
}

message HeartbeatRequest {
  string service_id = 1;
  int32 shard_id = 2;  // Discord Shard only
  int32 guild_count = 3;
  string connection_state = 4;
}
```

**Redis PubSub 事件通訊**:
- `streams.online` / `streams.offline`: Crawler → Discord Shard 直播狀態廣播
- `stream.follow` / `stream.unfollow`: Discord Shard → Crawler 追蹤管理
- `shard-request:{shardId}`: Crawler → 特定 Discord Shard API 請求
- 保持現有錄影工具事件格式相容性

---

## 實作計劃

### Phase 1: Discord Bot 重構 (2週)

**目標**: 移除爬蟲邏輯，改為純事件驅動架構

**主要任務**:
1. **SharedService 重構**
   - 移除 `SharedService/Youtube/`, `Twitch/`, `Twitter/`, `Twitcasting/` 中的爬蟲邏輯
   - 保留 `EmojiService.cs` 等輔助服務
   - 新增 `EventHandlers/` 處理 Redis PubSub 事件

2. **事件監聽系統**
   - 實作 `StreamEventListener` 監聽直播狀態變化事件
   - 建立事件路由機制，根據 Guild 判斷通知需求
   - 實作 `ShardRequestHandler` 處理 Crawler 的 API 請求

3. **指令系統調整**
   - 修改追蹤管理指令透過 Redis PubSub 通知 Crawler
   - 統一使用 Discord Embed 格式回應使用者
   - 保持現有指令介面不變

**驗收標準**:
- Discord Bot 不再執行任何爬蟲邏輯
- 所有使用者指令正常運作並使用 Embed 回應
- 成功監聽並處理 Redis PubSub 事件
- 單元測試覆蓋率 > 80%

### Phase 2: Crawler 服務建立 (3週)

**目標**: 建立獨立 StreamNotifyBot.Crawler 專案

**主要任務**:
1. **專案架構建立**
   ```
   StreamNotifyBot.Crawler/
   ├── Program.cs
   ├── CrawlerService.cs
   ├── PlatformMonitors/
   │   ├── YoutubeMonitor.cs
   │   ├── TwitchMonitor.cs
   │   ├── TwitterMonitor.cs
   │   └── TwitCastingMonitor.cs
   ├── WebhookHandlers/
   ├── MemberVerification/
   └── Configuration/
   ```

2. **爬蟲邏輯遷移**
   - 將現有 SharedService 中的爬蟲邏輯完整遷移
   - 實作統一的 `StreamMonitor` 管理所有平台監控
   - 建立 `TrackingManager` 處理動態追蹤目標管理

3. **會員驗證系統**
   - 實作 `MemberVerificationService` 定時驗證會員身份
   - 建立 shard 路由計算邏輯：`(guild_id >> 22) % total_shards`
   - 透過 Redis PubSub 請求特定 Shard 執行 Discord API 操作

4. **事件廣播機制**
   - 實作直播狀態變化的批量事件廣播
   - 維持與外部錄影工具的事件格式相容性
   - 實作 API 配額監控和錯誤處理

**驗收標準**:
- 成功監控所有平台的直播狀態
- 會員驗證功能正常運作，無 shard 路由錯誤
- 事件廣播機制運作正常
- API 錯誤處理和重試機制完善

### Phase 3: Coordinator 實作與監控整合 (2.5週)

**目標**: 建立統一服務管理、監控和 Prometheus 指標暴露

**主要任務**:
1. **gRPC 服務實作**
   - 實作 Coordinator gRPC 服務接收心跳和狀態回報
   - 建立服務註冊和健康檢查機制
   - 實作服務重啟和狀態查詢 API

2. **進程管理系統**
   - 建立 `ProcessManager` 管理服務生命週期
   - 實作服務啟動順序控制：Crawler → Discord Shard
   - 建立 tmux session 管理 script

3. **配置管理**
   - 實作 YAML 配置檔案支援
   - 建立環境變數替換機制
   - 實作配置熱重載功能

4. **監控和日誌**
   - 實作結構化日誌輸出
   - 建立服務狀態監控面板（console 輸出）
   - 實作異常通知機制（Discord Webhook）

5. **Prometheus 監控整合**
   - 整合 Prometheus 指標收集中介軟體
   - 實作系統、服務管理、Discord 和 Crawler 指標收集
   - 建立 Grafana 儀表板和告警規則範例
   - 暴露標準 `/metrics` HTTP 端點

**驗收標準**:
- Coordinator 能成功管理所有服務生命週期
- 服務故障時能自動檢測和重啟
- 配置管理系統運作正常
- 監控日誌清楚記錄系統狀態
- Prometheus 指標正確暴露且 Grafana 儀表板可正常顯示監控數據

---

## 風險評估

### 高風險項目

**R1: 會員驗證跨服務協調複雜度**
- **風險**: Crawler 服務請求錯誤 Shard 執行 Discord API 操作
- **影響**: 會員驗證功能失效，使用者無法存取會限內容
- **緩解措施**: 
  - 詳細測試 shard 路由計算邏輯
  - 實作 API 請求重試和錯誤處理機制
  - 建立 shard 健康檢查，確保目標 shard 正常運行

**R2: Redis PubSub 可靠性依賴**
- **風險**: Redis 服務中斷導致服務間通訊失效
- **影響**: 直播通知停止，追蹤管理功能失效
- **緩解措施**:
  - 實作 Redis 連接重試機制
  - 建立本地狀態快取，減少對 Redis 的依賴
  - 定期資料庫同步確保狀態一致性

**R3: 服務啟動順序依賴**
- **風險**: Crawler 服務未完全啟動前 Discord Shard 嘗試連接
- **影響**: Discord Shard 無法正常接收事件，功能異常
- **緩解措施**:
  - 實作健康檢查端點確認 Crawler 服務就緒
  - Coordinator 嚴格控制服務啟動順序
  - Discord Shard 實作連接重試機制

### 中風險項目

**R4: API 配額管理複雜化**
- **風險**: 多服務架構下 API 配額統計和限制更加複雜
- **影響**: API 超限導致監控功能暫停
- **緩解措施**:
  - Crawler 服務集中管理所有 API 配額
  - 實作配額預警和自動降頻機制

**R5: 資料庫連接池管理**
- **風險**: 多服務同時存取資料庫可能導致連接耗盡
- **影響**: 服務無法存取資料庫，功能停止
- **緩解措施**:
  - 各服務獨立配置合適的連接池大小
  - 實作連接池監控和告警機制

---

## 成功標準

### 功能成功標準
1. **服務分離完成**: 三個獨立服務正常運行，職責清楚分工
2. **功能相容性**: 所有現有功能正常運作，使用者體驗無差異
3. **擴展性提升**: 支援動態調整 Discord Shard 數量
4. **資源使用最佳化**: CPU 和記憶體使用效率提升 20%

### 技術成功標準
1. **系統可用性**: 整體可用性達到 99% 以上
2. **故障恢復時間**: 單一服務故障後 30 秒內自動恢復
3. **通知延遲**: 接收平台推送後 5 秒內發送 Discord 通知
4. **程式碼品質**: 單元測試覆蓋率 > 80%，程式碼重複率 < 10%

### 營運成功標準
1. **部署簡化**: 新架構部署時間不超過舊架構 20%
2. **監控完善**: 關鍵操作日誌覆蓋率 100%
3. **文件完整**: 技術文件和操作手冊完整更新
4. **團隊接受度**: 開發團隊對新架構滿意度 > 80%

---

## Epic 和 Story 分解

### Epic 概覽

| Epic | 名稱 | 預估時間 | 優先級 | 風險級別 | 依賴關係 |
|------|------|----------|--------|----------|----------|
| Epic 1 | Discord Bot 重構與事件驅動改造 | 2週 | 高 | 中 | 無 |
| Epic 2 | 獨立 Crawler 服務建立 | 3週 | 高 | 高 | Epic 1 |
| Epic 3 | Coordinator 服務實作與監控整合 | 2.5週 | 中 | 中 | Epic 2 (部分平行) |
| Epic 4 | Docker 容器化基礎設施建立 | 2週 | 中 | 低 | Epic 3 |

**總預估時間**: 9.5週  
**關鍵里程碑**: Week 2, Week 5, Week 7.5, Week 9.5

### Epic 1: Discord Bot 重構與事件驅動改造

#### Story 1.1: SharedService 爬蟲邏輯移除
- **工作量**: 3 天
- **驗收標準**:
  - [ ] 移除 `SharedService/Youtube/YoutubeStreamService.cs` 中的 Timer 和爬蟲邏輯
  - [ ] 移除 `SharedService/Twitch/TwitchService.cs` 中的定時監控程式碼
  - [ ] 移除 `SharedService/Twitter/TwitterSpacesService.cs` 中的輪詢機制
  - [ ] 移除 `SharedService/Twitcasting/TwitcastingService.cs` 中的爬蟲功能
  - [ ] 保留 `EmojiService.cs` 等輔助服務

#### Story 1.2: Redis PubSub 事件監聽器建立
- **工作量**: 4 天
- **驗收標準**:
  - [ ] 建立 `EventHandlers/StreamEventListener.cs` 監聽直播狀態事件
  - [ ] 建立 `EventHandlers/ShardRequestHandler.cs` 處理 Crawler 的 API 請求
  - [ ] 建立事件路由機制，根據 Guild 判斷通知需求
  - [ ] 實作事件反序列化和錯誤處理

#### Story 1.3: Discord 指令系統 Embed 回應重構
- **工作量**: 3 天
- **驗收標準**:
  - [ ] 修改 `Command/` 目錄下所有指令使用 Embed 回應
  - [ ] 修改 `Interaction/` 目錄下所有斜線指令使用 Embed 回應
  - [ ] 建立統一的 Embed 樣式和顏色配置（成功=綠色、錯誤=紅色）
  - [ ] 實作錯誤處理 Embed 格式化

#### Story 1.4: 追蹤管理指令 PubSub 整合
- **工作量**: 3 天
- **驗收標準**:
  - [ ] 修改 YouTube 追蹤指令發送 `stream.follow`/`stream.unfollow` 事件
  - [ ] 修改其他平台追蹤指令整合 PubSub 通知
  - [ ] 實作追蹤資料序列化和事件格式標準化

### Epic 2: 獨立 Crawler 服務建立

#### Story 2.1: Crawler 專案架構建立
- **工作量**: 2 天
- **驗收標準**:
  - [ ] 建立 `StreamNotifyBot.Crawler/` 專案目錄結構
  - [ ] 建立 `Program.cs` 主進入點和依賴注入配置
  - [ ] 配置 Entity Framework、Redis、HTTP Client 服務註冊

#### Story 2.2: YouTube 爬蟲邏輯遷移
- **工作量**: 4 天
- **驗收標準**:
  - [ ] 建立 `PlatformMonitors/YoutubeMonitor.cs` 包含所有 YouTube API 邏輯
  - [ ] 遷移 YouTube Data API v3 配額管理機制
  - [ ] 遷移 YouTube PubSubHubbub Webhook 處理邏輯
  - [ ] 實作 YouTube 直播狀態檢測和變化偵測

#### Story 2.3: 其他平台爬蟲邏輯遷移
- **工作量**: 5 天
- **驗收標準**:
  - [ ] 建立 `PlatformMonitors/TwitchMonitor.cs` 包含 Twitch API 和 EventSub
  - [ ] 建立 `PlatformMonitors/TwitterMonitor.cs` 包含 Twitter Spaces 監控
  - [ ] 建立 `PlatformMonitors/TwitCastingMonitor.cs` 包含 TwitCasting API
  - [ ] 實作統一的平台監控介面 `IPlatformMonitor`

#### Story 2.4: 會員驗證跨服務協調系統
- **工作量**: 4 天
- **驗收標準**:
  - [ ] 建立 `MemberVerification/MemberVerificationService.cs`
  - [ ] 實作 shard 路由計算邏輯：`(guild_id >> 22) % total_shards`
  - [ ] 建立 `shard-request:{shardId}` PubSub 請求機制
  - [ ] 實作 OAuth2 token 集中管理和自動續期

#### Story 2.5: 事件廣播和追蹤管理系統
- **工作量**: 3 天
- **驗收標準**:
  - [ ] 建立 `TrackingManager.cs` 管理全域追蹤計數器
  - [ ] 實作 `stream.follow`/`stream.unfollow` 事件處理
  - [ ] 建立批量事件廣播機制（`streams.online`/`streams.offline`）
  - [ ] 維持外部錄影工具事件格式相容性

#### Story 2.6: gRPC 客戶端和健康檢查
- **工作量**: 2 天
- **驗收標準**:
  - [ ] 建立 gRPC 客戶端連接 Coordinator
  - [ ] 實作心跳回報機制（服務狀態、爬蟲計數、API 配額狀態）
  - [ ] 建立 HTTP 健康檢查端點 `/health`
  - [ ] 建立優雅關閉機制

### Epic 3: Coordinator 服務實作與監控整合

#### Story 3.1: Coordinator gRPC 服務建立
- **工作量**: 3 天
- **驗收標準**:
  - [ ] 建立 `StreamNotifyBot.Coordinator/` 專案
  - [ ] 定義 `coordinator.proto` gRPC 服務定義
  - [ ] 實作 `CoordinatorService.cs` gRPC 服務端
  - [ ] 建立服務註冊和狀態追蹤機制

#### Story 3.2: 進程生命週期管理系統
- **工作量**: 4 天
- **驗收標準**:
  - [ ] 建立 `ProcessManager.cs` 管理服務進程
  - [ ] 實作服務啟動順序控制（Crawler → Discord Shard）
  - [ ] 建立進程健康檢查和故障檢測
  - [ ] 支援動態 Discord Shard 數量管理

#### Story 3.3: YAML 配置管理系統
- **工作量**: 3 天
- **驗收標準**:
  - [ ] 建立 `coord.yml` 配置檔案格式定義
  - [ ] 實作 YAML 配置解析和驗證
  - [ ] 建立環境變數替換機制（`{{variable}}` 語法）
  - [ ] 支援服務配置熱重載

#### Story 3.4: tmux 部署腳本和監控介面
- **工作量**: 4 天
- **驗收標準**:
  - [ ] 建立 `start-services.sh` tmux session 管理腳本
  - [ ] 建立 `stop-services.sh` 優雅關閉腳本
  - [ ] 實作 console 監控介面顯示所有服務狀態
  - [ ] 建立結構化日誌輸出（JSON 格式）
  - [ ] 實作 Discord Webhook 異常通知機制

#### Story 3.5: Prometheus 指標監控整合
- **工作量**: 5 天
- **驗收標準**:
  - [ ] 整合 Prometheus 指標收集中介軟體暴露 `/metrics` 端點
  - [ ] 實作系統層級指標收集（運行時間、CPU、記憶體、GC 統計）
  - [ ] 實作服務管理指標（託管服務狀態、重啟次數、心跳統計）
  - [ ] 實作 Discord 生態系統指標（Shard 狀態、Guild 數量、延遲統計）
  - [ ] 實作 Crawler 服務指標（監控直播數量、API 配額使用、事件廣播統計）
  - [ ] 建立 Grafana 儀表板範例和告警規則配置

### Epic 4: Docker 容器化基礎設施建立

#### Story 4.1: Docker 容器化基礎設施建立
- **工作量**: 2週
- **驗收標準**:
  - [ ] 建立 Coordinator、Crawler、Discord Shard 的 Dockerfile
  - [ ] 建立統一的 Docker Compose 編排配置
  - [ ] 實作外部 Redis 和 MySQL 服務連接
  - [ ] 實作動態 Shard 縮放機制（基於 Discord Gateway 建議）
  - [ ] 建立容器網路和服務發現配置
  - [ ] 建立容器管理腳本和健康檢查機制
  - [ ] 實作多階段建置 Dockerfile 優化
  - [ ] 建立 Prometheus 指標暴露和監控整合
  - [ ] 建立測試和驗證工具

**核心技術特色**:
- **智慧型 Shard 縮放**: Coordinator 透過 Discord Gateway API 自動決定 Shard 數量
- **外部服務整合**: 支援獨立的 Redis 和 MySQL 容器
- **Docker Socket 存取**: Coordinator 容器掛載 Docker socket 實現動態縮放
- **健康檢查機制**: 所有服務提供標準化健康檢查端點
- **安全最佳實踐**: 非 root 使用者、網路隔離、資源限制

### 測試策略

#### 單元測試要求
- **覆蓋率目標**: > 80%
- **測試框架**: xUnit + Moq
- **重點測試區域**: 事件處理邏輯、API 調用和錯誤處理、服務間通訊協定

#### 整合測試計劃
- **跨服務通訊測試**: Redis PubSub 事件流程
- **資料庫整合測試**: Entity Framework 操作
- **外部 API 測試**: 模擬各平台 API 回應
- **gRPC 通訊測試**: Coordinator 和服務間通訊

#### 效能測試需求
- **負載測試**: 模擬多 Guild 環境測試系統效能
- **壓力測試**: 大量併發直播狀態變化處理
- **記憶體測試**: 長時間運行的記憶體洩漏檢測

#### 故障測試 (Chaos Engineering)
- **服務故障模擬**: 模擬 Crawler 或 Discord Shard 故障
- **網路分割測試**: Redis 連接中斷情況
- **資料庫故障**: 資料庫不可用時的處理
- **API 限制測試**: 各平台 API 超限情況

### 部署策略

#### 環境需求
- **.NET 8.0 Runtime**
- **Redis Server**
- **MySQL/MariaDB**
- **tmux** (進程管理)

#### 部署順序
1. **環境準備**: 資料庫、Redis、配置檔案
2. **Coordinator 部署**: 啟動服務管理中心
3. **自動化部署**: 透過 Coordinator 啟動其他服務
4. **驗證測試**: 確認所有服務正常運行

**Docker 容器化部署** (可選):
1. **外部服務準備**: 獨立啟動 Redis 和 MySQL 容器
2. **網路建立**: 建立 Docker 自定義網路
3. **映像建置**: 建置所有服務的 Docker 映像
4. **服務編排**: 使用 Docker Compose 啟動完整系統
5. **動態縮放**: 透過智慧型腳本自動調整 Shard 數量

#### 回滾計劃
- **快速回滾**: 保留舊版本執行檔
- **資料庫相容**: 確保資料庫結構向下相容
- **配置還原**: 備份舊版配置檔案

---

## 附錄

### 相關文件
- [Discord Stream Notify Bot 棕地架構文件](./brownfield-architecture.md)
- [Epic & Stories 詳細文件](./epic-stories.md)
- 現有程式碼庫: `c:\Users\user\source\repos\_konnokai\DiscordStreamNotifyBot`

### 技術參考
- [Discord.Net 文件](https://docs.stillu.cc/)
- [NadekoBot Coordinator 實作參考](https://github.com/Kwoth/NadekoBot)
- [gRPC .NET 官方文件](https://docs.microsoft.com/en-us/aspnet/core/grpc/)

### 配置範例

**Coordinator 配置 (coord.yml)**:
```yaml
services:
  crawler:
    type: "crawler"
    command: "dotnet"
    args: "run --project StreamNotifyBot.Crawler"
    healthCheck:
      endpoint: "http://localhost:6111/health"
      timeoutMs: 5000
    dependencies: []
    
  discordShards:
    type: "discordShard" 
    totalShards: "dynamic"  # 使用 GetRecommendedShardCountAsync()
    command: "dotnet"
    args: "run --project DiscordStreamNotifyBot {shardId} {totalShards}"
    dependencies: ["crawler"]
    
monitoring:
  recheckIntervalMs: 2000
  unresponsiveSec: 30
  autoRestart: true
```

**服務配置範例**:
```json
{
  "redis": {
    "connectionString": "localhost:6379"
  },
  "database": {
    "connectionString": "Server=localhost;Database=StreamNotify;..."
  },
  "coordinator": {
    "grpcEndpoint": "http://localhost:6110"
  },
  "platforms": {
    "youtube": {
      "apiKeys": ["key1", "key2"],
      "quotaLimit": 10000
    },
    "twitch": {
      "clientId": "...",
      "clientSecret": "..."
    }
  }
}
```

**Docker Compose 配置範例**:
```yaml
services:
  coordinator:
    build: ./StreamNotifyBot.Coordinator
    ports:
      - "6110:6110"   # gRPC
      - "6112:6112"   # HTTP Health/Metrics
    environment:
      - DISCORD_TOKEN=${DISCORD_TOKEN}
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock  # 動態 Shard 縮放
    restart: unless-stopped

  crawler:
    build: ./StreamNotifyBot.Crawler
    ports:
      - "6111:6111"   # Health Check
    environment:
      - COORDINATOR_GRPC_ENDPOINT=coordinator:6110
      - YOUTUBE_API_KEYS=${YOUTUBE_API_KEYS}
    depends_on:
      - coordinator
    restart: unless-stopped

  discord-shard:
    build: ./DiscordStreamNotifyBot
    environment:
      - COORDINATOR_GRPC_ENDPOINT=coordinator:6110
      - DISCORD_TOKEN=${DISCORD_TOKEN}
      - SHARD_ID=${SHARD_ID:-0}
      - TOTAL_SHARDS=${TOTAL_SHARDS:-1}
    depends_on:
      - crawler
    restart: unless-stopped

networks:
  default:
    name: ${NETWORK_NAME:-streamnotify-network}
    external: true
```

**智慧型 Shard 縮放腳本**:
```bash
#!/bin/bash
# scripts/docker-scale.sh

if [ "$1" = "auto" ]; then
    # 透過 Coordinator API 取得 Discord Gateway 建議的 Shard 數量
    RECOMMENDED_SHARDS=$(curl -s http://localhost:6112/api/recommended-shard-count)
    docker-compose up -d --scale discord-shard=$RECOMMENDED_SHARDS
else
    # 手動指定 Shard 數量
    docker-compose up -d --scale discord-shard=${1:-3}
fi
```

---

**文件結束**

本 PRD 將作為 Discord Stream Notify Bot Coordinator-Shard 架構轉換專案的完整需求規範，指導開發團隊進行系統重構和現代化升級。
