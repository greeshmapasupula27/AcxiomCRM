using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _auditService;

        public UsersController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService auditService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = auditService;
        }

        // GET: Users
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.Include(u => u.Manager).ToListAsync();
            var list = new List<UserListViewModel>();

            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                var isLockedOut = await _userManager.IsLockedOutAsync(u);

                list.Add(new UserListViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email ?? "",
                    Role = roles.FirstOrDefault() ?? u.PrimaryRole,
                    ManagerId = u.ManagerId,
                    ManagerName = u.Manager?.FullName,
                    IsActive = u.IsActive,
                    IsLockedOut = isLockedOut,
                    AccessFailedCount = u.AccessFailedCount,
                    LockoutEnd = u.LockoutEnd?.UtcDateTime,
                    CreatedDate = u.CreatedDate
                });
            }

            return View(list);
        }

        // GET: Users/Create
        public async Task<IActionResult> Create()
        {
            var managers = await GetManagersListAsync();
            ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managers, "Id", "FullName");
            return View(new RegisterViewModel());
        }

        // POST: Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var managers = await GetManagersListAsync();
                ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managers, "Id", "FullName", model.ManagerId);
                return View(model);
            }

            var existing = await _userManager.FindByEmailAsync(model.Email);
            if (existing != null)
            {
                ModelState.AddModelError("Email", "Email already in use.");
                var managers = await GetManagersListAsync();
                ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managers, "Id", "FullName", model.ManagerId);
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                PrimaryRole = model.Role,
                ManagerId = string.IsNullOrWhiteSpace(model.ManagerId) ? null : model.ManagerId,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                if (!await _roleManager.RoleExistsAsync(model.Role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(model.Role));
                }
                await _userManager.AddToRoleAsync(user, model.Role);

                var currentUserId = _userManager.GetUserId(User);
                await _auditService.LogAsync(
                    currentUserId,
                    "Create",
                    "User",
                    user.Id,
                    null,
                    $"User: {user.Email}, Role: {model.Role}, ManagerId: {user.ManagerId}",
                    "Success",
                    $"Admin created user {user.Email} with role {model.Role}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"User '{user.FullName}' created successfully.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var err in result.Errors)
            {
                ModelState.AddModelError(string.Empty, err.Description);
            }

            var managerList = await GetManagersListAsync();
            ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managerList, "Id", "FullName", model.ManagerId);
            return View(model);
        }

        // POST: Users/ToggleStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (user.Id == currentUserId)
            {
                TempData["ErrorMessage"] = "You cannot deactivate your own admin account.";
                return RedirectToAction(nameof(Index));
            }

            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);

            await _auditService.LogAsync(
                currentUserId,
                "Role Change",
                "User",
                user.Id,
                $"IsActive: {!user.IsActive}",
                $"IsActive: {user.IsActive}",
                "Success",
                $"Admin {(user.IsActive ? "activated" : "deactivated")} user {user.Email}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"User '{user.FullName}' is now {(user.IsActive ? "Active" : "Inactive")}.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Users/EditRole/5
        public async Task<IActionResult> EditRole(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            var currentRole = roles.FirstOrDefault() ?? user.PrimaryRole;

            var managers = await GetManagersListAsync();
            // Don't let a user be their own manager
            managers = managers.Where(m => m.Id != user.Id).ToList();
            ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managers, "Id", "FullName", user.ManagerId);

            return View(new UserEditRoleViewModel
            {
                UserId = user.Id,
                UserEmail = user.Email ?? "",
                FullName = user.FullName,
                CurrentRole = currentRole,
                NewRole = currentRole,
                ManagerId = user.ManagerId
            });
        }

        // POST: Users/EditRole/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRole(UserEditRoleViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var managers = await GetManagersListAsync();
                ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managers, "Id", "FullName", model.ManagerId);
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (user.Id == currentUserId && model.NewRole != "Admin")
            {
                ModelState.AddModelError(string.Empty, "You cannot remove the Admin role from your own current logged-in account.");
                var managers = await GetManagersListAsync();
                ViewBag.ManagersList = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(managers, "Id", "FullName", model.ManagerId);
                return View(model);
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var oldRole = currentRoles.FirstOrDefault() ?? user.PrimaryRole;
            var oldManagerId = user.ManagerId;
            var targetManagerId = string.IsNullOrWhiteSpace(model.ManagerId) ? null : model.ManagerId;

            bool roleChanged = oldRole != model.NewRole;
            bool managerChanged = oldManagerId != targetManagerId;

            if (roleChanged)
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
                if (!await _roleManager.RoleExistsAsync(model.NewRole))
                {
                    await _roleManager.CreateAsync(new IdentityRole(model.NewRole));
                }
                await _userManager.AddToRoleAsync(user, model.NewRole);
                user.PrimaryRole = model.NewRole;
            }

            if (managerChanged)
            {
                user.ManagerId = targetManagerId;
            }

            if (roleChanged || managerChanged)
            {
                await _userManager.UpdateAsync(user);

                await _auditService.LogAsync(
                    currentUserId,
                    "Role Change",
                    "User",
                    user.Id,
                    $"Role: {oldRole}, ManagerId: {oldManagerId}",
                    $"Role: {model.NewRole}, ManagerId: {user.ManagerId}",
                    "Success",
                    $"Admin updated user {user.Email} (Role: {oldRole}->{model.NewRole}, ManagerId: {oldManagerId}->{user.ManagerId})",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Profile for {user.FullName} updated successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<List<ApplicationUser>> GetManagersListAsync()
        {
            var managerUsers = await _userManager.GetUsersInRoleAsync("Manager");
            return managerUsers.Where(u => u.IsActive).OrderBy(u => u.FullName).ToList();
        }

        // POST: Users/Unlock/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unlock(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);

            var currentUserId = _userManager.GetUserId(User);
            await _auditService.LogAsync(
                currentUserId,
                "Security",
                "User",
                user.Id,
                "LockedOut: true",
                "LockedOut: false",
                "Success",
                $"Admin unlocked account for {user.Email}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Account for '{user.FullName}' unlocked successfully.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Users/ResetPassword/5
        public async Task<IActionResult> ResetPassword(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null) return NotFound();

            return View(new ResetPasswordAdminViewModel
            {
                UserId = user.Id,
                UserEmail = user.Email ?? ""
            });
        }

        // POST: Users/ResetPassword/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordAdminViewModel model)
        {
            if (!ModelState.IsValid) return View(model);

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user == null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);

            if (result.Succeeded)
            {
                var currentUserId = _userManager.GetUserId(User);
                await _auditService.LogAsync(
                    currentUserId,
                    "Security",
                    "User",
                    user.Id,
                    null,
                    "Password Reset",
                    "Success",
                    $"Admin reset password for {user.Email}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Password for {user.Email} has been reset successfully.";
                return RedirectToAction(nameof(Index));
            }

            foreach (var err in result.Errors)
            {
                ModelState.AddModelError(string.Empty, err.Description);
            }

            return View(model);
        }
    }
}
