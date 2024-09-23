# 直播小幫手 [點我邀請到你的 Discord 內](https://discordapp.com/api/oauth2/authorize?client_id=758222559392432160&permissions=2416143425&scope=bot%20applications.commands)

![Discord-Stream-Notify-Bot](https://socialify.git.ci/konnokai/Discord-Stream-Notify-Bot/image?description=1&descriptionEditable=%E4%B8%80%E5%80%8B%E5%8F%AF%E4%BB%A5%E8%AE%93%E4%BD%A0%E5%9C%A8%20Discord%20%E4%B8%8A%E9%80%9A%E7%9F%A5%20Vtuber%20%E7%9B%B4%E6%92%AD%E7%9A%84%E5%B0%8F%E5%B9%AB%E6%89%8B&font=Inter&language=1&name=1&owner=1&pattern=Plus&stargazers=1&theme=Auto)

[![Website dcbot.konnokai.me](https://img.shields.io/website-up-down-green-red/http/dcbot.konnokai.me/stream.svg)](http://dcbot.konnokai.me/stream)
[![GitHub commits](https://badgen.net/github/commits/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/commit/)
[![GitHub latest commit](https://badgen.net/github/last-commit/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/commit/)

自行運行所需環境與參數
-
- .NET Core 6.0 Runtime 或 SDK ([微軟網址](https://dotnet.microsoft.com/en-us/download/dotnet/6.0))
- Redis Server ([Windows 下載網址](https://github.com/MicrosoftArchive/redis)，Linux 可直接透過 apt 或 yum 安裝)
- Discord Bot Token ([Discord Dev網址](https://discord.com/developers/applications))
- Discord Channel WebHook，做紀錄用
- Google Console API 金鑰並確保已於程式庫開啟 Youtube Data API v3 ([Google Console網址](https://console.cloud.google.com/apis/library/youtube.googleapis.com))
- 錄影功能需搭配隔壁 [Youtube Stream Record](https://github.com/konnokai/YoutubeStreamRecord) 使用 (如無搭配錄影的話則不會有關台通知，且不能即時的通知開台) \*
- Twitter AuthToken & CSRFToken，這需要從已登入的 Twitter 帳號中，由名稱為 `auth_token` 和 `ct0` 的 Cookie 來獲得 (如不需要推特語音通知則不需要) \*\*
- Discord & Google 的 OAuth Client ID 跟 Client Secret，用於 YouTube 會限驗證，需搭配 [網站後端](https://github.com/konnokai/Discord-Stream-Bot-Backend) 使用 \*\*
- ApiServerDomain，搭配上面的網站後端做 YouTube 影片上傳接收 & Twitch 狀態更新使用，僅需填寫後端域名就好 (Ex: api.example.me) ([Google PubSubHubbub](https://pubsubhubbub.appspot.com)) ([Twitch Webhook Callback](https://dev.twitch.tv/docs/eventsub/handling-webhook-events/))
- Uptime Kuma Push 監測器的網址，如果不需要上線監測則可為空，需搭配 [Uptime Kuma](https://github.com/louislam/uptime-kuma) 使用
- [ffmpeg](https://ffmpeg.org/download.html), [streamlink](https://streamlink.github.io/install.html)，原則上不裝的話就只是不會錄影 (裝完記得確認 PATH 環境變數是否有設定正確的路徑)
- Twitch App Client Id & Client Secret ([Twitch Develpers](https://dev.twitch.tv/console/apps)) \*\*
- TwitCasting Client Id & Client Secret ([TwitCasting Develpers](https://twitcasting.tv/developer.php)) \*\*

備註
-
請使用 Release 組態進行編譯，Debug 組態有忽略掉不少東西會導致功能出現異常等錯誤

如需要自行改程式碼也記得確認 Debug 組態下的 `#if` 是否會導致偵錯問題

\* 未錄影的話則是固定在排定開台時間的前一分鐘通知，若有開啟錄影則會在錄影環境偵測到開始錄影時一併發送開台通知

\*\* 未設定的話則僅該功能無法使用，在使用該功能的時會有錯誤提示

建置&測試環境
- 
- Visual Studio 2022
- .NET SDK 6.0
- Windows 10 & 11 Pro
- Debian 11
- Redis 7.0.4

參考專案
-
- [NadekoBot](https://gitlab.com/Kwoth/nadekobot)
- [LivestreamRecorderService](https://github.com/Recorder-moe/LivestreamRecorderService)
- [Discord .NET](https://github.com/discord-net/Discord.Net)
- [TwitchLib](https://github.com/TwitchLib/TwitchLib)
- [twspace-crawler](https://github.com/HitomaruKonpaku/twspace-crawler)
- 其餘參考附於程式碼內

授權
-
- 此專案採用 [MIT](https://github.com/konnokai/Discord-Stream-Notify-Bot/blob/master/LICENSE.txt) 授權
