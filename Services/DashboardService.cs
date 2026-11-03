using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly ApplicationDbContext _context;

        public DashboardService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DashboardViewModel> GetDashboardMetricsAsync(string userId, string role, string dateFilter, DateTime? startDate = null, DateTime? endDate = null)
        {
            var vm = new DashboardViewModel
            {
                DateFilter = dateFilter,
                StartDate = startDate,
                EndDate = endDate
            };

            // Calculate date window
            DateTime? fromDate = null;
            DateTime? toDate = null;
            var now = DateTime.UtcNow;

            switch (dateFilter)
            {
                case "Today":
                    fromDate = now.Date;
                    toDate = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case "ThisWeek":
                    var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
                    fromDate = startOfWeek;
                    toDate = startOfWeek.AddDays(7).AddTicks(-1);
                    break;
                case "ThisMonth":
                    fromDate = new DateTime(now.Year, now.Month, 1);
                    toDate = fromDate.Value.AddMonths(1).AddTicks(-1);
                    break;
                case "Custom":
                    if (startDate.HasValue) fromDate = startDate.Value.ToUniversalTime();
                    if (endDate.HasValue) toDate = endDate.Value.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
                    break;
                default: // AllTime
                    break;
            }

            // Base queries
            IQueryable<Customer> customerQ = _context.Customers.AsNoTracking();
            IQueryable<Lead> leadQ = _context.Leads.AsNoTracking();
            IQueryable<Opportunity> oppQ = _context.Opportunities.Include(o => o.Customer).AsNoTracking();
            IQueryable<FollowUp> followUpQ = _context.FollowUps.Include(f => f.Customer).Include(f => f.Lead).AsNoTracking();
            IQueryable<Activity> activityQ = _context.Activities.Include(a => a.Customer).Include(a => a.Lead).AsNoTracking();

            // Role Scoping
            if (role == "SalesExecutive")
            {
                customerQ = customerQ.Where(c => c.CreatedBy == userId);
                leadQ = leadQ.Where(l => l.AssignedTo == userId);
                oppQ = oppQ.Where(o => o.AssignedTo == userId);
                followUpQ = followUpQ.Where(f => f.AssignedTo == userId);
                activityQ = activityQ.Where(a => a.AssignedTo == userId);
            }
            // Admin and Manager can view all team records

            // Apply date filters where applicable to creation / activity
            if (fromDate.HasValue)
            {
                customerQ = customerQ.Where(c => c.CreatedDate >= fromDate.Value);
                leadQ = leadQ.Where(l => l.CreatedDate >= fromDate.Value);
                oppQ = oppQ.Where(o => o.CreatedDate >= fromDate.Value);
            }
            if (toDate.HasValue)
            {
                customerQ = customerQ.Where(c => c.CreatedDate <= toDate.Value);
                leadQ = leadQ.Where(l => l.CreatedDate <= toDate.Value);
                oppQ = oppQ.Where(o => o.CreatedDate <= toDate.Value);
            }

            // Metric cards
            vm.TotalCustomers = await customerQ.CountAsync();
            vm.TotalLeads = await leadQ.CountAsync();
            vm.OpenLeads = await leadQ.CountAsync(l => l.Status != "Converted" && l.Status != "Lost");

            vm.TotalOpportunities = await oppQ.CountAsync();
            vm.OpenOpportunities = await oppQ.CountAsync(o => o.Status == "Open" && o.Stage != "Won" && o.Stage != "Lost");
            vm.WonOpportunities = await oppQ.CountAsync(o => o.Stage == "Won" || o.Status == "Won");
            vm.LostOpportunities = await oppQ.CountAsync(o => o.Stage == "Lost" || o.Status == "Lost");

            var openOpps = await oppQ.Where(o => o.Status == "Open" && o.Stage != "Won" && o.Stage != "Lost").ToListAsync();
            vm.TotalPipelineValue = openOpps.Sum(o => o.Amount);
            vm.WeightedPipelineValue = openOpps.Sum(o => (o.Amount * o.Probability) / 100m);

            vm.PendingFollowUps = await followUpQ.CountAsync(f => f.Status == "Planned");
            vm.OverdueFollowUps = await followUpQ.CountAsync(f => f.Status == "Planned" && f.FollowUpDate < DateTime.UtcNow.Date);

            // Chart 1: Lead Status (New, Contacted, Qualified, Unqualified, Converted, Lost)
            var leadStatuses = new[] { "New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost" };
            var leadStatusGroup = await leadQ
                .GroupBy(l => l.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);

            foreach (var status in leadStatuses)
            {
                vm.LeadStatusLabels.Add(status);
                vm.LeadStatusCounts.Add(leadStatusGroup.TryGetValue(status, out var count) ? count : 0);
            }

            // Chart 2: Opportunity Pipeline Stages (Qualification, Proposal, Negotiation, Won, Lost)
            var pipelineStages = new[] { "Qualification", "Proposal", "Negotiation", "Won", "Lost" };
            var oppStageGroup = await oppQ
                .GroupBy(o => o.Stage)
                .Select(g => new { Stage = g.Key, TotalAmount = g.Sum(x => x.Amount), Count = g.Count() })
                .ToDictionaryAsync(x => x.Stage, x => x);

            foreach (var stage in pipelineStages)
            {
                vm.PipelineStageLabels.Add(stage);
                if (oppStageGroup.TryGetValue(stage, out var data))
                {
                    vm.PipelineStageAmounts.Add(data.TotalAmount);
                    vm.PipelineStageCounts.Add(data.Count);
                }
                else
                {
                    vm.PipelineStageAmounts.Add(0);
                    vm.PipelineStageCounts.Add(0);
                }
            }

            // Chart 3: Monthly Sales / Won Amount over last 6 months
            for (int i = 5; i >= 0; i--)
            {
                var monthDate = DateTime.UtcNow.AddMonths(-i);
                var monthStart = new DateTime(monthDate.Year, monthDate.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
                var monthName = monthStart.ToString("MMM yyyy");

                var monthlyWon = await _context.Opportunities
                    .Where(o => (o.Stage == "Won" || o.Status == "Won") && o.CreatedDate >= monthStart && o.CreatedDate <= monthEnd)
                    .SumAsync(o => (decimal?)o.Amount) ?? 0m;

                vm.MonthlySalesLabels.Add(monthName);
                vm.MonthlySalesAmounts.Add(monthlyWon);
            }

            // Recent lists
            vm.RecentOpportunities = await oppQ.OrderByDescending(o => o.CreatedDate).Take(5).ToListAsync();
            vm.UpcomingFollowUps = await followUpQ
                .Where(f => f.Status == "Planned")
                .OrderBy(f => f.FollowUpDate)
                .Take(5)
                .ToListAsync();
            vm.RecentLeads = await leadQ.OrderByDescending(l => l.CreatedDate).Take(5).ToListAsync();
            vm.RecentActivities = await activityQ.OrderByDescending(a => a.ActivityDate).Take(5).ToListAsync();

            return vm;
        }
    }
}
