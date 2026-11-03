using System.Text;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services
{
    public interface IReportService
    {
        Task<byte[]> GenerateCsvExportAsync(string reportType, string userRole, string userId);
    }

    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;

        public ReportService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IUserScopeService userScopeService)
        {
            _context = context;
            _userManager = userManager;
            _userScopeService = userScopeService;
        }

        public async Task<byte[]> GenerateCsvExportAsync(string reportType, string userRole, string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            List<string>? accessibleUserIds = null;
            if (user != null)
            {
                accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            }

            var sb = new StringBuilder();

            switch (reportType.ToLower())
            {
                case "customer":
                    sb.AppendLine("CustomerId,CustomerCode,CustomerName,Email,Phone,CompanyName,Status,CreatedDate");
                    IQueryable<Customer> custQuery = _context.Customers.AsNoTracking();
                    if (accessibleUserIds != null)
                    {
                        custQuery = custQuery.Where(c => c.CreatedBy != null && accessibleUserIds.Contains(c.CreatedBy));
                    }
                    var customers = await custQuery.ToListAsync();
                    foreach (var c in customers)
                    {
                        sb.AppendLine($"\"{c.CustomerId}\",\"{c.CustomerCode}\",\"{c.CustomerName}\",\"{c.Email}\",\"{c.Phone}\",\"{c.CompanyName}\",\"{c.Status}\",\"{c.CreatedDate:yyyy-MM-dd}\"");
                    }
                    break;

                case "lead":
                    sb.AppendLine("LeadId,LeadCode,LeadName,Email,Phone,Company,Source,Status,Priority,ExpectedValue,CreatedDate");
                    IQueryable<Lead> leadQuery = _context.Leads.AsNoTracking();
                    if (accessibleUserIds != null)
                    {
                        leadQuery = leadQuery.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
                    }
                    var leads = await leadQuery.ToListAsync();
                    foreach (var l in leads)
                    {
                        sb.AppendLine($"\"{l.LeadId}\",\"{l.LeadCode}\",\"{l.LeadName}\",\"{l.Email}\",\"{l.Phone}\",\"{l.CompanyName}\",\"{l.Source}\",\"{l.Status}\",\"{l.Priority}\",\"{l.ExpectedValue}\",\"{l.CreatedDate:yyyy-MM-dd}\"");
                    }
                    break;

                case "opportunity":
                    sb.AppendLine("OpportunityId,Name,Customer,Amount,Stage,Probability,WeightedAmount,ExpectedCloseDate,Status");
                    IQueryable<Opportunity> oppQuery = _context.Opportunities.Include(o => o.Customer).AsNoTracking();
                    if (accessibleUserIds != null)
                    {
                        oppQuery = oppQuery.Where(o => o.AssignedTo != null && accessibleUserIds.Contains(o.AssignedTo));
                    }
                    var opps = await oppQuery.ToListAsync();
                    foreach (var o in opps)
                    {
                        sb.AppendLine($"\"{o.OpportunityId}\",\"{o.OpportunityName}\",\"{o.Customer?.CustomerName}\",\"{o.Amount}\",\"{o.Stage}\",\"{o.Probability}%\",\"{o.WeightedAmount}\",\"{o.ExpectedCloseDate:yyyy-MM-dd}\",\"{o.Status}\"");
                    }
                    break;

                case "followup":
                    sb.AppendLine("FollowUpId,Subject,Type,Status,Date,Remarks,AssignedUser");
                    IQueryable<FollowUp> fuQuery = _context.FollowUps.Include(f => f.AssignedUser).AsNoTracking();
                    if (accessibleUserIds != null)
                    {
                        fuQuery = fuQuery.Where(f => f.AssignedTo != null && accessibleUserIds.Contains(f.AssignedTo));
                    }
                    var followups = await fuQuery.ToListAsync();
                    foreach (var f in followups)
                    {
                        sb.AppendLine($"\"{f.FollowUpId}\",\"{f.Subject}\",\"{f.FollowUpType}\",\"{f.Status}\",\"{f.FollowUpDate:yyyy-MM-dd}\",\"{f.Remarks}\",\"{f.AssignedUser?.FullName}\"");
                    }
                    break;

                case "audit":
                    // PDF Section 11: Audit Report access is Admin only (or Manager limited if configured)
                    if (userRole != "Admin" && userRole != "Manager")
                    {
                        throw new UnauthorizedAccessException("Audit reports are restricted to administrators.");
                    }
                    sb.AppendLine("AuditLogId,Timestamp,UserId,Action,EntityName,RecordId,Result,Details");
                    var logs = await _context.AuditLogs.OrderByDescending(a => a.CreatedDate).Take(500).AsNoTracking().ToListAsync();
                    foreach (var a in logs)
                    {
                        sb.AppendLine($"\"{a.AuditLogId}\",\"{a.CreatedDate:yyyy-MM-dd HH:mm:ss}\",\"{a.UserId}\",\"{a.Action}\",\"{a.EntityName}\",\"{a.RecordId}\",\"{a.Result}\",\"{a.Details?.Replace("\"", "\"\"")}\"");
                    }
                    break;

                default:
                    sb.AppendLine("Report,GeneratedAt");
                    sb.AppendLine($"\"{reportType}\",\"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\"");
                    break;
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
        }
    }
}
