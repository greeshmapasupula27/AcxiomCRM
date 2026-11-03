using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditService _auditService;

        public AccountController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IAuditService _audit)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _roleManager = roleManager;
            _auditService = _audit;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Dashboard");
            }
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email) ?? await _userManager.FindByNameAsync(model.Email);
            if (user == null)
            {
                await _auditService.LogAsync(null, "Failed Login", "Auth", model.Email, null, null, "Failure", "Unknown username or email", GetIpAddress());
                ModelState.AddModelError(string.Empty, "Invalid login attempt. Please check your credentials.");
                return View(model);
            }

            if (!user.IsActive)
            {
                await _auditService.LogAsync(user.Id, "Failed Login", "Auth", model.Email, null, null, "Failure", "User account is deactivated", GetIpAddress());
                ModelState.AddModelError(string.Empty, "Your account has been deactivated. Please contact an administrator.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user.UserName!, model.Password, model.RememberMe, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                await _auditService.LogAsync(user.Id, "Login", "Auth", user.Email, null, null, "Success", "User logged in successfully", GetIpAddress());
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction("Index", "Dashboard");
            }

            if (result.IsLockedOut)
            {
                await _auditService.LogAsync(user.Id, "Lockout", "Auth", user.Email, null, null, "Warning", "Account locked out due to repeated failed attempts", GetIpAddress());
                return RedirectToAction(nameof(Lockout));
            }

            // Failed login
            await _auditService.LogAsync(user.Id, "Failed Login", "Auth", user.Email, null, null, "Failure", "Incorrect password", GetIpAddress());
            var failedAttempts = user.AccessFailedCount + 1;
            ModelState.AddModelError(string.Empty, $"Invalid password. Failed attempts: {failedAttempts}/5 before temporary lockout.");
            return View(model);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "A user with this email address already exists.");
                return View(model);
            }

            // SECURITY FIX: Public registration must never allow self-assignment of Admin or Manager roles (PDF Section 4.7 & 17.10)
            const string enforcedRole = "SalesExecutive";

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                FullName = model.FullName,
                PrimaryRole = enforcedRole,
                IsActive = true,
                CreatedDate = DateTime.UtcNow
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                if (!await _roleManager.RoleExistsAsync(enforcedRole))
                {
                    await _roleManager.CreateAsync(new IdentityRole(enforcedRole));
                }
                await _userManager.AddToRoleAsync(user, enforcedRole);

                await _auditService.LogAsync(user.Id, "Create", "User", user.Email, null, $"Role: {enforcedRole}", "Success", "Public self-registered new Sales Executive account", GetIpAddress());
                await _signInManager.SignInAsync(user, isPersistent: false);

                return RedirectToAction("Index", "Dashboard");
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            var userId = _userManager.GetUserId(User);
            var userName = User.Identity?.Name;
            await _signInManager.SignOutAsync();
            await _auditService.LogAsync(userId, "Logout", "Auth", userName, null, null, "Success", "User logged out", GetIpAddress());
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Lockout()
        {
            return View();
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private string? GetIpAddress()
        {
            return HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }
}
