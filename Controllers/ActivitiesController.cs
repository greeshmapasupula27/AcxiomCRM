using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AcxiomCRM.Controllers
{
    [Authorize]
    public class ActivitiesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;

        public ActivitiesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: Activities
        public async Task<IActionResult> Index(string? type, string? status, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            var roles = await _userManager.GetRolesAsync(user);
            var isSales = roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager");

            IQueryable<Activity> query = _context.Activities
                .Include(a => a.Customer)
                .Include(a => a.Lead)
                .Include(a => a.AssignedUser);

            if (isSales)
            {
                query = query.Where(a => a.AssignedTo == user.Id);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(a => a.ActivityType == type);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(a => a.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(assignedTo))
            {
                query = query.Where(a => a.AssignedTo == assignedTo);
            }

            const int pageSize = 10;
            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(a => a.ActivityDate)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync();

            ViewBag.CurrentType = type;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentAssignedTo = assignedTo;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName");

            return View(items);
        }

        // GET: Activities/Create
        public async Task<IActionResult> Create(int? customerId, int? leadId)
        {
            var act = new Activity
            {
                ActivityType = "Call",
                ActivityDate = DateTime.UtcNow,
                Status = "Completed",
                CustomerId = customerId,
                LeadId = leadId
            };

            var user = await _userManager.GetUserAsync(User);
            act.AssignedTo = user?.Id;

            await PopulateDropDownsAsync(act);
            return View(act);
        }

        // POST: Activities/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Activity activity)
        {
            if (ModelState.IsValid)
            {
                activity.CreatedDate = DateTime.UtcNow;
                _context.Add(activity);
                await _context.SaveChangesAsync();

                var user = await _userManager.GetUserAsync(User);
                await _auditService.LogAsync(
                    user?.Id,
                    "Create",
                    "Activity",
                    activity.ActivityId.ToString(),
                    null,
                    JsonSerializer.Serialize(new { activity.ActivityType, activity.Subject, activity.ActivityDate }),
                    "Success",
                    $"Logged {activity.ActivityType}: {activity.Subject}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = "Activity logged successfully.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateDropDownsAsync(activity);
            return View(activity);
        }

        private async Task PopulateDropDownsAsync(Activity act)
        {
            ViewBag.CustomersList = new SelectList(await _context.Customers.OrderBy(c => c.CustomerName).ToListAsync(), "CustomerId", "CustomerName", act.CustomerId);
            ViewBag.LeadsList = new SelectList(await _context.Leads.OrderBy(l => l.LeadName).ToListAsync(), "LeadId", "LeadName", act.LeadId);
            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", act.AssignedTo);
        }
    }
}
