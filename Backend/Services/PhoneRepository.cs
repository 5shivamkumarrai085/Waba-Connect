using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WhatsAppCampaignApi.Data;
using WhatsAppCampaignApi.Services.Interfaces;
using WhatsAppCampaignApi.Models.Entities;

namespace WhatsAppCampaignApi.Services
{
    public class PhoneRepository : IPhoneRepository
    {
        private readonly AppDbContext _context;

        public PhoneRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<WabaPhoneNumber>> GetAllAsync()
        {
            return await _context.WabaPhoneNumbers.ToListAsync();
        }

        public async Task<WabaPhoneNumber?> GetByIdAsync(int id)
        {
            return await _context.WabaPhoneNumbers.FindAsync(id);
        }

        public async Task<WabaPhoneNumber?> GetByNumberIdAsync(string phoneNumberId)
        {
            return await _context.WabaPhoneNumbers.FirstOrDefaultAsync(p => p.PhoneNumberId == phoneNumberId);
        }

        public async Task SaveRangeAsync(IEnumerable<WabaPhoneNumber> phones)
        {
            // Clear existing to keep it simple, or implement a merge logic
            _context.WabaPhoneNumbers.RemoveRange(_context.WabaPhoneNumbers);
            await _context.WabaPhoneNumbers.AddRangeAsync(phones);
            await _context.SaveChangesAsync();
        }

        public async Task ClearAllAsync()
        {
            _context.WabaPhoneNumbers.RemoveRange(_context.WabaPhoneNumbers);
            await _context.SaveChangesAsync();
        }
    }
}
