using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class HealthLogRepository : IHealthLogRepository
    {
        private readonly AppDbContext _context;

        public HealthLogRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<HealthLog>> GetRecentLogsAsync(int count = 10)
        {
            return await _context.HealthLogs
                .OrderByDescending(h => h.CheckedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<HealthLog?> GetLatestLogAsync()
        {
            return await _context.HealthLogs
                .OrderByDescending(h => h.CheckedAt)
                .FirstOrDefaultAsync();
        }

        public async Task AddLogAsync(HealthLog log)
        {
            _context.HealthLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        public async Task ClearAllAsync()
        {
            _context.HealthLogs.RemoveRange(_context.HealthLogs);
            await _context.SaveChangesAsync();
        }
    }
}
