using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AcxiomCRM.Controllers
{
    [Authorize]
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditService _auditService;

        public CustomersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _auditService = auditService;
        }

        // GET: Customers
        public async Task<IActionResult> Index(string? search, string? status, string? sortBy, int page = 1)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();
            var roles = await _userManager.GetRolesAsync(user);
            var isSales = roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager");

            IQueryable<Customer> query = _context.Customers.Include(c => c.Owner);

            if (isSales)
            {
                query = query.Where(c => c.CreatedBy == user.Id);
            }

            // Search filter (Section 17.13: Customer Name, Email, Phone, Company)
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Trim();
                query = query.Where(c => c.CustomerName.Contains(search) ||
                                         c.Email.Contains(search) ||
                                         c.Phone.Contains(search) ||
                                         (c.CompanyName != null && c.CompanyName.Contains(search)));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(c => c.Status == status);
            }

            // Sorting
            query = sortBy switch
            {
                "name_desc" => query.OrderByDescending(c => c.CustomerName),
                "name" => query.OrderBy(c => c.CustomerName),
                "date_asc" => query.OrderBy(c => c.CreatedDate),
                _ => query.OrderByDescending(c => c.CreatedDate)
            };

            const int pageSize = 10;
            var totalCount = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.CurrentSearch = search;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentSort = sortBy;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;

            return View(items);
        }

        // GET: Customers/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var customer = await _context.Customers
                .Include(c => c.Owner)
                .Include(c => c.Opportunities)
                .Include(c => c.FollowUps)
                .Include(c => c.Activities)
                .FirstOrDefaultAsync(m => m.CustomerId == id);

            if (customer == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user!);
            if (roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager") && customer.CreatedBy != user!.Id)
            {
                return Forbid();
            }

            return View(customer);
        }

        // GET: Customers/Create
        public IActionResult Create()
        {
            var customer = new Customer
            {
                CustomerCode = "CUST-" + new Random().Next(1000, 9999),
                Status = "Active"
            };
            return View(customer);
        }

        // POST: Customers/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Customer customer)
        {
            // Business validations (Section 17.5 & 17.7)
            if (await _context.Customers.AnyAsync(c => c.Email.ToLower() == customer.Email.ToLower()))
            {
                ModelState.AddModelError("Email", "Email address is already in use by another customer.");
            }

            if (await _context.Customers.AnyAsync(c => c.Phone == customer.Phone))
            {
                ModelState.AddModelError("Phone", "Phone number is already in use by another customer.");
            }

            // Check duplicate customer by name + company
            if (!string.IsNullOrEmpty(customer.CompanyName) &&
                await _context.Customers.AnyAsync(c => c.CustomerName.ToLower() == customer.CustomerName.ToLower() &&
                                                       c.CompanyName != null && c.CompanyName.ToLower() == customer.CompanyName.ToLower()))
            {
                ModelState.AddModelError("CustomerName", "A customer with the same name and company already exists.");
            }

            if (ModelState.IsValid)
            {
                var user = await _userManager.GetUserAsync(User);
                customer.CreatedBy = user?.Id;
                customer.CreatedDate = DateTime.UtcNow;

                _context.Add(customer);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user?.Id,
                    "Create",
                    "Customer",
                    customer.CustomerId.ToString(),
                    null,
                    JsonSerializer.Serialize(new { customer.CustomerName, customer.Email, customer.Phone, customer.CompanyName }),
                    "Success",
                    $"Created customer {customer.CustomerName} ({customer.CustomerCode})",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' created successfully.";
                return RedirectToAction(nameof(Index));
            }

            return View(customer);
        }

        // GET: Customers/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user!);
            if (roles.Contains("SalesExecutive") && !roles.Contains("Admin") && !roles.Contains("Manager") && customer.CreatedBy != user!.Id)
            {
                return Forbid();
            }

            return View(customer);
        }

        // POST: Customers/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Customer customer)
        {
            if (id != customer.CustomerId) return NotFound();

            // Validate email & phone uniqueness excluding self
            if (await _context.Customers.AnyAsync(c => c.Email.ToLower() == customer.Email.ToLower() && c.CustomerId != id))
            {
                ModelState.AddModelError("Email", "Email address is already in use by another customer.");
            }

            if (await _context.Customers.AnyAsync(c => c.Phone == customer.Phone && c.CustomerId != id))
            {
                ModelState.AddModelError("Phone", "Phone number is already in use by another customer.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    var existing = await _context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.CustomerId == id);
                    if (existing == null) return NotFound();

                    var user = await _userManager.GetUserAsync(User);
                    customer.ModifiedDate = DateTime.UtcNow;
                    customer.CreatedBy = existing.CreatedBy;
                    customer.CreatedDate = existing.CreatedDate;

                    _context.Update(customer);
                    await _context.SaveChangesAsync();

                    await _auditService.LogAsync(
                        user?.Id,
                        "Update",
                        "Customer",
                        customer.CustomerId.ToString(),
                        JsonSerializer.Serialize(new { existing.CustomerName, existing.Email, existing.Phone, existing.Status }),
                        JsonSerializer.Serialize(new { customer.CustomerName, customer.Email, customer.Phone, customer.Status }),
                        "Success",
                        $"Updated customer {customer.CustomerName}",
                        HttpContext.Connection.RemoteIpAddress?.ToString());

                    TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.Customers.AnyAsync(e => e.CustomerId == customer.CustomerId))
                        return NotFound();
                    else throw;
                }
            }
            return View(customer);
        }

        // GET: Customers/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var customer = await _context.Customers
                .Include(c => c.Owner)
                .FirstOrDefaultAsync(m => m.CustomerId == id);

            if (customer == null) return NotFound();

            return View(customer);
        }

        // POST: Customers/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer != null)
            {
                var user = await _userManager.GetUserAsync(User);
                var oldData = JsonSerializer.Serialize(new { customer.CustomerName, customer.Email, customer.Phone });

                _context.Customers.Remove(customer);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    user?.Id,
                    "Delete",
                    "Customer",
                    id.ToString(),
                    oldData,
                    null,
                    "Success",
                    $"Deleted customer {customer.CustomerName}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' removed successfully.";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
