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
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public ActivitiesController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IUserScopeService userScopeService,
            IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _userScopeService = userScopeService;
            _auditService = auditService;
        }

        // GET: Activities
        public async Task<IActionResult> Index(string? type, string? status, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Activity> query = _context.Activities
                .Include(a => a.Customer)
                .Include(a => a.Lead)
                .Include(a => a.AssignedUser);

            if (accessibleUserIds != null)
            {
                query = query.Where(a => a.AssignedTo != null && accessibleUserIds.Contains(a.AssignedTo));
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

            var usersListQuery = _context.Users.Where(u => u.IsActive);
            if (accessibleUserIds != null)
            {
                usersListQuery = usersListQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }
            ViewBag.UsersList = new SelectList(await usersListQuery.ToListAsync(), "Id", "FullName");

            return View(items);
        }

        // GET: Activities/Create
        public async Task<IActionResult> Create(int? customerId, int? leadId)
        {
            var user = await _userManager.GetUserAsync(User);
            var act = new Activity
            {
                ActivityType = "Call",
                ActivityDate = DateTime.UtcNow,
                Status = "Completed",
                CustomerId = customerId,
                LeadId = leadId,
                AssignedTo = user?.Id
            };

            await PopulateDropDownsAsync(act);
            return View(act);
        }

        // POST: Activities/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Activity activity)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (ModelState.IsValid)
            {
                if (string.IsNullOrEmpty(activity.AssignedTo))
                {
                    activity.AssignedTo = user.Id;
                }

                activity.CreatedDate = DateTime.UtcNow;
                _context.Add(activity);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user.Id,
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
            var user = await _userManager.GetUserAsync(User);
            var accessibleUserIds = user != null ? await _userScopeService.GetAccessibleUserIdsAsync(user) : null;

            IQueryable<Customer> custQuery = _context.Customers;
            IQueryable<Lead> leadQuery = _context.Leads;
            IQueryable<ApplicationUser> userQuery = _context.Users.Where(u => u.IsActive);

            if (accessibleUserIds != null)
            {
                custQuery = custQuery.Where(c => c.CreatedBy != null && accessibleUserIds.Contains(c.CreatedBy));
                leadQuery = leadQuery.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
                userQuery = userQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }

            ViewBag.CustomersList = new SelectList(await custQuery.OrderBy(c => c.CustomerName).ToListAsync(), "CustomerId", "CustomerName", act.CustomerId);
            ViewBag.LeadsList = new SelectList(await leadQuery.OrderBy(l => l.LeadName).ToListAsync(), "LeadId", "LeadName", act.LeadId);
            ViewBag.UsersList = new SelectList(await userQuery.ToListAsync(), "Id", "FullName", act.AssignedTo);
        }
    }
}
