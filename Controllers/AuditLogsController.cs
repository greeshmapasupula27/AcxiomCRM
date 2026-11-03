using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    public class AuditLogsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditLogsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: AuditLogs
        public async Task<IActionResult> Index(string? userFilter, string? moduleFilter, string? actionFilter, DateTime? startDate, DateTime? endDate, int page = 1)
        {
            IQueryable<AuditLog> query = _context.AuditLogs.Include(a => a.User);

            if (!string.IsNullOrWhiteSpace(userFilter))
            {
                query = query.Where(a => a.UserId == userFilter || (a.User != null && a.User.Email == userFilter));
            }

            if (!string.IsNullOrWhiteSpace(moduleFilter))
            {
                query = query.Where(a => a.EntityName == moduleFilter);
            }

            if (!string.IsNullOrWhiteSpace(actionFilter))
            {
                query = query.Where(a => a.Action == actionFilter);
            }

            if (startDate.HasValue)
            {
                var s = startDate.Value.ToUniversalTime();
                query = query.Where(a => a.CreatedDate >= s);
            }

            if (endDate.HasValue)
            {
                var e = endDate.Value.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
                query = query.Where(a => a.CreatedDate <= e);
            }

            const int pageSize = 20;
            var totalCount = await query.CountAsync();
            var items = await query.OrderByDescending(a => a.CreatedDate)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync();

            ViewBag.CurrentUser = userFilter;
            ViewBag.CurrentModule = moduleFilter;
            ViewBag.CurrentAction = actionFilter;
            ViewBag.StartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.EndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;

            ViewBag.UsersList = new SelectList(await _context.Users.ToListAsync(), "Id", "Email");

            return View(items);
        }
    }
}
