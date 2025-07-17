// SimpleEFTLauncher/Services/EftApiService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class EftApiService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private const string BaseUrl = "https://launcher.escapefromtarkov.com";

        public EftApiService(AuthService authService, HttpMessageHandler httpHandler)
        {
            _authService = authService;
            _httpClient = new HttpClient(httpHandler);
        }

        public async Task<(LauncherConfig? Config, string? ErrorMessage)> GetLauncherConfigAsync()
        {
            var authData = _authService.GetCurrentAuthData();
            if (authData == null) return (null, "Not logged in.");
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/launcher/configuration/eft");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (null, $"API Error: {response.StatusCode}");
                
                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());
#if DEBUG
                Console.WriteLine($"--- Response Body for /configuration/eft ---\n\r{JToken.Parse(decompressedJson).ToString(Newtonsoft.Json.Formatting.Indented)}\n\r-------------------------------------------------");
#endif
                
                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0) return (null, json["errmsg"]?.Value<string>());
                return (json["data"].ToObject<LauncherConfig>(), null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        public async Task<(GameInstallInfo? Info, string? ErrorMessage)> GetGameInstallInfoAsync()
        {
            var authData = _authService.GetCurrentAuthData();
            if (authData == null) return (null, "Not logged in.");
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/launcher/game-installation/eft");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (null, $"API Error: {response.StatusCode}");

                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());
#if DEBUG
                Console.WriteLine($"--- Response Body for /game-installation/eft ---\n\r{JToken.Parse(decompressedJson).ToString(Newtonsoft.Json.Formatting.Indented)}\n\r-------------------------------------------------");
#endif

                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0) return (null, json["errmsg"]?.Value<string>());
                var dataToken = json["data"];
                if (dataToken == null || dataToken.Type == JTokenType.Null || !dataToken.HasValues) return (null, "Server returned empty data for game installation info.");
                return (dataToken.ToObject<GameInstallInfo>(), null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        public async Task<(List<GameUpdate>? Updates, string? ErrorMessage)> GetGameUpdatesAsync()
        {
            var authData = _authService.GetCurrentAuthData();
            if (authData == null) return (null, "Not logged in.");
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/launcher/game-updates/eft");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (null, "API Error");
                
                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());
#if DEBUG
                Console.WriteLine($"--- Response Body for /game-updates/eft ---\n\r{JToken.Parse(decompressedJson).ToString(Newtonsoft.Json.Formatting.Indented)}\n\r-------------------------------------------------");
#endif
                
                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0) return (null, json["errmsg"]?.Value<string>());
                return (json["data"].ToObject<List<GameUpdate>>(), null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }

        private string DecompressResponse(byte[] compressedData)
        {
            using (var inputStream = new MemoryStream(compressedData, 2, compressedData.Length - 2))
            using (var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress))
            using (var outputStream = new MemoryStream())
            {
                deflateStream.CopyTo(outputStream);
                return Encoding.UTF8.GetString(outputStream.ToArray());
            }
        }
    }
}