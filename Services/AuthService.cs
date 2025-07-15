using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class AuthService
    {
        private readonly HttpClient _httpClient;
        private readonly HardwareService _hardwareService;
        private static readonly string AuthFilePath = Path.Combine(AppContext.BaseDirectory, "auth.json");

        public AuthService(HardwareService hardwareService, HttpMessageHandler httpHandler)
        {
            _hardwareService = hardwareService;

            _httpClient = new HttpClient(httpHandler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BsgLauncher/14.5.1.3034");
        }

        public async Task<(bool Success, string ErrorMessage)> LoginAsync(string email, string password)
        {
            string hwId = _hardwareService.GenerateHwIdV1();
            string passwordHash = GetMd5Hex(password);

            var payload = new { email, pass = passwordHash, hwCode = hwId, captcha = (string)null };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync("https://launcher.escapefromtarkov.com/launcher/login", httpContent);
                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());

                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0)
                {
                    return (false, json["errmsg"]?.Value<string>() ?? "An unknown error occurred.");
                }

                var data = json["data"];
                var authData = new AuthData
                {
                    AccessToken = data["access_token"].Value<string>(),
                    RefreshToken = data["refresh_token"].Value<string>(),
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(data["expires_in"].Value<int>())
                };
                
                File.WriteAllText(AuthFilePath, JsonConvert.SerializeObject(authData));
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"A network or system error occurred: {ex.Message}");
            }
        }

        public async Task<bool> LoginWithRefreshTokenAsync()
        {
            if (!File.Exists(AuthFilePath)) return false;

            var savedAuthData = JsonConvert.DeserializeObject<AuthData>(File.ReadAllText(AuthFilePath));
            if (savedAuthData.ExpiresAtUtc > DateTime.UtcNow.AddMinutes(5))
            {
                return true; // Token is still valid
            }

            // Refresh the token
            var payload = new
            {
                grant_type = "refresh_token",
                refresh_token = savedAuthData.RefreshToken,
                hwCode = _hardwareService.GenerateHwIdV1(),
                client_id = 0
            };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsync("https://launcher.escapefromtarkov.com/launcher/token/refresh", httpContent);
                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());

                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0)
                {
                    File.Delete(AuthFilePath); // The refresh token is invalid
                    return false;
                }
                
                var data = json["data"];
                var newAuthData = new AuthData
                {
                    AccessToken = data["access_token"].Value<string>(),
                    RefreshToken = data["refresh_token"].Value<string>(),
                    ExpiresAtUtc = DateTime.UtcNow.AddSeconds(data["expires_in"].Value<int>())
                };
                File.WriteAllText(AuthFilePath, JsonConvert.SerializeObject(newAuthData));
                return true;
            }
            catch
            {
                return false; // Network error or other issue
            }
        }

        public void Logout()
        {
            if (File.Exists(AuthFilePath))
            {
                File.Delete(AuthFilePath);
            }
        }

        private string GetMd5Hex(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder();
                foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
        
        private string DecompressResponse(byte[] compressedData)
        {
            // The EFT API returns a Zlib-compressed stream. We need to skip the first 2 bytes (Zlib header)
            // to decompress it correctly with .NET's DeflateStream.
            using (var inputStream = new MemoryStream(compressedData, 2, compressedData.Length - 2))
            using (var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                deflateStream.CopyTo(outputStream);
                return Encoding.UTF8.GetString(outputStream.ToArray());
            }
        }
        
        public AuthData? GetCurrentAuthData()
        {
            if (!File.Exists(AuthFilePath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<AuthData>(File.ReadAllText(AuthFilePath));
            }
            catch
            {
                // Handle potential file corruption
                File.Delete(AuthFilePath);
                return null;
            }
        }
    }
}