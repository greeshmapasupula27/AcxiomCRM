using System.Text;
using AcxiomCRM.Data;
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

        public ReportService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<byte[]> GenerateCsvExportAsync(string reportType, string userRole, string userId)
        {
            var sb = new StringBuilder();

            switch (reportType.ToLower())
            {
                case "customer":
                    sb.AppendLine("CustomerId,CustomerCode,CustomerName,Email,Phone,CompanyName,Status,CreatedDate");
                    var customers = await _context.Customers.AsNoTracking().ToListAsync();
                    foreach (var c in customers)
                    {
                        sb.AppendLine($"\"{c.CustomerId}\",\"{c.CustomerCode}\",\"{c.CustomerName}\",\"{c.Email}\",\"{c.Phone}\",\"{c.CompanyName}\",\"{c.Status}\",\"{c.CreatedDate:yyyy-MM-dd}\"");
                    }
                    break;

                case "lead":
                    sb.AppendLine("LeadId,LeadCode,LeadName,Email,Phone,Company,Source,Status,Priority,ExpectedValue,CreatedDate");
                    var leads = await _context.Leads.AsNoTracking().ToListAsync();
                    foreach (var l in leads)
                    {
                        sb.AppendLine($"\"{l.LeadId}\",\"{l.LeadCode}\",\"{l.LeadName}\",\"{l.Email}\",\"{l.Phone}\",\"{l.CompanyName}\",\"{l.Source}\",\"{l.Status}\",\"{l.Priority}\",\"{l.ExpectedValue}\",\"{l.CreatedDate:yyyy-MM-dd}\"");
                    }
                    break;

                case "opportunity":
                    sb.AppendLine("OpportunityId,Name,Customer,Amount,Stage,Probability,WeightedAmount,ExpectedCloseDate,Status");
                    var opps = await _context.Opportunities.Include(o => o.Customer).AsNoTracking().ToListAsync();
                    foreach (var o in opps)
                    {
                        sb.AppendLine($"\"{o.OpportunityId}\",\"{o.OpportunityName}\",\"{o.Customer?.CustomerName}\",\"{o.Amount}\",\"{o.Stage}\",\"{o.Probability}%\",\"{o.WeightedAmount}\",\"{o.ExpectedCloseDate:yyyy-MM-dd}\",\"{o.Status}\"");
                    }
                    break;

                case "followup":
                    sb.AppendLine("FollowUpId,Subject,Type,Status,Date,Remarks,AssignedUser");
                    var followups = await _context.FollowUps.Include(f => f.AssignedUser).AsNoTracking().ToListAsync();
                    foreach (var f in followups)
                    {
                        sb.AppendLine($"\"{f.FollowUpId}\",\"{f.Subject}\",\"{f.FollowUpType}\",\"{f.Status}\",\"{f.FollowUpDate:yyyy-MM-dd}\",\"{f.Remarks}\",\"{f.AssignedUser?.FullName}\"");
                    }
                    break;

                case "audit":
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
