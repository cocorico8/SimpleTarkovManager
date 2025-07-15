using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class EftApiService
    {
        private readonly HttpClient _httpClient;
        private readonly AuthService _authService;
        private const string BaseUrl = "https://launcher.escapefromtarkov.com";

        public EftApiService(AuthService authService, HttpMessageHandler handler)
        {
            _authService = authService;
            
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BsgLauncher/14.5.1.3034");
        }

        public async Task<(LauncherConfig? Config, string? ErrorMessage)> GetLauncherConfigAsync()
        {
            var authData = _authService.GetCurrentAuthData();
            if (authData == null || string.IsNullOrEmpty(authData.AccessToken))
            {
                return (null, "User is not logged in.");
            }

            try
            {
                // Create the request and add the Bearer token for authorization
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/launcher/configuration/eft");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
                
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"API returned an error: {response.StatusCode}");
                }

                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());

                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0)
                {
                    // If the token is expired, the error code might indicate that.
                    // A more robust implementation would trigger a token refresh here.
                    return (null, json["errmsg"]?.Value<string>() ?? "An unknown API error occurred.");
                }

                var config = json["data"].ToObject<LauncherConfig>();
                return (config, null);
            }
            catch (Exception ex)
            {
                return (null, $"A network or system error occurred: {ex.Message}");
            }
        }
        
        public async Task<(GameInstallInfo? Info, string? ErrorMessage)> GetGameInstallInfoAsync()
        {
            var authData = _authService.GetCurrentAuthData();
            if (authData == null || string.IsNullOrEmpty(authData.AccessToken))
            {
                return (null, "User is not logged in.");
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/launcher/game-installation/eft");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authData.AccessToken);
        
                HttpResponseMessage response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"API returned an error: {response.StatusCode}");
                }

                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());
        
                var json = JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0)
                {
                    return (null, json["errmsg"]?.Value<string>() ?? "An unknown API error occurred.");
                }

                // Check if the 'data' field is null or an empty object before deserializing
                var dataToken = json["data"];
                if (dataToken == null || dataToken.Type == JTokenType.Null || !dataToken.HasValues)
                {
                    // The server sent a successful response but with no data. This is the likely cause.
                    return (null, "Server returned empty or null data for game installation info.");
                }

                var info = dataToken.ToObject<GameInstallInfo>();
                return (info, null);
            }
            catch (Exception ex)
            {
                return (null, $"A network or system error occurred: {ex.Message}");
            }
        }
        
        public async Task<(List<GameUpdate>? Updates, string? ErrorMessage)> GetGameUpdatesAsync()
        {
            var authData = _authService.GetCurrentAuthData();
            if (authData == null) return (null, "Not logged in.");
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/launcher/game-updates/eft");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authData.AccessToken);
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return (null, "API Error");
                string decompressedJson = DecompressResponse(await response.Content.ReadAsByteArrayAsync());
                var json = Newtonsoft.Json.Linq.JObject.Parse(decompressedJson);
                if (json["err"]?.Value<int>() != 0) return (null, json["errmsg"]?.Value<string>());
                return (json["data"].ToObject<List<GameUpdate>>(), null);
            }
            catch (Exception ex) { return (null, ex.Message); }
        }
        
        /*public async Task<long> GetFileSizeAsync(string relativeUri)
        {
            // We only need to find one working server to get the size
            var downloadServers = _launcherConfig?.Channels?.Instances.Select(i => i.Endpoint);
            if (downloadServers == null) return -1;
    
            foreach(var server in downloadServers)
            {
                try
                {
                    var fullUri = new Uri(new Uri(server), relativeUri);
                    using var request =
                        new HttpRequestMessage(HttpMethod.Head, fullUri); // Use HEAD request for efficiency
                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode && response.Content.Headers.ContentLength.HasValue)
                    {
                        return response.Content.Headers.ContentLength.Value;
                    }
                }
                catch
                {
                    //Ignore errors and try next server
                }
            }
            return -1;
        }*/
        
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