using System.Collections.Generic;
using System.Linq;
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
            var phonesList = phones.ToList();
            if (!phonesList.Any()) return;

            foreach (var phone in phonesList)
            {
                var existing = await _context.WabaPhoneNumbers
                    .FirstOrDefaultAsync(p => p.PhoneNumberId == phone.PhoneNumberId);

                if (existing != null)
                {
                    existing.PhoneNumber = phone.PhoneNumber;
                    existing.DisplayName = phone.DisplayName;
                    existing.VerifiedName = phone.VerifiedName;
                    existing.Quality = phone.Quality;
                    existing.Status = phone.Status;
                    existing.MessageLimit = phone.MessageLimit;
                    if (phone.ConnectionId.HasValue)
                    {
                        existing.ConnectionId = phone.ConnectionId;
                    }
                }
                else
                {
                    await _context.WabaPhoneNumbers.AddAsync(phone);
                }
            }

            await _context.SaveChangesAsync();
        }

        public async Task ClearAllAsync()
        {
            _context.WabaPhoneNumbers.RemoveRange(_context.WabaPhoneNumbers);
            await _context.SaveChangesAsync();
        }
    }
}
