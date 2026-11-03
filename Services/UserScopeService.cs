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
        Task<bool> CanAssignToUserAsync(ApplicationUser currentUser, string? targetUserId);
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
            var accessibleIds = await GetAccessibleUserIdsAsync(user);
            if (accessibleIds == null) return true; // Admin has unrestricted access

            // For non-admin, an unassigned or missing owner cannot pass ownership checks
            if (string.IsNullOrEmpty(recordOwnerId)) return false;

            return accessibleIds.Contains(recordOwnerId);
        }

        public async Task<bool> CanAssignToUserAsync(ApplicationUser currentUser, string? targetUserId)
        {
            var roles = await _userManager.GetRolesAsync(currentUser);

            // 1. Admin: Can assign to any active user (or leave unassigned)
            if (roles.Contains("Admin"))
            {
                if (string.IsNullOrEmpty(targetUserId)) return true;
                return await _context.Users.AnyAsync(u => u.Id == targetUserId && u.IsActive);
            }

            // 2. Manager: Can assign to self or to direct team members (SalesExecutives managed by this Manager)
            if (roles.Contains("Manager"))
            {
                if (string.IsNullOrEmpty(targetUserId)) return false;
                var teamMemberIds = await _context.Users
                    .Where(u => (u.ManagerId == currentUser.Id || u.Id == currentUser.Id) && u.IsActive)
                    .Select(u => u.Id)
                    .ToListAsync();
                return teamMemberIds.Contains(targetUserId);
            }

            // 3. SalesExecutive: Can strictly assign to themselves only
            return targetUserId == currentUser.Id;
        }
    }
}
