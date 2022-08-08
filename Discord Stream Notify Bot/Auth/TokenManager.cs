using Newtonsoft.Json;
using System;
using System.Text;

namespace Discord_Stream_Notify_Bot.Auth
{
    public class TokenManager
    {
        /// <summary>
        /// 產生加密使用者資料
        /// </summary>
        /// <param name="user">尚未加密的使用者資料</param>
        /// <returns>已加密的使用者資料</returns>
        public static string CreateToken(object data, string key)
        {
            var json = JsonConvert.SerializeObject(data);
            var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            var iv = Guid.NewGuid().ToString().Replace("-", "").Substring(0, 16);

            //使用 AES 加密 Payload
            var encrypt = TokenCrypto
                .AESEncrypt(base64, key.Substring(0, 16), iv);

            //取得簽章
            var signature = TokenCrypto
                .ComputeHMACSHA256(iv + "." + encrypt, key.Substring(0, 64));

            return iv + "." + encrypt + "." + signature;
        }

        /// <summary>
        /// 解密Google Token Response資料
        /// </summary>
        /// <param name="token">已加密的Google Token Response資料</param>
        /// <returns>未加密的Google Token Response資料</returns>
        /// <exception cref="ArgumentOutOfRangeException">Token格式錯誤</exception>
        /// <exception cref="ArgumentException">簽章驗證失敗</exception>
        public static T GetTokenResponseValue<T>(string token, string key)
        {
            if (string.IsNullOrWhiteSpace(token)) return default(T);

            token = token.Replace(" ", "+");
            var split = token.Split('.');
            if (split.Length != 3) throw new ArgumentOutOfRangeException("token");

            var iv = split[0];
            var encrypt = split[1];
            var signature = split[2];

            //檢查簽章是否正確
            if (signature != TokenCrypto.ComputeHMACSHA256(iv + "." + encrypt, key.Substring(0, 64)))
                throw new ArgumentException(signature);

            //使用 AES 解密 Payload
            var base64 = TokenCrypto.AESDecrypt(encrypt, key.Substring(0, 16), iv);
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var payload = JsonConvert.DeserializeObject<T>(json);

            return payload;
        }
    }
}