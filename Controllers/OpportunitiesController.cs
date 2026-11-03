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
    public class OpportunitiesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;

        public OpportunitiesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: Opportunities
        public async Task<IActionResult> Index(string? search, string? stage, string? status, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            var roles = await _userManager.GetRolesAsync(user);
            var isSales = roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager");

            IQueryable<Opportunity> query = _context.Opportunities
                .Include(o => o.Customer)
                .Include(o => o.AssignedUser);

            if (isSales)
            {
                query = query.Where(o => o.AssignedTo == user.Id);
            }

            // Search filter (Section 17.13: Opportunity Name, Customer, Stage, Status)
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(o => o.OpportunityName.Contains(search) ||
                                         (o.Customer != null && o.Customer.CustomerName.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(stage))
            {
                query = query.Where(o => o.Stage == stage);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(o => o.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(assignedTo))
            {
                query = query.Where(o => o.AssignedTo == assignedTo);
            }

            const int pageSize = 10;
            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(o => o.CreatedDate)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync();

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentStage = stage;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentAssignedTo = assignedTo;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;

            // Totals
            ViewBag.TotalPipeline = await query.SumAsync(o => o.Amount);
            ViewBag.TotalWeighted = await query.SumAsync(o => (o.Amount * o.Probability) / 100m);

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName");

            return View(items);
        }

        // GET: Opportunities/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var opportunity = await _context.Opportunities
                .Include(o => o.Customer)
                .Include(o => o.Lead)
                .Include(o => o.AssignedUser)
                .Include(o => o.FollowUps)
                .FirstOrDefaultAsync(m => m.OpportunityId == id);

            if (opportunity == null) return NotFound();

            return View(opportunity);
        }

        // GET: Opportunities/Create
        public async Task<IActionResult> Create(int? customerId, int? leadId)
        {
            var opp = new Opportunity
            {
                Stage = "Qualification",
                Probability = 25,
                Amount = 10000,
                ExpectedCloseDate = DateTime.UtcNow.AddDays(30),
                CustomerId = customerId,
                LeadId = leadId
            };

            var user = await _userManager.GetUserAsync(User);
            opp.AssignedTo = user?.Id;

            await PopulateDropDownsAsync(opp);
            return View(opp);
        }

        // POST: Opportunities/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Opportunity opportunity)
        {
            // Business Validation Rules (Section 5.3, 5.4, 17.7 & 17.19)
            if (opportunity.Amount <= 0)
            {
                ModelState.AddModelError("Amount", "Opportunity Amount must be greater than 0.");
            }

            if (opportunity.Probability < 0 || opportunity.Probability > 100)
            {
                ModelState.AddModelError("Probability", "Probability must be between 0 and 100.");
            }

            if (opportunity.Stage != "Won" && opportunity.Stage != "Lost" && opportunity.ExpectedCloseDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past.");
            }

            if (ModelState.IsValid)
            {
                // Set status based on stage
                if (opportunity.Stage == "Won") opportunity.Status = "Won";
                else if (opportunity.Stage == "Lost") opportunity.Status = "Lost";
                else opportunity.Status = "Open";

                opportunity.CreatedDate = DateTime.UtcNow;
                _context.Add(opportunity);
                await _context.SaveChangesAsync();

                var user = await _userManager.GetUserAsync(User);
                await _auditService.LogAsync(
                    user?.Id,
                    "Create",
                    "Opportunity",
                    opportunity.OpportunityId.ToString(),
                    null,
                    JsonSerializer.Serialize(new { opportunity.OpportunityName, opportunity.Amount, opportunity.Stage, opportunity.Probability }),
                    "Success",
                    $"Created opportunity {opportunity.OpportunityName} for amount ${opportunity.Amount:N2}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Opportunity '{opportunity.OpportunityName}' created successfully.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateDropDownsAsync(opportunity);
            return View(opportunity);
        }

        // GET: Opportunities/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var opportunity = await _context.Opportunities.FindAsync(id);
            if (opportunity == null) return NotFound();

            await PopulateDropDownsAsync(opportunity);
            return View(opportunity);
        }

        // POST: Opportunities/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Opportunity opportunity)
        {
            if (id != opportunity.OpportunityId) return NotFound();

            if (opportunity.Amount <= 0)
            {
                ModelState.AddModelError("Amount", "Opportunity Amount must be greater than 0.");
            }

            if (opportunity.Probability < 0 || opportunity.Probability > 100)
            {
                ModelState.AddModelError("Probability", "Probability must be between 0 and 100.");
            }

            if (opportunity.Stage != "Won" && opportunity.Stage != "Lost" && opportunity.ExpectedCloseDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Opportunities.AsNoTracking().FirstOrDefaultAsync(o => o.OpportunityId == id);
                    if (existing == null) return NotFound();

                    if (opportunity.Stage == "Won") opportunity.Status = "Won";
                    else if (opportunity.Stage == "Lost") opportunity.Status = "Lost";
                    else opportunity.Status = "Open";

                    opportunity.CreatedDate = existing.CreatedDate;

                    _context.Update(opportunity);
                    await _context.SaveChangesAsync();

                    var user = await _userManager.GetUserAsync(User);
                    await _auditService.LogAsync(
                        user?.Id,
                        "Update",
                        "Opportunity",
                        opportunity.OpportunityId.ToString(),
                        JsonSerializer.Serialize(new { existing.OpportunityName, existing.Amount, existing.Stage, existing.Probability }),
                        JsonSerializer.Serialize(new { opportunity.OpportunityName, opportunity.Amount, opportunity.Stage, opportunity.Probability }),
                        "Success",
                        $"Updated opportunity {opportunity.OpportunityName}",
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["SuccessMessage"] = $"Opportunity '{opportunity.OpportunityName}' updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Opportunities.AnyAsync(e => e.OpportunityId == opportunity.OpportunityId)) return NotFound();
                    else throw;
                }
            }

            await PopulateDropDownsAsync(opportunity);
            return View(opportunity);
        }

        // GET: Opportunities/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var opportunity = await _context.Opportunities
                .Include(o => o.Customer)
                .Include(o => o.AssignedUser)
                .FirstOrDefaultAsync(m => m.OpportunityId == id);

            if (opportunity == null) return NotFound();

            return View(opportunity);
        }

        // POST: Opportunities/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var opportunity = await _context.Opportunities.FindAsync(id);
            if (opportunity != null)
            {
                var user = await _userManager.GetUserAsync(User);
                var oldData = JsonSerializer.Serialize(new { opportunity.OpportunityName, opportunity.Amount, opportunity.Stage });

                _context.Opportunities.Remove(opportunity);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user?.Id,
                    "Delete",
                    "Opportunity",
                    id.ToString(),
                    oldData,
                    null,
                    "Success",
                    $"Deleted opportunity {opportunity.OpportunityName}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Opportunity '{opportunity.OpportunityName}' removed successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropDownsAsync(Opportunity opp)
        {
            ViewBag.CustomersList = new SelectList(await _context.Customers.OrderBy(c => c.CustomerName).ToListAsync(), "CustomerId", "CustomerName", opp.CustomerId);
            ViewBag.LeadsList = new SelectList(await _context.Leads.OrderBy(l => l.LeadName).ToListAsync(), "LeadId", "LeadName", opp.LeadId);
            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", opp.AssignedTo);
        }
    }
}
