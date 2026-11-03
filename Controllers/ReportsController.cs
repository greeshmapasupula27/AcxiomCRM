using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers
{
    [Authorize]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;
        private readonly IReportService _reportService;

        public ReportsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IUserScopeService userScopeService,
            IReportService reportService)
        {
            _context = context;
            _userManager = userManager;
            _userScopeService = userScopeService;
            _reportService = reportService;
        }

        // GET: Reports
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var roles = await _userManager.GetRolesAsync(user);
            ViewBag.IsAdminOrManager = roles.Contains("Admin") || roles.Contains("Manager");

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);

            IQueryable<Customer> custQ = _context.Customers.AsNoTracking();
            IQueryable<Lead> leadQ = _context.Leads.AsNoTracking();
            IQueryable<Opportunity> oppQ = _context.Opportunities.AsNoTracking();
            IQueryable<FollowUp> fuQ = _context.FollowUps.AsNoTracking();

            if (accessibleUserIds != null)
            {
                custQ = custQ.Where(c => c.CreatedBy != null && accessibleUserIds.Contains(c.CreatedBy));
                leadQ = leadQ.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
                oppQ = oppQ.Where(o => o.AssignedTo != null && accessibleUserIds.Contains(o.AssignedTo));
                fuQ = fuQ.Where(f => f.AssignedTo != null && accessibleUserIds.Contains(f.AssignedTo));
            }

            ViewBag.CustomerCount = await custQ.CountAsync();
            ViewBag.LeadCount = await leadQ.CountAsync();
            ViewBag.OpportunityCount = await oppQ.CountAsync();
            ViewBag.TotalPipeline = await oppQ.Where(o => o.Status == "Open").SumAsync(o => o.Amount);
            ViewBag.FollowUpCount = await fuQ.CountAsync();
            ViewBag.AuditCount = (roles.Contains("Admin") || roles.Contains("Manager")) ? await _context.AuditLogs.CountAsync() : 0;

            return View();
        }

        // Pipeline Report (PDF Section 4.11 & 11)
        public async Task<IActionResult> Pipeline()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Opportunity> query = _context.Opportunities
                .Include(o => o.Customer)
                .Include(o => o.AssignedUser)
                .AsNoTracking();

            if (accessibleUserIds != null)
            {
                query = query.Where(o => o.AssignedTo != null && accessibleUserIds.Contains(o.AssignedTo));
            }

            var opportunities = await query.ToListAsync();

            var pipelineByStage = opportunities
                .GroupBy(o => o.Stage)
                .Select(g => new
                {
                    Stage = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(x => x.Amount),
                    WeightedAmount = g.Sum(x => (x.Amount * x.Probability) / 100m)
                })
                .ToList();

            ViewBag.PipelineByStage = pipelineByStage;
            return View(opportunities);
        }

        // Conversion Report (PDF Section 4.11 & 11)
        public async Task<IActionResult> Conversion()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Lead> query = _context.Leads.Include(l => l.AssignedUser).AsNoTracking();

            if (accessibleUserIds != null)
            {
                query = query.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
            }

            var leads = await query.OrderByDescending(l => l.CreatedDate).ToListAsync();

            var totalLeads = leads.Count;
            var convertedLeads = leads.Count(l => l.Status == "Converted");
            var lostLeads = leads.Count(l => l.Status == "Lost");
            var activeLeads = totalLeads - convertedLeads - lostLeads;

            ViewBag.TotalLeads = totalLeads;
            ViewBag.ConvertedLeads = convertedLeads;
            ViewBag.LostLeads = lostLeads;
            ViewBag.ActiveLeads = activeLeads;
            ViewBag.ConversionRate = totalLeads > 0 ? (convertedLeads * 100.0 / totalLeads) : 0;

            return View(leads);
        }

        // Export to CSV (Section 4.11)
        public async Task<IActionResult> ExportCsv(string type)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? "SalesExecutive";

            if (type.ToLower() == "audit" && role != "Admin" && role != "Manager")
            {
                return Forbid();
            }

            var csvBytes = await _reportService.GenerateCsvExportAsync(type, role, user.Id);
            var fileName = $"{type}_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(csvBytes, "text/csv", fileName);
        }
    }
}
