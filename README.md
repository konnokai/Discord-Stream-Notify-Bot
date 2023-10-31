# Discord Stream Notify Bot [點我邀請到你的Discord內](https://discordapp.com/api/oauth2/authorize?client_id=758222559392432160&permissions=2416143425&scope=bot%20applications.commands)

[![Website dcbot.konnokai.me](https://img.shields.io/website-up-down-green-red/http/dcbot.konnokai.me/stream.svg)](http://dcbot.konnokai.me/stream)
[![GitHub commits](https://badgen.net/github/commits/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/commit/)
[![GitHub latest commit](https://badgen.net/github/last-commit/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/commit/)
[![GitHub stars](https://badgen.net/github/stars/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/)

一個可以讓你在Discord上通知Vtuber直播的小幫手

自行運行所需環境與參數
-
1. .NET Core 6.0 Runtime 或 SDK ([微軟網址](https://dotnet.microsoft.com/en-us/download/dotnet/6.0))
2. Redis Server ([Windows 下載網址](https://github.com/MicrosoftArchive/redis)，Linux 可直接透過 apt 或 yum 安裝)
3. Discord Bot Token ([Discord Dev網址](https://discord.com/developers/applications))
4. Discord Channel WebHook (做紀錄用)
5. Google Console API 金鑰並確保已於程式庫開啟 Youtube Data API v3 ([Google Console網址](https://console.cloud.google.com/apis/library/youtube.googleapis.com))
6. Twitter AuthToken & CSRFToken，這需要從已登入的 Twitter 帳號中，由名稱為 `auth_token` 和 `ct0` 的 Cookie 來獲得 (如不需要推特語音通知則不需要)
7. 錄影功能需搭配隔壁 [Youtube Stream Record](https://github.com/konnokai/YoutubeStreamRecord) 使用 (如無搭配錄影的話則不會有關台通知，且不能即時的通知開台*)
8. Discord & Google 的 OAuth Client ID 跟 Client Secret，用於 YouTube 會限驗證，需搭配 [網站後端](https://github.com/konnokai/Discord-Stream-Bot-Backend) 使用
9. PubSubCallbackUrl，搭配上面的網站後端做YT影片上傳接收使用，當有新爬蟲時小幫手會自動註冊，網址格式為: `https://[後端域名]/NotificationCallback` ([Google PubSubHubbub](https://pubsubhubbub.appspot.com))
10. Uptime Kuma Push 監測器的網址，如果不需要上線監測則可為空，需搭配 [Uptime Kuma](https://github.com/louislam/uptime-kuma) 使用
11. [ffmpeg](https://ffmpeg.org/download.html), [streamlink](https://streamlink.github.io/install.html)，原則上不裝的話就只是不會錄影 (裝完記得確認 PATH 環境變數是否有設定正確的路徑)

備註
-
請使用Release組態進行編譯，Debug組態有忽略掉不少東西會導致功能出現異常等錯誤

如需要自行改程式碼也記得確認Debug組態下的 `#if` 是否會導致偵錯問題

\* 未錄影的話則是固定在排定開台時間的前一分鐘通知，若有開啟錄影則會在錄影環境偵測到開始錄影時一併發送開台通知

建置&測試環境
- 
- Visual Studio 2022 17.7.6
- .NET SDK 6.0.14
- Windows 10 & 11 Pro
- Debian 11
- Redis 7.0.4

參考專案
-
- [NadekoBot](https://gitlab.com/Kwoth/nadekobot)
- [LivestreamRecorderService](https://github.com/Recorder-moe/LivestreamRecorderService)
- [Discord .NET](https://github.com/discord-net/Discord.Net)
- [twspace-crawler](https://github.com/HitomaruKonpaku/twspace-crawler)
- 其餘參考附於程式碼內

授權
-
- 此專案採用 [MIT](https://github.com/konnokai/Discord-Stream-Notify-Bot/blob/master/LICENSE.txt) 授權
