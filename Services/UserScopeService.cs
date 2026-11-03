using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services
{
    public interface IUserScopeService
    {
        Task<List<string>?> GetAccessibleUserIdsAsync(ApplicationUser user);
        Task<bool> CanAccessRecordAsync(ApplicationUser user, string? recordOwnerId);
    }

    public class UserScopeService : IUserScopeService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public UserScopeService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<List<string>?> GetAccessibleUserIdsAsync(ApplicationUser user)
        {
            var roles = await _userManager.GetRolesAsync(user);

            // 1. Admin: Unrestricted (null indicates all records)
            if (roles.Contains("Admin"))
            {
                return null;
            }

            // 2. Manager: Team scope (Self + all SalesExecutives assigned to this Manager)
            if (roles.Contains("Manager"))
            {
                var teamMemberIds = await _context.Users
                    .Where(u => u.ManagerId == user.Id || u.Id == user.Id)
                    .Select(u => u.Id)
                    .ToListAsync();
                return teamMemberIds;
            }

            // 3. SalesExecutive: Strictly own assigned records
            return new List<string> { user.Id };
        }

        public async Task<bool> CanAccessRecordAsync(ApplicationUser user, string? recordOwnerId)
        {
            if (string.IsNullOrEmpty(recordOwnerId)) return true;

            var accessibleIds = await GetAccessibleUserIdsAsync(user);
            if (accessibleIds == null) return true; // Admin

            return accessibleIds.Contains(recordOwnerId);
        }
    }
}
