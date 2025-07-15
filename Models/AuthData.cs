using System;

namespace SimpleTarkovManager.Models
{
    public class AuthData
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }
}