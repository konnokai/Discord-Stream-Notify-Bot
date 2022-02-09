# Discord Stream Notify Bot

一個可以讓你在Discord上通知Vtuber直播的小幫手

自行運行所需環境與參數
-
1. .NET Core 6.0 Runtime 或 SDK ([微軟網址](https://dotnet.microsoft.com/en-us/download/dotnet/5.0))
2. Redis Server ([Windows下載網址](https://github.com/MicrosoftArchive/redis)，Linux可直接透過apt或yum安裝)
3. Discord Bot Token ([Discord Dev網址](https://discord.com/developers/applications))
4. Discord Channel WebHook (做紀錄用)
5. Google Console的API金鑰並確保已於程式庫開啟Youtube Data API v3 ([Google Console網址](https://console.cloud.google.com/apis/library/youtube.googleapis.com))
6. Twitter V2 Access API Key 跟 Secret (如不需要推特語音通知則不需要) ([Twitter Dev網址](https://developer.twitter.com/))
7. 錄影功能需搭配隔壁 [Youtube Stream Record](https://gitlab.com/jun112561/youtube-stream-record) 使用

建置環境
- 
- Visual Studio 2022
- .NET Core 6.0

**此版本目前未包含會限驗證的程式碼，待確認功能正常後會再更新上來**
