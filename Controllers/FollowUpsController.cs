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
        private readonly IAuditService _auditService;

        public FollowUpsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: FollowUps
        public async Task<IActionResult> Index(string? status, string? type, DateTime? date, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            var roles = await _userManager.GetRolesAsync(user);
            var isSales = roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager");

            IQueryable<FollowUp> query = _context.FollowUps
                .Include(f => f.Customer)
                .Include(f => f.Lead)
                .Include(f => f.Opportunity)
                .Include(f => f.AssignedUser);

            if (isSales)
            {
                query = query.Where(f => f.AssignedTo == user.Id);
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

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName");

            return View(items);
        }

        // GET: FollowUps/Create
        public async Task<IActionResult> Create(int? customerId, int? leadId, int? opportunityId)
        {
            var fu = new FollowUp
            {
                FollowUpDate = DateTime.UtcNow.Date.AddDays(1),
                Status = "Planned",
                FollowUpType = "Call",
                CustomerId = customerId,
                LeadId = leadId,
                OpportunityId = opportunityId
            };

            var user = await _userManager.GetUserAsync(User);
            fu.AssignedTo = user?.Id;

            await PopulateDropDownsAsync(fu);
            return View(fu);
        }

        // POST: FollowUps/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FollowUp followUp)
        {
            // Business rule: Follow-up date cannot be earlier than today for a new/planned follow-up
            if (followUp.Status == "Planned" && followUp.FollowUpDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("FollowUpDate", "Follow-up date cannot be earlier than today.");
            }

            if (ModelState.IsValid)
            {
                followUp.CreatedDate = DateTime.UtcNow;
                _context.Add(followUp);
                await _context.SaveChangesAsync();

                var user = await _userManager.GetUserAsync(User);
                await _auditService.LogAsync(
                    user?.Id,
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
            var followUp = await _context.FollowUps.FindAsync(id);
            if (followUp == null) return NotFound();

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

            var user = await _userManager.GetUserAsync(User);
            await _auditService.LogAsync(
                user?.Id,
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

            await PopulateDropDownsAsync(followUp);
            return View(followUp);
        }

        // POST: FollowUps/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FollowUp followUp)
        {
            if (id != followUp.FollowUpId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.FollowUps.AsNoTracking().FirstOrDefaultAsync(f => f.FollowUpId == id);
                    if (existing == null) return NotFound();

                    followUp.CreatedDate = existing.CreatedDate;
                    _context.Update(followUp);
                    await _context.SaveChangesAsync();

                    var user = await _userManager.GetUserAsync(User);
                    await _auditService.LogAsync(
                        user?.Id,
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
            if (followUp != null)
            {
                var user = await _userManager.GetUserAsync(User);
                _context.FollowUps.Remove(followUp);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user?.Id,
                    "Delete",
                    "FollowUp",
                    id.ToString(),
                    followUp.Subject,
                    null,
                    "Success",
                    $"Deleted follow-up '{followUp.Subject}'",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = "Follow-up deleted.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropDownsAsync(FollowUp fu)
        {
            ViewBag.CustomersList = new SelectList(await _context.Customers.OrderBy(c => c.CustomerName).ToListAsync(), "CustomerId", "CustomerName", fu.CustomerId);
            ViewBag.LeadsList = new SelectList(await _context.Leads.OrderBy(l => l.LeadName).ToListAsync(), "LeadId", "LeadName", fu.LeadId);
            ViewBag.OpportunitiesList = new SelectList(await _context.Opportunities.OrderBy(o => o.OpportunityName).ToListAsync(), "OpportunityId", "OpportunityName", fu.OpportunityId);
            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", fu.AssignedTo);
        }
    }
}
