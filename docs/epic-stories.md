# Discord Stream Notify Bot Coordinator-Shard 架構轉換 Epic & Stories

## 文件資訊

**專案名稱**: Discord Stream Notify Bot Coordinator-Shard 架構轉換  
**文件類型**: Epic & Stories 分解文件  
**版本**: 1.0  
**建立日期**: 2025年8月18日  
**建立者**: BMad Master  
**相關 PRD**: [brownfield-prd.md](./brownfield-prd.md)

---

## Epic 概覽

| Epic | 名稱 | 預估時間 | 優先級 | 風險級別 | 依賴關係 |
|------|------|----------|--------|----------|----------|
| Epic 1 | Discord Bot 重構與事件驅動改造 | 2週 | 高 | 中 | 無 |
| Epic 2 | 獨立 Crawler 服務建立 | 3週 | 高 | 高 | Epic 1 |
| Epic 3 | Coordinator 服務實作與監控整合 | 2.5週 | 中 | 中 | Epic 2 (部分平行) |

**總預估時間**: 7.5週  
**關鍵里程碑**: Week 2, Week 5, Week 7.5

---

## Epic 1: Discord Bot 重構與事件驅動改造

### Epic 1 描述
**目標**: 移除現有 Discord Bot 中的爬蟲邏輯，將其重構為純事件驅動架構，專注於 Discord 使用者互動和通知發送功能。

**商業價值**: 
- 提升系統模組化程度，降低維護複雜度
- 為後續 Crawler 服務分離奠定基礎
- 改善使用者體驗（Discord Embed 回應）

**成功標準**:
- Discord Bot 不再執行任何爬蟲邏輯
- 所有使用者指令正常運作並使用 Embed 回應
- 成功監聽並處理 Redis PubSub 事件
- 單元測試覆蓋率 > 80%

---

### Story 1.1: SharedService 爬蟲邏輯移除

**Story ID**: DSNT-1.1  
**優先級**: 高  
**工作量**: 3 天  
**負責人**: [待分配]

#### 任務描述
移除 SharedService/ 目錄中所有平台的爬蟲相關程式碼，為事件驅動架構做準備。

#### 驗收標準
- [ ] 移除 `SharedService/Youtube/YoutubeStreamService.cs` 中的 Timer 和爬蟲邏輯
  - 保留 YouTube API 調用方法（供 Crawler 服務重用）
  - 移除所有定時執行的背景任務
- [ ] 移除 `SharedService/Twitch/TwitchService.cs` 中的定時監控程式碼
  - 保留 Twitch API 整合邏輯
  - 移除 EventSub 訂閱管理（轉移至 Crawler）
- [ ] 移除 `SharedService/Twitter/TwitterSpacesService.cs` 中的輪詢機制
  - 保留 Twitter API 基礎方法
  - 移除定時檢查 Spaces 狀態的邏輯
- [ ] 移除 `SharedService/Twitcasting/TwitcastingService.cs` 中的爬蟲功能
  - 保留 API 調用封裝
  - 移除背景監控任務
- [ ] 保留 `EmojiService.cs` 和其他輔助服務
- [ ] 更新依賴注入配置，移除已刪除服務的註冊
- [ ] 確保移除程式碼不影響現有 Discord 指令功能

#### 技術需求
- 程式碼重構經驗
- 熟悉現有 SharedService 架構
- 依賴注入 (DI) 配置管理

#### 風險與緩解
- **風險**: 意外移除必要的共用邏輯
- **緩解**: 仔細識別並保留 API 調用方法，供未來重用

---

### Story 1.2: Redis PubSub 事件監聽器建立

**Story ID**: DSNT-1.2  
**優先級**: 高  
**工作量**: 4 天  
**負責人**: [待分配]  
**前置依賴**: Story 1.1

#### 任務描述
建立統一的 Redis PubSub 事件監聽和處理系統，為接收 Crawler 服務事件做準備。

#### 驗收標準
- [ ] 建立 `EventHandlers/StreamEventListener.cs` 監聽直播狀態事件
  - 實作 `streams.online` 事件處理器
  - 實作 `streams.offline` 事件處理器
  - 支援批量事件處理（多個直播同時開關台）
- [ ] 建立 `EventHandlers/ShardRequestHandler.cs` 處理 Crawler 的 API 請求
  - 監聽 `shard-request:{shardId}` 頻道
  - 實作 Discord API 代理執行機制
  - 支援會員驗證相關的 Guild 操作
- [ ] 建立事件路由邏輯，根據 Guild 判斷是否需要發送通知
  - 檢查 Guild 是否屬於當前 Shard
  - 查詢 Guild 的通知設定
  - 過濾不需要通知的事件
- [ ] 實作事件反序列化和錯誤處理
  - JSON 事件資料反序列化
  - 處理格式錯誤的事件
  - 記錄事件處理失敗的詳細日誌
- [ ] 建立 Redis 連接管理和重連機制
  - Redis 連接中斷自動重連
  - PubSub 訂閱失敗重試
  - 連接狀態監控

#### 技術需求
- Redis PubSub 程式設計
- 事件驅動架構設計
- JSON 序列化/反序列化
- 錯誤處理和重試機制

