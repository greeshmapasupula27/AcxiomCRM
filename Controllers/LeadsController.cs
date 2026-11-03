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
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public LeadsController(
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

        // GET: Leads
        public async Task<IActionResult> Index(string? search, string? status, string? priority, string? assignedTo, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Lead> query = _context.Leads.Include(l => l.AssignedUser);

            if (accessibleUserIds != null)
            {
                query = query.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
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

            // Scoped Users List for Dropdown
            var usersListQuery = _context.Users.Where(u => u.IsActive);
            if (accessibleUserIds != null)
            {
                usersListQuery = usersListQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }
            ViewBag.UsersList = new SelectList(await usersListQuery.ToListAsync(), "Id", "FullName");

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

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
            {
                return Forbid();
            }

            return View(lead);
        }

        // GET: Leads/Create
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            var lead = new Lead
            {
                LeadCode = "LEAD-" + new Random().Next(1000, 9999),
                Status = "New",
                Priority = "Medium",
                ExpectedValue = 10000,
                AssignedTo = user?.Id
            };

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user!);
            var usersListQuery = _context.Users.Where(u => u.IsActive);
            if (accessibleUserIds != null)
            {
                usersListQuery = usersListQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }

            ViewBag.UsersList = new SelectList(await usersListQuery.ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // POST: Leads/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Lead lead)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            if (lead.ExpectedValue <= 0)
            {
                ModelState.AddModelError("ExpectedValue", "Expected value must be greater than 0.");
            }

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("SalesExecutive"))
            {
                lead.AssignedTo = user.Id;
            }
            else if (roles.Contains("Manager"))
            {
                if (string.IsNullOrEmpty(lead.AssignedTo)) lead.AssignedTo = user.Id;
                else if (!await _userScopeService.CanAssignToUserAsync(user, lead.AssignedTo))
                {
                    ModelState.AddModelError("AssignedTo", "Managers can only assign leads to themselves or direct team members.");
                }
            }
            else
            {
                if (string.IsNullOrEmpty(lead.AssignedTo)) lead.AssignedTo = user.Id;
                else if (!await _userScopeService.CanAssignToUserAsync(user, lead.AssignedTo))
                {
                    ModelState.AddModelError("AssignedTo", "Selected user is invalid or inactive.");
                }
            }

            if (ModelState.IsValid)
            {
                lead.CreatedDate = DateTime.UtcNow;
                _context.Add(lead);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user.Id,
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

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            var usersListQuery = _context.Users.Where(u => u.IsActive);
            if (accessibleUserIds != null)
            {
                usersListQuery = usersListQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }
            ViewBag.UsersList = new SelectList(await usersListQuery.ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // GET: Leads/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
            {
                return Forbid();
            }

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            var usersListQuery = _context.Users.Where(u => u.IsActive);
            if (accessibleUserIds != null)
            {
                usersListQuery = usersListQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }
            ViewBag.UsersList = new SelectList(await usersListQuery.ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // POST: Leads/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Lead lead)
        {
            if (id != lead.LeadId) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var existing = await _context.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.LeadId == id);
            if (existing == null) return NotFound();

            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, existing.AssignedTo))
            {
                return Forbid();
            }

            if (lead.ExpectedValue <= 0)
            {
                ModelState.AddModelError("ExpectedValue", "Expected value must be greater than 0.");
            }

            var roles = await _userManager.GetRolesAsync(user);
            if (roles.Contains("SalesExecutive"))
            {
                lead.AssignedTo = user.Id;
            }
            else if (roles.Contains("Manager"))
            {
                if (string.IsNullOrEmpty(lead.AssignedTo)) lead.AssignedTo = user.Id;
                else if (!await _userScopeService.CanAssignToUserAsync(user, lead.AssignedTo))
                {
                    ModelState.AddModelError("AssignedTo", "Managers can only assign leads to themselves or direct team members.");
                }
            }
            else
            {
                if (string.IsNullOrEmpty(lead.AssignedTo)) lead.AssignedTo = user.Id;
                else if (!await _userScopeService.CanAssignToUserAsync(user, lead.AssignedTo))
                {
                    ModelState.AddModelError("AssignedTo", "Selected user is invalid or inactive.");
                }
            }

            if (ModelState.IsValid)
            {
                try
                {
                    lead.CreatedDate = existing.CreatedDate;
                    _context.Update(lead);
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(
                        user.Id,
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

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            var usersListQuery = _context.Users.Where(u => u.IsActive);
            if (accessibleUserIds != null)
            {
                usersListQuery = usersListQuery.Where(u => accessibleUserIds.Contains(u.Id));
            }
            ViewBag.UsersList = new SelectList(await usersListQuery.ToListAsync(), "Id", "FullName", lead.AssignedTo);
            return View(lead);
        }

        // GET: Leads/Convert/5
        public async Task<IActionResult> Convert(int? id)
        {
            if (id == null) return NotFound();

            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
            {
                return Forbid();
            }

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

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
            {
                return Forbid();
            }

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

            Customer? customer = null;

            // 1. Create or link Customer
            if (model.CreateCustomer)
            {
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
                        CreatedBy = lead.AssignedTo ?? user.Id,
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
                    AssignedTo = lead.AssignedTo ?? user.Id,
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
                user.Id,
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

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
            {
                return Forbid();
            }

            return View(lead);
        }

        // POST: Leads/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var lead = await _context.Leads.FindAsync(id);
            if (lead == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            if (user == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
            {
                return Forbid();
            }

            var oldData = JsonSerializer.Serialize(new { lead.LeadName, lead.Email, lead.Status });

            _context.Leads.Remove(lead);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                user.Id,
                "Delete",
                "Lead",
                id.ToString(),
                oldData,
                null,
                "Success",
                $"Deleted lead {lead.LeadName}",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            TempData["SuccessMessage"] = $"Lead '{lead.LeadName}' removed successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
