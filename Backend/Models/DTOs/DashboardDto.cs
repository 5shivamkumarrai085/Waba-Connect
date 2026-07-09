using System;
using System.Collections.Generic;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Models.DTOs
{
    public class DashboardDto
    {
        public bool IsConnected { get; set; }
        public string FacebookAppId { get; set; } = string.Empty;
        public string WabaId { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string WebhookUrl { get; set; } = string.Empty;
        public string VerifyToken { get; set; } = string.Empty;
        public Business? Business { get; set; }
        public IEnumerable<WabaPhoneNumber> PhoneNumbers { get; set; } = new List<WabaPhoneNumber>();
        public IEnumerable<Template> Templates { get; set; } = new List<Template>();
        public HealthLog? LatestHealthLog { get; set; }
        public IEnumerable<HealthLog> HealthLogs { get; set; } = new List<HealthLog>();
        public TokenInfoDto TokenInfo { get; set; } = new TokenInfoDto();
    }

    public class TokenInfoDto
    {
        public string AppId { get; set; } = string.Empty;
        public string Application { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public DateTime? IssuedAt { get; set; }
        public IEnumerable<string> Scopes { get; set; } = new List<string>();
    }
}