#### 測試需求
- 單元測試覆蓋率 > 80%
- Redis PubSub 事件模擬測試
- 錯誤情況處理測試

---

### Story 1.3: Discord 指令系統 Embed 回應重構

**Story ID**: DSNT-1.3  
**優先級**: 中  
**工作量**: 3 天  
**負責人**: [待分配]  
**前置依賴**: 無（可平行進行）

#### 任務描述
將所有使用者指令回應改為統一的 Discord Embed 格式，提升使用者體驗。

#### 驗收標準
- [ ] 修改 `Command/` 目錄下所有指令使用 Embed 回應
  - 更新所有 YouTube 相關指令
  - 更新所有 Twitch 相關指令
  - 更新所有 Twitter 相關指令
  - 更新所有 TwitCasting 相關指令
  - 更新管理員指令和幫助指令
- [ ] 修改 `Interaction/` 目錄下所有斜線指令使用 Embed 回應
  - 所有斜線指令統一使用 Embed 格式
  - 保持指令功能和參數不變
- [ ] 建立統一的 Embed 樣式和顏色配置
  - 成功操作：綠色 (#00ff00)
  - 錯誤訊息：紅色 (#ff0000)
  - 資訊顯示：藍色 (#0099ff)
  - 警告訊息：橘色 (#ff9900)
- [ ] 實作錯誤處理 Embed 格式化
  - 統一錯誤訊息 Embed 樣式
  - 包含錯誤代碼和解決建議
  - 友善的使用者錯誤說明
- [ ] 確保所有平台追蹤指令保持現有介面不變
  - 指令名稱和參數保持一致
  - 功能邏輯完全相同
  - 只改變回應格式為 Embed

#### 技術需求
- Discord.Net Embed API
- 統一樣式設計
- 使用者體驗設計

#### 設計規範
```csharp
// Embed 樣式標準
public static class EmbedStyles
{
    public static readonly Color Success = new Color(0, 255, 0);
    public static readonly Color Error = new Color(255, 0, 0);
    public static readonly Color Info = new Color(0, 153, 255);
    public static readonly Color Warning = new Color(255, 153, 0);
    
    public static EmbedBuilder CreateSuccess(string title, string description)
    {
        return new EmbedBuilder()
            .WithColor(Success)
            .WithTitle($"✅ {title}")
            .WithDescription(description)
            .WithTimestamp(DateTimeOffset.Now);
    }
}
```

---

### Story 1.4: 追蹤管理指令 PubSub 整合

**Story ID**: DSNT-1.4  
**優先級**: 高  
**工作量**: 3 天  
**負責人**: [待分配]  
**前置依賴**: Story 1.2 (事件監聽器)

#### 任務描述
修改追蹤新增/移除指令透過 Redis PubSub 通知 Crawler 服務，建立服務間通訊機制。

#### 驗收標準
- [ ] 修改 YouTube 追蹤指令發送 `stream.follow`/`stream.unfollow` 事件
  - `/youtube follow` 指令發送 follow 事件
  - `/youtube unfollow` 指令發送 unfollow 事件
  - 包含完整的追蹤目標資訊（頻道 ID、Guild ID、Channel ID）
- [ ] 修改 Twitch 追蹤指令整合 PubSub 通知
  - Twitch 使用者追蹤/取消追蹤事件
  - 支援 Twitch 頻道 ID 和使用者名稱解析
- [ ] 修改 Twitter 追蹤指令整合 PubSub 通知  
  - Twitter Spaces 追蹤事件
  - 處理 Twitter 使用者 ID 和 handle 對應
- [ ] 修改 TwitCasting 追蹤指令整合 PubSub 通知
  - TwitCasting 使用者追蹤事件
  - 支援使用者 ID 和螢幕名稱
- [ ] 實作追蹤資料序列化和事件格式標準化
  - 定義統一的事件資料格式
  - JSON 序列化追蹤目標資訊
  - 包含必要的元數據（時間戳記、操作者）
- [ ] 保持資料庫操作邏輯不變
  - 指令執行時仍然寫入資料庫
  - PubSub 事件為額外的通知機制
  - 確保資料一致性

#### 技術需求
- Redis PubSub 發布機制
- 事件資料模型設計
- JSON 序列化
- 資料一致性保證

#### 事件格式定義
```csharp
// 追蹤事件資料格式
public class StreamFollowEvent
{
    public string Platform { get; set; } // youtube, twitch, twitter, twitcasting
    public string StreamKey { get; set; } // 平台特定的識別符
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong UserId { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Metadata { get; set; }
}
```

#### 測試需求
- 事件發送功能測試
- 資料序列化正確性測試
- Redis 連接失敗處理測試

---

## Epic 2: 獨立 Crawler 服務建立

### Epic 2 描述
**目標**: 建立專責的 StreamNotifyBot.Crawler 獨立服務，統一管理所有平台的直播監控、會員驗證和 API 管理。

**商業價值**:
- 實現服務職責分離，提升系統模組化
- 支援獨立擴展和資源優化
- 降低 Discord Bot 負載，提升穩定性

**成功標準**:
- 成功監控所有平台的直播狀態
- 會員驗證功能正常運作，無 shard 路由錯誤
- 事件廣播機制運作正常
- API 錯誤處理和重試機制完善

---

### Story 2.1: Crawler 專案架構建立

**Story ID**: DSNT-2.1  
**優先級**: 高  
**工作量**: 2 天  
**負責人**: [待分配]  
**前置依賴**: Epic 1 完成

#### 任務描述
建立新的 StreamNotifyBot.Crawler 專案和基礎架構，準備承接爬蟲邏輯。

#### 驗收標準
- [ ] 建立 `StreamNotifyBot.Crawler/` 專案目錄結構
  ```
  StreamNotifyBot.Crawler/
  ├── StreamNotifyBot.Crawler.csproj
  ├── Program.cs
  ├── CrawlerService.cs
  ├── PlatformMonitors/
  ├── WebhookHandlers/
  ├── MemberVerification/
  ├── Configuration/
  ├── Models/
  └── Services/
  ```
- [ ] 建立 `Program.cs` 主進入點和依賴注入配置
  - ASP.NET Core Host 配置
  - 服務生命週期管理
  - 配置檔案載入
- [ ] 建立 `CrawlerService.cs` 主服務類別
  - 實作 `IHostedService` 介面
  - 服務啟動和停止邏輯
  - 各平台監控器管理
- [ ] 設定 NuGet 套件依賴
  - Entity Framework Core 9.0.3
  - StackExchange.Redis
  - Google.Apis.YouTube.v3
  - 其他平台 API 套件
- [ ] 配置 Entity Framework、Redis、HTTP Client 服務註冊
  - 資料庫連接字串配置
  - Redis 連接設定
  - HTTP Client Factory 設定
- [ ] 建立基礎配置模型和管理機制
  - 配置檔案結構定義
  - 環境變數支援
  - 配置驗證機制

#### 技術需求
- .NET 8.0 專案模板
- ASP.NET Core Hosting
- 依賴注入容器設定
- NuGet 套件管理

---

### Story 2.2: YouTube 爬蟲邏輯遷移

**Story ID**: DSNT-2.2  
**優先級**: 高  
**工作量**: 4 天  
**負責人**: [待分配]  
**前置依賴**: Story 2.1

#### 任務描述
將現有 YouTube 相關爬蟲邏輯完整遷移至 Crawler 服務，包括 API 管理和 Webhook 處理。

#### 驗收標準
- [ ] 建立 `PlatformMonitors/YoutubeMonitor.cs` 包含所有 YouTube API 邏輯
  - 遷移直播狀態檢測邏輯
  - 遷移影片資訊獲取功能
  - 遷移頻道資訊管理
- [ ] 遷移 YouTube Data API v3 配額管理機制
  - API 配額計數器
  - 多 API 金鑰輪替
  - 配額超限處理和警告
- [ ] 遷移 YouTube PubSubHubbub Webhook 處理邏輯
  - 建立 `WebhookHandlers/YoutubeWebhookHandler.cs`
  - Webhook 訂閱管理
  - 接收和解析 PubSubHubbub 通知
- [ ] 實作 YouTube 直播狀態檢測和變化偵測
  - 定時輪詢機制
  - 狀態變化比較邏輯
  - 批量處理多個頻道
- [ ] 建立 YouTube API 錯誤處理和重試機制
  - API 呼叫失敗重試
  - Rate Limiting 處理
  - 異常狀況日誌記錄
- [ ] 實作事件廣播機制
  - 直播開始/結束事件發送
  - 與外部錄影工具相容的事件格式
  - 批量事件合併和發送

#### 技術需求
- Google.Apis.YouTube.v3 API
- Webhook 接收端點實作
- HTTP 請求處理和錯誤重試
- 事件驅動架構

#### 測試需求
- YouTube API 模擬測試
- Webhook 處理測試
- 配額管理測試
- 事件廣播測試

---

### Story 2.3: 其他平台爬蟲邏輯遷移

**Story ID**: DSNT-2.3  
**優先級**: 高  
**工作量**: 5 天  
**負責人**: [待分配]  
**前置依賴**: Story 2.2

#### 任務描述
將 Twitch、Twitter、TwitCasting 爬蟲邏輯遷移至 Crawler 服務，建立統一的平台監控介面。

#### 驗收標準
- [ ] 建立 `PlatformMonitors/TwitchMonitor.cs` 包含 Twitch API 和 EventSub
  - Twitch API 調用邏輯
  - EventSub 訂閱管理
  - 直播狀態監控
  - Webhook 事件處理
- [ ] 建立 `PlatformMonitors/TwitterMonitor.cs` 包含 Twitter Spaces 監控
  - Twitter Spaces API 邏輯
  - Cookie 認證管理
  - Spaces 狀態檢測
  - 不穩定連接處理
- [ ] 建立 `PlatformMonitors/TwitCastingMonitor.cs` 包含 TwitCasting API
  - TwitCasting API 整合
  - 直播狀態輪詢
  - 使用者資訊管理
  - API 限制處理
- [ ] 實作統一的平台監控介面 `IPlatformMonitor`
  - 定義通用的監控方法
  - 狀態報告標準化
  - 啟動和停止介面
  - 錯誤處理標準化
- [ ] 建立各平台的錯誤處理和 Rate Limiting 機制
  - 平台特定的錯誤處理
  - Rate Limit 監控和延遲
  - 連接失敗重試邏輯
  - 異常狀況恢復機制
- [ ] 整合所有平台到統一的監控系統
  - 平台監控器註冊
  - 平行執行多個平台監控
  - 統一的事件廣播格式
  - 監控狀態報告

#### 技術需求
- 多平台 API 整合經驗
- HTTP 客戶端程式設計
- 並行處理和任務管理
- 統一介面設計

#### 平台監控介面定義
```csharp
public interface IPlatformMonitor
{
    string PlatformName { get; }
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<PlatformStatus> GetStatusAsync();
    event EventHandler<StreamStatusChangedEventArgs> StreamStatusChanged;
}
```

---

### Story 2.4: 會員驗證跨服務協調系統

**Story ID**: DSNT-2.4  
**優先級**: 高  
**工作量**: 4 天  
**負責人**: [待分配]  
**前置依賴**: Story 2.3

#### 任務描述
實作會員驗證邏輯和 Discord Shard 路由機制，確保正確的跨服務協調。

#### 驗收標準
- [ ] 建立 `MemberVerification/MemberVerificationService.cs`
  - 從 Discord Bot 遷移所有會員驗證邏輯
  - OAuth2 token 管理和續期
  - 定時驗證任務調度
- [ ] 實作 shard 路由計算：`(guild_id >> 22) % total_shards`
  - 建立 `ShardRoutingService.cs`
  - Guild ID 到 Shard ID 的映射計算
  - 支援動態 Shard 數量調整
- [ ] 建立 `shard-request:{shardId}` PubSub 請求機制
  - 實作請求/回應模式
  - 支援異步等待回應
  - 請求超時處理
  - 重試失敗請求
- [ ] 實作 OAuth2 token 集中管理和自動續期
  - Token 儲存和加密
  - 自動續期檢查
  - 過期 token 處理
  - 使用者授權狀態管理
- [ ] 建立會員驗證定時任務和狀態追蹤
  - 批量會員驗證處理
  - 驗證結果快取
  - 狀態變化追蹤
  - 驗證失敗處理
- [ ] 實作 Discord API 操作代理機制
  - 透過正確 Shard 執行 Guild 操作
  - 支援角色管理和頻道權限
  - API 調用結果回傳

#### 技術需求
- OAuth2 認證流程
- Discord Guild ID 和 Shard 計算
- Redis PubSub 請求/回應模式
- 加密和安全儲存

#### Shard 路由實作範例
```csharp
public class ShardRoutingService
{
    private int _totalShards;
    
    public int CalculateShardId(ulong guildId)
    {
        return (int)((guildId >> 22) % (ulong)_totalShards);
    }
    
    public async Task<T> RequestShardOperation<T>(ulong guildId, string operation, object data)
    {
        var shardId = CalculateShardId(guildId);
        var requestId = Guid.NewGuid().ToString();
        
        var request = new ShardRequest
        {
            RequestId = requestId,
            Operation = operation,
            GuildId = guildId,
            Data = data
        };
        
        await _pubSub.PublishAsync($"shard-request:{shardId}", request);
        return await WaitForResponse<T>(requestId, TimeSpan.FromSeconds(30));
    }
}
```

---

### Story 2.5: 事件廣播和追蹤管理系統

**Story ID**: DSNT-2.5  
**優先級**: 高  
**工作量**: 3 天  
**負責人**: [待分配]  
**前置依賴**: Story 2.4

#### 任務描述
實作直播狀態廣播和動態追蹤管理機制，完善服務間通訊。

#### 驗收標準
- [ ] 建立 `TrackingManager.cs` 管理全域追蹤計數器
  - 維護 `Dictionary<StreamKey, HashSet<GuildId>>` 追蹤映射
  - 支援追蹤目標新增和移除
  - 計數器歸零時停止對應爬蟲
- [ ] 實作 `stream.follow`/`stream.unfollow` 事件處理
  - 監聽 Discord Bot 發送的追蹤事件
  - 更新全域追蹤計數器
  - 動態啟動/停止平台監控
- [ ] 建立批量事件廣播機制（`streams.online`/`streams.offline`）
  - 收集同時段的狀態變化
  - 批量發送減少 PubSub 負載
  - 支援單一和批量事件格式
- [ ] 實作動態追蹤目標調整（無 Guild 追蹤時停止爬蟲）
  - 即時調整監控目標清單
  - 避免不必要的 API 調用
  - 支援追蹤目標重新啟動
- [ ] 維持外部錄影工具事件格式相容性
  - 保持現有事件名稱和格式
  - 向後相容性確保
  - 新舊事件格式並存支援
- [ ] 實作追蹤狀態持久化和恢復
  - 啟動時從資料庫載入追蹤目標
  - 定期同步記憶體狀態到資料庫
  - 服務重啟後狀態恢復

#### 技術需求
- 並行集合管理 (ConcurrentDictionary)
- 事件批量處理
- 資料庫狀態同步
- 記憶體效率優化

#### 追蹤管理實作範例
```csharp
public class TrackingManager
{
    private readonly ConcurrentDictionary<StreamKey, HashSet<ulong>> _globalTrackCounter = new();
    private readonly object _trackLock = new object();
    
    public async Task HandleFollowRequest(StreamFollowEvent followEvent)
    {
        var key = new StreamKey(followEvent.Platform, followEvent.StreamKey);
        
        lock (_trackLock)
        {
            if (_globalTrackCounter.ContainsKey(key))
            {
                _globalTrackCounter[key].Add(followEvent.GuildId);
            }
            else
            {
                _globalTrackCounter[key] = new HashSet<ulong> { followEvent.GuildId };
                // 第一次追蹤，通知平台監控器開始追蹤
                await _platformManager.StartTrackingAsync(key);
            }
        }
    }
    
    public async Task HandleUnfollowRequest(StreamUnfollowEvent unfollowEvent)
    {
        var key = new StreamKey(unfollowEvent.Platform, unfollowEvent.StreamKey);
        
        lock (_trackLock)
        {
            if (_globalTrackCounter.TryGetValue(key, out var guilds))
            {
                guilds.Remove(unfollowEvent.GuildId);
                if (guilds.Count == 0)
                {
                    _globalTrackCounter.TryRemove(key, out _);
                    // 沒有 Guild 追蹤了，通知停止追蹤
                    await _platformManager.StopTrackingAsync(key);
                }
            }
        }
    }
}
```

---

### Story 2.6: gRPC 客戶端和健康檢查

**Story ID**: DSNT-2.6  
**優先級**: 中  
**工作量**: 2 天  
**負責人**: [待分配]  
**前置依賴**: Story 2.5

#### 任務描述
實作 Coordinator 通訊和服務健康檢查機制，完成 Crawler 服務基礎建設。

#### 驗收標準
- [ ] 建立 gRPC 客戶端連接 Coordinator
  - 實作 `CoordinatorGrpcClient.cs`
  - 建立持久化 gRPC 連接
  - 連接失敗自動重試
- [ ] 實作心跳回報機制（服務狀態、爬蟲計數、API 配額狀態）
  - 定期發送心跳到 Coordinator
  - 報告服務運行狀態
  - 包含監控統計資訊
- [ ] 建立 HTTP 健康檢查端點 `/health`
  - ASP.NET Core Health Checks
  - 檢查 Redis 連接狀態
  - 檢查資料庫連接狀態
  - 檢查各平台 API 可用性
- [ ] 實作服務啟動就緒檢查
  - 確保所有依賴服務可用後才報告就緒
  - 支援 Coordinator 啟動順序管理
  - 提供詳細的就緒狀態資訊
- [ ] 建立優雅關閉機制
  - 接收關閉信號時停止接收新任務
  - 完成現有任務處理
  - 清理資源和連接
  - 向 Coordinator 發送關閉通知

#### 技術需求
- gRPC 客戶端程式設計
- ASP.NET Core 健康檢查
- 優雅關閉模式
- 服務間通訊

#### 健康檢查實作
```csharp
public class CrawlerHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, 
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        
        // 檢查 Redis 連接
        var redisHealthy = await CheckRedisHealth();
        data.Add("redis", redisHealthy ? "healthy" : "unhealthy");
        
        // 檢查資料庫連接
        var dbHealthy = await CheckDatabaseHealth();
        data.Add("database", dbHealthy ? "healthy" : "unhealthy");
        
        // 檢查平台監控狀態
        var platformStatus = await GetPlatformMonitorStatus();
        data.Add("platforms", platformStatus);
        
        var isHealthy = redisHealthy && dbHealthy;
        
        return isHealthy 
            ? HealthCheckResult.Healthy("Crawler service is healthy", data)
            : HealthCheckResult.Unhealthy("Crawler service has issues", data: data);
    }
}
```

---

## Epic 3: Coordinator 服務實作

### Epic 3 描述
**目標**: 實作統一的服務生命週期管理和監控系統，負責 Crawler 和多個 Discord Shard 的協調管理。

**商業價值**:
- 實現自動化服務管理，減少人工介入
- 提供系統整體監控，提升運維效率
- 支援故障自動恢復，提升系統可用性

**成功標準**:
- Coordinator 能成功管理所有服務生命週期
- 服務故障時能自動檢測和重啟
- 配置管理系統運作正常
- 監控日誌清楚記錄系統狀態

---

### Story 3.1: Coordinator gRPC 服務建立

**Story ID**: DSNT-3.1  
**優先級**: 高  
**工作量**: 3 天  
**負責人**: [待分配]  
**前置依賴**: Epic 2 完成

#### 任務描述
建立 Coordinator 專案和 gRPC 服務端實作，提供統一的服務管理 API。

#### 驗收標準
- [ ] 建立 `StreamNotifyBot.Coordinator/` 專案
  - 建立 ASP.NET Core gRPC 專案
  - 設定依賴套件 (Grpc.AspNetCore)
  - 建立基礎專案結構
- [ ] 定義 `coordinator.proto` gRPC 服務定義
  ```protobuf
  service Coordinator {
    rpc Heartbeat(HeartbeatRequest) returns (HeartbeatReply);
    rpc GetStatus(GetStatusRequest) returns (GetStatusReply);
    rpc GetAllStatuses(GetAllStatusesRequest) returns (GetAllStatusesReply);
    rpc RestartService(RestartServiceRequest) returns (RestartServiceReply);
  }
  ```
- [ ] 實作 `CoordinatorService.cs` gRPC 服務端
  - 實作所有 RPC 方法
  - 服務狀態追蹤
  - 併發安全處理
- [ ] 實作 `Heartbeat`, `GetStatus`, `RestartService` RPC 方法
  - 心跳接收和處理
  - 服務狀態查詢
  - 服務重啟指令執行
- [ ] 建立服務註冊和狀態追蹤機制
  - 服務實例註冊
  - 心跳超時檢測
  - 服務狀態狀態機

#### 技術需求
- gRPC ASP.NET Core 整合
- protobuf 定義和產生
- 併發安全程式設計

---

### Story 3.2: 進程生命週期管理系統

**Story ID**: DSNT-3.2  
**優先級**: 高  
**工作量**: 4 天  
**負責人**: [待分配]  
**前置依賴**: Story 3.1

#### 任務描述
實作多服務進程啟動、監控和重啟機制，確保服務協調運行。

#### 驗收標準
- [ ] 建立 `ProcessManager.cs` 管理服務進程
  - 進程啟動和停止
  - 進程狀態監控
  - 進程 ID 和控制代碼管理
- [ ] 實作服務啟動順序控制（Crawler → Discord Shard）
  - 依賴關係解析
  - 循序啟動機制
  - 啟動失敗處理
- [ ] 建立進程健康檢查和故障檢測
  - HTTP 健康檢查調用
  - gRPC 心跳監控
  - 進程存活檢查
- [ ] 實作自動重啟機制和重試策略
  - 故障檢測觸發重啟
  - 重啟次數限制
  - 指數退避重試
- [ ] 支援動態 Discord Shard 數量管理
  - 從配置讀取或 API 獲取 Shard 數量
  - 動態啟動多個 Shard 進程
  - Shard 編號和總數參數傳遞

#### 技術需求
- Process API 程式設計
- 進程間通訊
- 健康檢查策略
- 依賴關係管理

#### 進程管理實作範例
```csharp
public class ProcessManager
{
    private readonly Dictionary<string, ProcessInfo> _processes = new();
    private readonly ILogger<ProcessManager> _logger;
    
    public async Task StartServicesAsync()
    {
        // 1. 啟動 Crawler 服務
        await StartServiceAsync("crawler");
        await WaitForServiceHealthy("crawler");
        
        // 2. 獲取推薦 Shard 數量
        var shardCount = await GetRecommendedShardCount();
        
        // 3. 啟動所有 Discord Shard
        var shardTasks = new List<Task>();
        for (int i = 0; i < shardCount; i++)
        {
            shardTasks.Add(StartShardAsync(i, shardCount));
        }
        await Task.WhenAll(shardTasks);
    }
    
    public async Task RestartServiceAsync(string serviceName)
    {
        if (_processes.TryGetValue(serviceName, out var processInfo))
        {
            _logger.LogInformation("Restarting service: {ServiceName}", serviceName);
            
            // 優雅關閉
            processInfo.Process.CloseMainWindow();
            
            // 等待關閉或強制終止
            if (!processInfo.Process.WaitForExit(30000))
            {
                processInfo.Process.Kill();
            }
            
            // 重新啟動
            await StartServiceAsync(serviceName);
        }
    }
}
```

---

### Story 3.3: YAML 配置管理系統

**Story ID**: DSNT-3.3  
**優先級**: 中  
**工作量**: 3 天  
**負責人**: [待分配]  
**前置依賴**: Story 3.2

#### 任務描述
實作靈活的配置檔案管理和環境變數替換，支援動態配置。

#### 驗收標準
- [ ] 建立 `coord.yml` 配置檔案格式定義
  - 服務定義格式
  - 監控參數配置
  - 環境變數模板
- [ ] 實作 YAML 配置解析和驗證
  - 使用 YamlDotNet 解析
  - 配置結構驗證
  - 必填項目檢查
- [ ] 建立環境變數替換機制（`{{variable}}` 語法）
  - 模板變數識別
  - 環境變數解析
  - 預設值支援
- [ ] 支援服務配置熱重載
  - 檔案變更監控
  - 配置動態更新
  - 服務重新配置
- [ ] 建立配置檔案範例和文件
  - 完整的範例配置
  - 配置參數說明文件
  - 最佳實務建議

#### 技術需求
- YAML 解析套件 (YamlDotNet)
- 配置驗證和綁定
- 檔案監控 (FileSystemWatcher)
- 環境變數處理

#### 配置格式範例
```yaml
global:
  redis:
    connectionString: "{{REDIS_CONNECTION_STRING:localhost:6379}}"
  database:
    connectionString: "{{DATABASE_CONNECTION_STRING}}"
    
services:
  crawler:
    type: "crawler"
    command: "dotnet"
    args: ["run", "--project", "StreamNotifyBot.Crawler"]
    workingDirectory: "{{CRAWLER_PATH:./Crawler}}"
    healthCheck:
      endpoint: "http://localhost:6111/health"
      timeoutMs: 5000
      intervalMs: 30000
    dependencies: []
    
  discordShards:
    type: "discordShard"
    totalShards: "dynamic"  # 或固定數字
    command: "dotnet"
    args: ["run", "--project", "DiscordStreamNotifyBot", "{shardId}", "{totalShards}"]
    workingDirectory: "{{BOT_PATH:./DiscordBot}}"
    healthCheck:
      type: "grpc"
      timeoutMs: 3000
      intervalMs: 15000
    dependencies: ["crawler"]
    
monitoring:
  recheckIntervalMs: 2000
  unresponsiveSec: 30
  maxRestartAttempts: 3
  restartDelayMs: 5000
  
logging:
  level: "Information"
  structured: true
  console: true
  
notifications:
  discord:
    webhookUrl: "{{DISCORD_WEBHOOK_URL}}"
    enabled: true
```

---

### Story 3.4: tmux 部署腳本和監控介面

**Story ID**: DSNT-3.4  
**優先級**: 中  
**工作量**: 4 天  
**負責人**: [待分配]  
**前置依賴**: Story 3.3

#### 任務描述
建立 tmux 部署管理腳本和 console 監控輸出，提供完整的部署和監控解決方案。

#### 驗收標準
- [ ] 建立 `start-services.sh` tmux session 管理腳本
  ```bash
  #!/bin/bash
  # 建立主要的 tmux session
  tmux new-session -d -s "streamnotify" -c "/app"
  
  # 建立各服務的 tmux window
  tmux new-window -t "streamnotify:1" -n "coordinator" -c "/app/Coordinator"
  tmux new-window -t "streamnotify:2" -n "crawler" -c "/app/Crawler"
  
  # 根據動態 shard 數量建立 Discord Shard windows
  SHARD_COUNT=$(dotnet run --project Coordinator -- --get-shard-count)
  for i in $(seq 0 $((SHARD_COUNT-1))); do
    tmux new-window -t "streamnotify:$((i+3))" -n "shard-$i" -c "/app/DiscordBot"
  done
  
  # 啟動各服務
  tmux send-keys -t "streamnotify:coordinator" "dotnet run --project StreamNotifyBot.Coordinator" Enter
  sleep 5  # 等待 Coordinator 啟動
  
  tmux send-keys -t "streamnotify:crawler" "dotnet run --project StreamNotifyBot.Crawler" Enter
  sleep 10 # 等待 Crawler 完全啟動
  
  # 啟動所有 Discord Shard
  for i in $(seq 0 $((SHARD_COUNT-1))); do
    tmux send-keys -t "streamnotify:shard-$i" "dotnet run --project DiscordStreamNotifyBot $i $SHARD_COUNT" Enter
    sleep 2
  done
  ```
- [ ] 建立 `stop-services.sh` 優雅關閉腳本
  - 發送 SIGTERM 到所有 tmux window
  - 等待服務優雅關閉
  - 強制終止 tmux session
- [ ] 實作 console 監控介面顯示所有服務狀態
  - 即時服務狀態顯示
  - Crawler 服務監控統計
  - Discord Shard 狀態和延遲
  - 系統資源使用情況
  - 最近事件日誌
- [ ] 建立結構化日誌輸出（JSON 格式）
  - 統一的日誌格式標準
  - JSON 結構化輸出
  - 日誌等級和分類
  - 時間戳和服務識別
- [ ] 實作 Discord Webhook 異常通知機制
  - 系統嚴重錯誤通知
  - 服務故障警報
  - Discord Embed 格式通知
  - 通知頻率限制

#### 技術需求
- bash scripting
- tmux API 操作
- Console UI 程式設計
- 結構化日誌設計
- HTTP Webhook 發送

#### Console 監控介面實作
```csharp
public class ConsoleMonitoringService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Console.Clear();
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine($"  Discord Stream Notify Bot - System Status");
            Console.WriteLine($"  Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine("═══════════════════════════════════════════════════════════");
            Console.WriteLine();
            
            // 顯示 Crawler 服務狀態
            var crawlerStatus = await _coordinatorService.GetServiceStatus("crawler");
            Console.WriteLine($"🔍 Crawler Service:     {GetStatusIcon(crawlerStatus.IsHealthy)} {crawlerStatus.Status}");
            Console.WriteLine($"   └─ Monitored Streams: {crawlerStatus.MonitoredStreamCount}");
            Console.WriteLine($"   └─ API Quota Usage:   {crawlerStatus.ApiQuotaUsage}%");
            Console.WriteLine();
            
            // 顯示 Discord Shard 狀態
            Console.WriteLine("🤖 Discord Shards:");
            var shardStatuses = await _coordinatorService.GetAllShardStatuses();
            foreach (var shard in shardStatuses)
            {
                Console.WriteLine($"   Shard {shard.ShardId}:  {GetStatusIcon(shard.IsHealthy)} " +
                                $"{shard.Status} - {shard.GuildCount} Guilds - {shard.Latency}ms");
            }
            Console.WriteLine();
            
            // 顯示系統資源使用
            Console.WriteLine("📊 System Resources:");
            Console.WriteLine($"   Memory Usage: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            Console.WriteLine($"   Redis Status: {await CheckRedisStatus()}");
            Console.WriteLine($"   Database:     {await CheckDatabaseStatus()}");
            Console.WriteLine();
            
            // 顯示最近的事件
            Console.WriteLine("📋 Recent Events (Last 5):");
            var recentEvents = await _eventLogService.GetRecentEvents(5);
            foreach (var evt in recentEvents)
            {
                Console.WriteLine($"   {evt.Timestamp:HH:mm:ss} [{evt.Level}] {evt.Message}");
            }
            
            await Task.Delay(2000, stoppingToken); // 每2秒更新一次
        }
    }
    
    private string GetStatusIcon(bool isHealthy)
    {
        return isHealthy ? "🟢" : "🔴";
    }
}
```

---

### Story 3.5: Prometheus 指標監控整合

**Story ID**: DSNT-3.5  
**優先級**: 中  
**工作量**: 5 天  
**負責人**: [待分配]  
**前置依賴**: Story 3.1 (gRPC 服務)

#### 任務描述
在 Coordinator 服務中整合 Prometheus 指標暴露功能，提供詳細的系統監控數據，支援透過標準監控工具進行深度監控和告警。

#### 驗收標準
- [ ] 整合 Prometheus 指標收集中介軟體
  - 使用 `prometheus-net.AspNetCore` 套件
  - 暴露 `/metrics` HTTP 端點
  - 支援標準 Prometheus 指標格式
- [ ] 實作系統層級指標收集
  - 服務運行時間 (uptime)
  - CPU 和記憶體使用率
  - GC 統計資訊
  - HTTP/gRPC 請求統計
- [ ] 實作服務管理指標
  - 託管服務數量和狀態
  - 服務重啟次數和原因
  - 心跳接收統計
  - 服務健康檢查結果
- [ ] 實作 Discord 生態系統指標
  - 總 Shard 數量和狀態
  - Guild 總數和每 Shard 分佈
  - 各 Shard 連接延遲
  - Discord API 調用統計
- [ ] 實作 Crawler 服務指標
  - 監控的直播數量 (按平台分類)
  - API 配額使用情況
  - 事件廣播統計 (成功/失敗)
  - 會員驗證統計
- [ ] 建立 Grafana 儀表板範例
  - 系統概覽儀表板
  - 服務詳細監控儀表板
  - 告警規則範例
  - 部署和配置說明

#### 技術需求
- Prometheus .NET 整合
- ASP.NET Core 指標中介軟體
- 系統效能計數器存取
- Grafana 儀表板設計
- YAML 配置管理

#### 指標設計概覽
```csharp
// 系統指標範例
coordinator_uptime_seconds - 服務運行時間
coordinator_memory_usage_bytes - 記憶體使用量
coordinator_service_status{service_name, service_type} - 服務狀態
discord_shards_total - Discord Shard 總數
crawler_monitored_streams_total{platform} - 監控的直播數量
```

---

## 測試策略

### 單元測試要求
- **覆蓋率目標**: > 80%
- **測試框架**: xUnit + Moq
- **重點測試區域**:
  - 事件處理邏輯
  - API 調用和錯誤處理
  - 服務間通訊協定
  - 配置管理和驗證

### 整合測試計劃
- **跨服務通訊測試**: Redis PubSub 事件流程
- **資料庫整合測試**: Entity Framework 操作
- **外部 API 測試**: 模擬各平台 API 回應
- **gRPC 通訊測試**: Coordinator 和服務間通訊

### 效能測試需求
- **負載測試**: 模擬多 Guild 環境測試系統效能
- **壓力測試**: 大量併發直播狀態變化處理
- **記憶體測試**: 長時間運行的記憶體洩漏檢測

### 故障測試 (Chaos Engineering)
- **服務故障模擬**: 模擬 Crawler 或 Discord Shard 故障
- **網路分割測試**: Redis 連接中斷情況
- **資料庫故障**: 資料庫不可用時的處理
- **API 限制測試**: 各平台 API 超限情況

---

## 部署策略

### 環境需求
- **.NET 8.0 Runtime**
- **Redis Server**
- **MySQL/MariaDB**
- **tmux** (進程管理)

### 部署順序
1. **環境準備**: 資料庫、Redis、配置檔案
2. **Coordinator 部署**: 啟動服務管理中心
3. **自動化部署**: 透過 Coordinator 啟動其他服務
4. **驗證測試**: 確認所有服務正常運行

### 回滾計劃
- **快速回滾**: 保留舊版本執行檔
- **資料庫相容**: 確保資料庫結構向下相容
- **配置還原**: 備份舊版配置檔案

---

## 風險緩解計劃

### 技術風險緩解
- **開發環境**: 建立完整的開發和測試環境
- **程式碼審查**: 所有關鍵程式碼必須進行同行審查
- **漸進式重構**: 分階段遷移，確保每階段都能獨立運行

### 業務風險緩解
- **功能相容性**: 保持所有現有功能和使用者介面不變
- **停機時間最小化**: 支援藍綠部署減少服務中斷
- **資料備份**: 部署前完整備份資料庫和配置

### 運維風險緩解
- **監控完善**: 部署完成後立即啟用所有監控機制
- **文件更新**: 同步更新所有運維文件和操作手冊
- **團隊培訓**: 確保運維團隊熟悉新架構

---

**文件結束**

本文件提供了 Discord Stream Notify Bot Coordinator-Shard 架構轉換的完整 Epic 和 Story 分解，為開發團隊提供詳細的任務指導和執行計劃。
