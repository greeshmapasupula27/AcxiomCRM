using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels
{
    public class LeadConversionViewModel
    {
        public int LeadId { get; set; }

        public string LeadCode { get; set; } = string.Empty;

        [Display(Name = "Lead Name")]
        public string LeadName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Phone { get; set; } = string.Empty;

        [Display(Name = "Company Name")]
        public string? CompanyName { get; set; }

        [Display(Name = "Create Customer Record")]
        public bool CreateCustomer { get; set; } = true;

        [Display(Name = "Create Sales Opportunity")]
        public bool CreateOpportunity { get; set; } = true;

        [Display(Name = "Opportunity Name")]
        [StringLength(150)]
        public string? OpportunityName { get; set; }

        [Range(0.01, 100000000, ErrorMessage = "Opportunity Amount must be greater than 0.")]
        [Display(Name = "Opportunity Amount ($)")]
        public decimal OpportunityAmount { get; set; }

        [Range(0, 100, ErrorMessage = "Probability must be between 0 and 100.")]
        [Display(Name = "Probability (%)")]
        public int Probability { get; set; } = 40;

        [DataType(DataType.Date)]
        [Display(Name = "Expected Close Date")]
        public DateTime ExpectedCloseDate { get; set; } = DateTime.UtcNow.AddDays(30);

        [Display(Name = "Conversion Notes")]
        public string? Notes { get; set; }
    }
}
