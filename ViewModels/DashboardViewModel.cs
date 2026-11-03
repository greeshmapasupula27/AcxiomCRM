using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels
{
    public class DashboardViewModel
    {
        // Filters
        public string DateFilter { get; set; } = "ThisMonth"; // Today, ThisWeek, ThisMonth, AllTime
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        // KPI Cards
        public int TotalCustomers { get; set; }
        public int TotalLeads { get; set; }
        public int OpenLeads { get; set; }
        public int TotalOpportunities { get; set; }
        public int OpenOpportunities { get; set; }
        public int WonOpportunities { get; set; }
        public int LostOpportunities { get; set; }
        public decimal TotalPipelineValue { get; set; }
        public decimal WeightedPipelineValue { get; set; }
        public int PendingFollowUps { get; set; }
        public int OverdueFollowUps { get; set; }

        // Chart Data (Serialized to JSON for Chart.js)
        public List<string> LeadStatusLabels { get; set; } = new();
        public List<int> LeadStatusCounts { get; set; } = new();

        public List<string> PipelineStageLabels { get; set; } = new();
        public List<decimal> PipelineStageAmounts { get; set; } = new();
        public List<int> PipelineStageCounts { get; set; } = new();

        public List<string> MonthlySalesLabels { get; set; } = new();
        public List<decimal> MonthlySalesAmounts { get; set; } = new();

        // Recent items
        public List<Opportunity> RecentOpportunities { get; set; } = new();
        public List<FollowUp> UpcomingFollowUps { get; set; } = new();
        public List<Lead> RecentLeads { get; set; } = new();
        public List<Activity> RecentActivities { get; set; } = new();
    }
}
