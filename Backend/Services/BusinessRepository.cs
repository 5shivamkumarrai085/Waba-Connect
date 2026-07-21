using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class BusinessRepository : IBusinessRepository
    {
        private readonly AppDbContext _context;

        public BusinessRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Business?> GetAsync()
        {
            return await _context.Businesses.OrderBy(x => x.Id).FirstOrDefaultAsync();
        }

        public async Task SaveAsync(Business business)
        {
            var existing = await _context.Businesses.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (existing != null)
            {
                existing.BusinessId = business.BusinessId;
                existing.BusinessName = business.BusinessName;
                existing.Timezone = business.Timezone;
                existing.Status = business.Status;
                _context.Businesses.Update(existing);
            }
            else
            {
                _context.Businesses.Add(business);
            }
            await _context.SaveChangesAsync();
        }

        public async Task ClearAllAsync()
        {
            _context.Businesses.RemoveRange(_context.Businesses);
            await _context.SaveChangesAsync();
        }
    }
}
