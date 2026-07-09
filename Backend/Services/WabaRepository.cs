using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class WabaRepository : IWabaRepository
    {
        private readonly AppDbContext _context;

        public WabaRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<WabaConfiguration?> GetAsync()
        {
            return await _context.WabaConfigurations.FirstOrDefaultAsync();
        }

        public async Task<WabaConfiguration?> GetByIdAsync(int id)
        {
            return await _context.WabaConfigurations.FindAsync(id);
        }

        public async Task<WabaConfiguration> AddOrUpdateAsync(WabaConfiguration config)
        {
            var existing = await _context.WabaConfigurations.FirstOrDefaultAsync();
            if (existing == null)
            {
                config.CreatedAt = DateTime.UtcNow;
                _context.WabaConfigurations.Add(config);
            }
            else
            {
                existing.FacebookAppId = config.FacebookAppId;
                existing.FacebookAppSecret = config.FacebookAppSecret;
                existing.AccessToken = config.AccessToken;
                existing.WabaId = config.WabaId;
                existing.WebhookUrl = config.WebhookUrl;
                existing.VerifyToken = config.VerifyToken;
                existing.Connected = config.Connected;
                existing.UpdatedAt = DateTime.UtcNow;
                _context.WabaConfigurations.Update(existing);
                config = existing;
            }

            await _context.SaveChangesAsync();
            return config;
        }

        public async Task<bool> DeleteAsync()
        {
            var existing = await _context.WabaConfigurations.FirstOrDefaultAsync();
            if (existing == null) return false;

            _context.WabaConfigurations.Remove(existing);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}
