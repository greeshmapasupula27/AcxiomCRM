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
        private readonly IReportService _reportService;

        public ReportsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IReportService reportService)
        {
            _context = context;
            _userManager = userManager;
            _reportService = reportService;
        }

        // GET: Reports
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user!);
            ViewBag.IsAdminOrManager = roles.Contains("Admin") || roles.Contains("Manager");

            // Overview summary numbers
            ViewBag.CustomerCount = await _context.Customers.CountAsync();
            ViewBag.LeadCount = await _context.Leads.CountAsync();
            ViewBag.OpportunityCount = await _context.Opportunities.CountAsync();
            ViewBag.TotalPipeline = await _context.Opportunities.Where(o => o.Status == "Open").SumAsync(o => o.Amount);
            ViewBag.FollowUpCount = await _context.FollowUps.CountAsync();
            ViewBag.AuditCount = await _context.AuditLogs.CountAsync();

            return View();
        }

        // Pipeline Report
        public async Task<IActionResult> Pipeline()
        {
            var opportunities = await _context.Opportunities
                .Include(o => o.Customer)
                .Include(o => o.AssignedUser)
                .AsNoTracking()
                .ToListAsync();

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

        // Conversion Report
        public async Task<IActionResult> Conversion()
        {
            var totalLeads = await _context.Leads.CountAsync();
            var convertedLeads = await _context.Leads.CountAsync(l => l.Status == "Converted");
            var lostLeads = await _context.Leads.CountAsync(l => l.Status == "Lost");
            var activeLeads = totalLeads - convertedLeads - lostLeads;

            ViewBag.TotalLeads = totalLeads;
            ViewBag.ConvertedLeads = convertedLeads;
            ViewBag.LostLeads = lostLeads;
            ViewBag.ActiveLeads = activeLeads;
            ViewBag.ConversionRate = totalLeads > 0 ? (convertedLeads * 100.0 / totalLeads) : 0;

            var leads = await _context.Leads.Include(l => l.AssignedUser).OrderByDescending(l => l.CreatedDate).ToListAsync();
            return View(leads);
        }

        // Export to CSV (Section 4.11)
        public async Task<IActionResult> ExportCsv(string type)
        {
            var user = await _userManager.GetUserAsync(User);
            var roles = await _userManager.GetRolesAsync(user!);
            var role = roles.FirstOrDefault() ?? "SalesExecutive";

            var csvBytes = await _reportService.GenerateCsvExportAsync(type, role, user!.Id);
            var fileName = $"{type}_Report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            return File(csvBytes, "text/csv", fileName);
        }
    }
}
