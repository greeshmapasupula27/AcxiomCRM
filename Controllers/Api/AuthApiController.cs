using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers.Api
{
    [Route("api/auth")]
    [ApiController]
    public class AuthApiController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;

        public AuthApiController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            IAuditService auditService)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _auditService = auditService;
        }

        public class LoginApiRequest
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        // POST: api/auth/login (PDF Section 10 & 17.14)
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> Login([FromBody] LoginApiRequest request)
        {
            if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            {
                return BadRequest(new ApiResponse<object> { Success = false, Message = "Email and Password are required." });
            }

            var user = await _userManager.FindByEmailAsync(request.Email) ?? await _userManager.FindByNameAsync(request.Email);
            if (user == null)
            {
                await _auditService.LogAsync(null, "Failed Login", "Auth", request.Email, null, null, "Failure", "API login attempt with unknown email", HttpContext.Connection.RemoteIpAddress?.ToString());
                return Unauthorized(new ApiResponse<object> { Success = false, Message = "Invalid credentials." });
            }

            if (!user.IsActive)
            {
                await _auditService.LogAsync(user.Id, "Failed Login", "Auth", user.Email, null, null, "Failure", "Deactivated user attempted API login", HttpContext.Connection.RemoteIpAddress?.ToString());
                return Unauthorized(new ApiResponse<object> { Success = false, Message = "Account is deactivated." });
            }

            var result = await _signInManager.PasswordSignInAsync(user.UserName!, request.Password, isPersistent: true, lockoutOnFailure: true);

            if (result.Succeeded)
            {
                var roles = await _userManager.GetRolesAsync(user);
                await _auditService.LogAsync(user.Id, "Login", "Auth", user.Email, null, null, "Success", "User logged in via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Authentication successful.",
                    Data = new
                    {
                        UserId = user.Id,
                        user.Email,
                        user.FullName,
                        Roles = roles
                    }
                });
            }

            if (result.IsLockedOut)
            {
                await _auditService.LogAsync(user.Id, "Lockout", "Auth", user.Email, null, null, "Warning", "Account locked out via API failed logins", HttpContext.Connection.RemoteIpAddress?.ToString());
                return StatusCode(423, new ApiResponse<object> { Success = false, Message = "Account is temporarily locked out." });
            }

            await _auditService.LogAsync(user.Id, "Failed Login", "Auth", user.Email, null, null, "Failure", "Incorrect API password attempt", HttpContext.Connection.RemoteIpAddress?.ToString());
            return Unauthorized(new ApiResponse<object> { Success = false, Message = "Invalid credentials." });
        }

        // POST: api/auth/logout (PDF Section 10)
        [HttpPost("logout")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<bool>>> Logout()
        {
            var userId = _userManager.GetUserId(User);
            await _signInManager.SignOutAsync();
            await _auditService.LogAsync(userId, "Logout", "Auth", User.Identity?.Name, null, null, "Success", "Logged out via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());
            return Ok(new ApiResponse<bool> { Success = true, Message = "Logged out successfully.", Data = true });
        }
    }
}
