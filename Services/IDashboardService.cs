using AcxiomCRM.ViewModels;

namespace AcxiomCRM.Services
{
    public interface IDashboardService
    {
        Task<DashboardViewModel> GetDashboardMetricsAsync(string userId, string role, string dateFilter, DateTime? startDate = null, DateTime? endDate = null);
    }
}
