# Discord Stream Notify Bot [點我邀請到你的Discord內](https://discordapp.com/api/oauth2/authorize?client_id=758222559392432160&permissions=2416143425&scope=bot%20applications.commands)

[![Website dcbot.konnokai.me](https://img.shields.io/website-up-down-green-red/http/dcbot.konnokai.me/stream.svg)](http://dcbot.konnokai.me/stream)
[![GitHub commits](https://badgen.net/github/commits/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/commit/)
[![GitHub latest commit](https://badgen.net/github/last-commit/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/konnokai/Discord-Stream-Notify-Bot/commit/)
[![GitHub stars](https://badgen.net/github/stars/konnokai/Discord-Stream-Notify-Bot)](https://GitHub.com/Naereen/konnokai/Discord-Stream-Notify-Bot/)

一個可以讓你在Discord上通知Vtuber直播的小幫手

自行運行所需環境與參數
-
1. .NET Core 6.0 Runtime 或 SDK ([微軟網址](https://dotnet.microsoft.com/en-us/download/dotnet/5.0))
2. Redis Server ([Windows下載網址](https://github.com/MicrosoftArchive/redis)，Linux可直接透過apt或yum安裝)
3. Discord Bot Token ([Discord Dev網址](https://discord.com/developers/applications))
4. Discord Channel WebHook (做紀錄用)
5. Google Console的API金鑰並確保已於程式庫開啟Youtube Data API v3 ([Google Console網址](https://console.cloud.google.com/apis/library/youtube.googleapis.com))
6. Twitter V2 Access API Key 跟 Secret (如不需要推特語音通知則不需要) ([Twitter Dev網址](https://developer.twitter.com/))
7. 錄影功能需搭配隔壁 [Youtube Stream Record](https://github.com/jun112561/youtube-stream-record) 使用
8. Discord & Google 的 OAuth Client ID 跟 Client Secret 會限驗證，需搭配 [網站](https://github.com/jun112561/Discord-Stream-Bot-Backend) 使用
9. PubSubCallbackUrl，搭配上面的網站後端做YT影片上傳接收使用，當有新爬蟲時小幫手會自動註冊 ([Google PubSubHubbub](https://pubsubhubbub.appspot.com))
10. Imgur Client Id，發送全域訊息如果要上傳圖片附件時會需要用到，可到 [Imgur](https://api.imgur.com/oauth2/addclient) 註冊，`Authorization type:` 選擇 `Anonymous usage without user authorization` 即可
11. Uptime Kuma Push 監測器的網址，如果不需要上線監測則可為空，需搭配 [Uptime Kuma](https://github.com/louislam/uptime-kuma) 使用

建置環境
- 
- Visual Studio 2022
- .NET Core 6.0
