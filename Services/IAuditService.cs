namespace AcxiomCRM.Services
{
    public interface IAuditService
    {
        Task LogAsync(string? userId, string action, string entityName, string? recordId, string? oldValue, string? newValue, string result = "Success", string? details = null, string? ipAddress = null);
    }
}
