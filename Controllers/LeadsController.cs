using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AcxiomCRM.Controllers
{
    [Authorize]
    public class LeadsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;

        public LeadsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: Leads
        public async Task<IActionResult> Index(string? search, string? status, string? priority, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            var roles = await _userManager.GetRolesAsync(user);
            var isSales = roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager");

            IQueryable<Lead> query = _context.Leads.Include(l => l.AssignedUser);

            if (isSales)
            {
                query = query.Where(l => l.AssignedTo == user.Id);
            }

            // Search filter (Section 17.13: Lead Name, Company, Status, Assigned User)
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(l => l.LeadName.Contains(search) ||
                                         (l.CompanyName != null && l.CompanyName.Contains(search)) ||
                                         l.Email.Contains(search) ||
                                         l.Phone.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(l => l.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(priority))
            {
                query = query.Where(l => l.Priority == priority);
            }

            if (!string.IsNullOrWhiteSpace(assignedTo))
            {
                query = query.Where(l => l.AssignedTo == assignedTo);
            }

            const int pageSize = 10;
            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(l => l.CreatedDate)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync();

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentPriority = priority;
            ViewBag.CurrentAssignedTo = assignedTo;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;

            // Dropdowns
            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName");

            return View(items);
        }

        // GET: Leads/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var lead = await _context.Leads
                .Include(l => l.AssignedUser)
                .Include(l => l.Opportunities)
                .Include(l => l.FollowUps)
                .Include(l => l.Activities)
                .FirstOrDefaultAsync(m => m.LeadId == id);

            if (lead == null) return NotFound();

            return View(lead);
        }

        // GET: Leads/Create
        public async Task<IActionResult> Create()
        {
            var lead = new Lead
            {
                LeadCode = "LEAD-" + new Random().Next(1000, 9999),
                Status = "New",
                Priority = "Medium",
                ExpectedValue = 10000
            };

            var user = await _userManager.GetUserAsync(User);
            lead.AssignedTo = user?.Id;

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // POST: Leads/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Lead lead)
        {
            if (lead.ExpectedValue <= 0)
            {
                ModelState.AddModelError("ExpectedValue", "Expected value must be greater than 0.");
            }

            if (ModelState.IsValid)
            {
                lead.CreatedDate = DateTime.UtcNow;
                _context.Add(lead);
                await _context.SaveChangesAsync();

                var user = await _userManager.GetUserAsync(User);
                await _auditService.LogAsync(
                    user?.Id,
                    "Create",
                    "Lead",
                    lead.LeadId.ToString(),
                    null,
                    JsonSerializer.Serialize(new { lead.LeadName, lead.Email, lead.Status, lead.ExpectedValue }),
                    "Success",
                    $"Captured lead {lead.LeadName} ({lead.LeadCode})",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' created successfully.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // GET: Leads/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // POST: Leads/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Lead lead)
        {
            if (id != lead.LeadId) return NotFound();

            if (lead.ExpectedValue <= 0)
            {
                ModelState.AddModelError("ExpectedValue", "Expected value must be greater than 0.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == id);
                    if (existing == null) return NotFound();

                    lead.CreatedDate = existing.CreatedDate;
                    _context.Update(lead);
                    await _context.SaveChangesAsync();

                    var user = await _userManager.GetUserAsync(User);
                    await _auditService.LogAsync(
                        user?.Id,
                        "Update",
                        "Lead",
                        lead.LeadId.ToString(),
                        JsonSerializer.Serialize(new { existing.LeadName, existing.Status, existing.ExpectedValue, existing.AssignedTo }),
                        JsonSerializer.Serialize(new { lead.LeadName, lead.Status, lead.ExpectedValue, lead.AssignedTo }),
                        "Success",
                        $"Updated lead {lead.LeadName}",
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Leads.AnyAsync(e => e.LeadId == lead.LeadId)) return NotFound();
                    else throw;
                }
            }

            ViewBag.UsersList = new SelectList(await _context.Users.Where(u => u.IsActive).ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // GET: Leads/Convert/5
        public async Task<IActionResult> Convert(int? id)
        {
            if (id == null) return NotFound();

            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();

            if (lead.Status == "Converted")
            {
                TempData["ErrorMessage"] = "This lead has already been converted.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var vm = new LeadConversionViewModel
            {
                LeadId = lead.LeadId,
                LeadCode = lead.LeadCode,
                LeadName = lead.LeadName,
                Email = lead.Email,
                Phone = lead.Phone,
                CompanyName = lead.CompanyName,
                OpportunityName = $"{lead.CompanyName ?? lead.LeadName} - Sales Deal",
                OpportunityAmount = lead.ExpectedValue > 0 ? lead.ExpectedValue : 25000,
                Probability = 50,
                ExpectedCloseDate = DateTime.UtcNow.AddDays(30),
                CreateCustomer = true,
                CreateOpportunity = true
            };

            return View(vm);
        }

        // POST: Leads/Convert
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Convert(LeadConversionViewModel model)
        {
            var lead = await _context.Leads.FindAsync(model.LeadId);
            if (lead == null) return NotFound();

            if (model.CreateOpportunity && model.OpportunityAmount <= 0)
            {
                ModelState.AddModelError("OpportunityAmount", "Opportunity Amount must be greater than 0.");
            }

            if (model.CreateOpportunity && model.ExpectedCloseDate < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.GetUserAsync(User);
            Customer? customer = null;

            // 1. Create or link Customer
            if (model.CreateCustomer)
            {
                // Check if customer already exists by email
                customer = await _context.Customers.FirstOrDefaultAsync(c => c.Email.ToLower() == lead.Email.ToLower());
                if (customer == null)
                {
                    customer = new Customer
                    {
                        CustomerCode = "CUST-" + new Random().Next(1000, 9999),
                        CustomerName = lead.LeadName,
                        Email = lead.Email,
                        Phone = lead.Phone,
                        CompanyName = lead.CompanyName,
                        Status = "Active",
                        CreatedDate = DateTime.UtcNow,
                        CreatedBy = lead.AssignedTo ?? user?.Id,
                        Notes = $"Converted from Lead {lead.LeadCode}. {model.Notes}"
                    };
                    _context.Customers.Add(customer);
                    await _context.SaveChangesAsync();
                }
            }

            // 2. Create Opportunity
            if (model.CreateOpportunity)
            {
                var opportunity = new Opportunity
                {
                    OpportunityName = model.OpportunityName ?? $"{lead.LeadName} Deal",
                    CustomerId = customer?.CustomerId,
                    LeadId = lead.LeadId,
                    Amount = model.OpportunityAmount,
                    Stage = "Proposal",
                    Probability = model.Probability,
                    ExpectedCloseDate = model.ExpectedCloseDate,
                    Status = "Open",
                    CreatedDate = DateTime.UtcNow,
                    AssignedTo = lead.AssignedTo ?? user?.Id,
                    Notes = $"Created via lead conversion. {model.Notes}"
                };
                _context.Opportunities.Add(opportunity);
                await _context.SaveChangesAsync();
            }

            // 3. Update Lead Status to Converted
            lead.Status = "Converted";
            _context.Update(lead);
            await _context.SaveChangesAsync();

            // 4. Record conversion in Audit Log (Section 8: Lead-to-Customer Workflow Step 6)
            await _auditService.LogAsync(
                user?.Id,
                "Convert",
                "Lead",
                lead.LeadId.ToString(),
                "Status: Qualified",
                "Status: Converted",
                "Success",
                $"Converted Lead {lead.LeadCode} to Customer ID: {customer?.CustomerId}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' successfully converted!";
            return RedirectToAction(nameof(Details), new { id = lead.LeadId });
        }

        // GET: Leads/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var lead = await _context.Leads
                .Include(l => l.AssignedUser)
                .FirstOrDefaultAsync(m => m.LeadId == id);

            if (lead == null) return NotFound();

            return View(lead);
        }

        // POST: Leads/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var lead = await _context.Leads.FindAsync(id);
            if (lead != null)
            {
                var user = await _userManager.GetUserAsync(User);
                var oldData = JsonSerializer.Serialize(new { lead.LeadName, lead.Email, lead.Status });

                _context.Leads.Remove(lead);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user?.Id,
                    "Delete",
                    "Lead",
                    id.ToString(),
                    oldData,
                    null,
                    "Success",
                    $"Deleted lead {lead.LeadName}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' removed successfully.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
