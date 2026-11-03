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
    public class FollowUpsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public FollowUpsController(
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

        // GET: FollowUps
        public async Task<IActionResult> Index(string? status, string? type, DateTime? date, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<FollowUp> query = _context.FollowUps
                .Include(f => f.Customer)
                .Include(f => f.Lead)
                .Include(f => f.Opportunity)
                .Include(f => f.AssignedUser);

            if (accessibleUserIds != null)
            {
                query = query.Where(f => f.AssignedTo != null && accessibleUserIds.Contains(f.AssignedTo));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(f => f.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(f => f.FollowUpType == type);
            }

            if (date.HasValue)
            {
                var dt = date.Value.Date;
                query = query.Where(f => f.FollowUpDate.Date == dt);
            }

            if (!string.IsNullOrWhiteSpace(assignedTo))
            {
                query = query.Where(f => f.AssignedTo == assignedTo);
            }

            const int pageSize = 10;
            var totalCount = await query.CountAsync();
            var items = await query.OrderBy(f => f.FollowUpDate)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync();

            ViewBag.CurrentStatus = status;
            ViewBag.CurrentType = type;
            ViewBag.CurrentDate = date?.ToString("yyyy-MM-dd");
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

        // GET: FollowUps/Create
        public async Task<IActionResult> Create(int? customerId, int? leadId, int? opportunityId)
        {
            var user = await _userManager.GetUserAsync(User);
            var fu = new FollowUp
            {
                FollowUpDate = DateTime.UtcNow.Date.AddDays(1),
                Status = "Planned",
                FollowUpType = "Call",
                CustomerId = customerId,
                LeadId = leadId,
                OpportunityId = opportunityId,
                AssignedTo = user?.Id
            };

            await PopulateDropDownsAsync(fu);
            return View(fu);
        }

        // POST: FollowUps/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FollowUp followUp)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            // Business rule: Follow-up date cannot be earlier than today for a new/planned follow-up
            if (followUp.Status == "Planned" && followUp.FollowUpDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("FollowUpDate", "Follow-up date cannot be earlier than today.");
            }

            if (ModelState.IsValid)
            {
                if (string.IsNullOrEmpty(followUp.AssignedTo))
                {
                    followUp.AssignedTo = user.Id;
                }

                followUp.CreatedDate = DateTime.UtcNow;
                _context.Add(followUp);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user.Id,
                    "Create",
                    "FollowUp",
                    followUp.FollowUpId.ToString(),
                    null,
                    JsonSerializer.Serialize(new { followUp.Subject, followUp.FollowUpDate, followUp.FollowUpType, followUp.Status }),
                    "Success",
                    $"Scheduled follow-up '{followUp.Subject}' on {followUp.FollowUpDate:yyyy-MM-dd}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = "Follow-up scheduled successfully.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateDropDownsAsync(followUp);
            return View(followUp);
        }

        // POST: FollowUps/Complete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id, string? completionRemarks)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();

            if (!await _userScopeService.CanAccessRecordAsync(user, followUp.AssignedTo))
            {
                return Forbid();
            }

            var oldStatus = followUp.Status;
            followUp.Status = "Completed";
            if (!string.IsNullOrEmpty(completionRemarks))
            {
                followUp.Remarks = (followUp.Remarks ?? "") + $" [Completed: {completionRemarks}]";
            }

            _context.Update(followUp);

            // Also record in Activity history!
            var activity = new Activity
            {
                ActivityType = followUp.FollowUpType,
                Subject = $"Follow-up: {followUp.Subject}",
                Description = completionRemarks ?? followUp.Remarks ?? "Completed scheduled follow-up",
                ActivityDate = DateTime.UtcNow,
                CustomerId = followUp.CustomerId,
                LeadId = followUp.LeadId,
                AssignedTo = followUp.AssignedTo,
                Status = "Completed"
            };
            _context.Activities.Add(activity);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                user.Id,
                "Complete",
                "FollowUp",
                id.ToString(),
                $"Status: {oldStatus}",
                "Status: Completed",
                "Success",
                $"Completed follow-up '{followUp.Subject}'",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Follow-up marked as Completed!";
            return RedirectToAction(nameof(Index));
        }

        // GET: FollowUps/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, followUp.AssignedTo))
            {
                return Forbid();
            }

            await PopulateDropDownsAsync(followUp);
            return View(followUp);
        }

        // POST: FollowUps/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FollowUp followUp)
        {
            if (id != followUp.FollowUpId) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var existing = await _context.FollowUps.AsNoTracking().FirstOrDefaultAsync(f => f.FollowUpId == id);
            if (existing == null) return NotFound();

            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, existing.AssignedTo))
            {
                return Forbid();
            }

            // Validation: Follow-up date cannot be in the past for planned follow-up (PDF Section 5.3 & 17.7)
            if (followUp.Status == "Planned" && followUp.FollowUpDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("FollowUpDate", "Follow-up date cannot be earlier than today.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    followUp.CreatedDate = existing.CreatedDate;
                    _context.Update(followUp);
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(
                        user.Id,
                        "Update",
                        "FollowUp",
                        followUp.FollowUpId.ToString(),
                        JsonSerializer.Serialize(new { existing.Subject, existing.FollowUpDate, existing.Status }),
                        JsonSerializer.Serialize(new { followUp.Subject, followUp.FollowUpDate, followUp.Status }),
                        "Success",
                        $"Updated follow-up '{followUp.Subject}'",
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["SuccessMessage"] = "Follow-up updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.FollowUps.AnyAsync(e => e.FollowUpId == followUp.FollowUpId)) return NotFound();
                    else throw;
                }
            }

            await PopulateDropDownsAsync(followUp);
            return View(followUp);
        }

        // POST: FollowUps/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, followUp.AssignedTo))
            {
                return Forbid();
            }

            _context.FollowUps.Remove(followUp);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                user.Id,
                "Delete",
                "FollowUp",
                id.ToString(),
                followUp.Subject,
                null,
                "Success",
                $"Deleted follow-up '{followUp.Subject}'",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = "Follow-up deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropDownsAsync(FollowUp fu)
        {
            var user = await _userManager.GetUserAsync(User);
            var accessibleUserIds = user != null ? await _userScopeService.GetAccessibleUserIdsAsync(user) : null;

            IQueryable<Customer> custQuery = _context.Customers;
            IQueryable<Lead> leadQuery = _context.Leads;
            IQueryable<Opportunity> oppQuery = _context.Opportunities;
            IQueryable<ApplicationUser> userQuery = _context.Users.Where(u => u.IsActive);

            if (accessibleUserIds != null)
            {
                custQuery = custQuery.Where(c => c.CreatedBy != null && accessibleUserIds.Contains(c.CreatedBy));
                leadQuery = leadQuery.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
                oppQuery = oppQuery.Where(o => o.AssignedTo != null && accessibleUserIds.Contains(o.AssignedTo));
                userQuery = userQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }

            ViewBag.CustomersList = new SelectList(await custQuery.OrderBy(c => c.CustomerName).ToListAsync(), "CustomerId", "CustomerName", fu.CustomerId);
            ViewBag.LeadsList = new SelectList(await leadQuery.OrderBy(l => l.LeadName).ToListAsync(), "LeadId", "LeadName", fu.LeadId);
            ViewBag.OpportunitiesList = new SelectList(await oppQuery.OrderBy(o => o.OpportunityName).ToListAsync(), "OpportunityId", "OpportunityName", fu.OpportunityId);
            ViewBag.UsersList = new SelectList(await userQuery.ToListAsync(), "Id", "FullName", fu.AssignedTo);
        }
    }
}
