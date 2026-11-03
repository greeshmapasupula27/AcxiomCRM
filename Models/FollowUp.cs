using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AcxiomCRM.Models
{
    public class FollowUp
    {
        [Key]
        public int FollowUpId { get; set; }

        [Display(Name = "Related Customer")]
        public int? CustomerId { get; set; }

        [ForeignKey("CustomerId")]
        public Customer? Customer { get; set; }

        [Display(Name = "Related Lead")]
        public int? LeadId { get; set; }

        [ForeignKey("LeadId")]
        public Lead? Lead { get; set; }

        [Display(Name = "Related Opportunity")]
        public int? OpportunityId { get; set; }

        [ForeignKey("OpportunityId")]
        public Opportunity? Opportunity { get; set; }

        [Required(ErrorMessage = "Follow-up Date is required.")]
        [DataType(DataType.Date)]
        [Display(Name = "Follow-Up Date")]
        public DateTime FollowUpDate { get; set; } = DateTime.UtcNow.Date;

        [Required(ErrorMessage = "Follow-up Type is required.")]
        [MaxLength(40)]
        [Display(Name = "Follow-Up Type")]
        public string FollowUpType { get; set; } = "Call"; // Call, Meeting, Email, Visit, Demo

        [Required(ErrorMessage = "Subject is required.")]
        [StringLength(150, ErrorMessage = "Subject cannot exceed 150 characters.")]
        public string Subject { get; set; } = string.Empty;

        [MaxLength(1000)]
        [Display(Name = "Remarks / Notes")]
        public string? Remarks { get; set; }

        [Required]
        [MaxLength(30)]
        public string Status { get; set; } = "Planned"; // Planned, Completed, Missed, Cancelled

        [Display(Name = "Assigned To")]
        public string? AssignedTo { get; set; }

        [ForeignKey("AssignedTo")]
        public ApplicationUser? AssignedUser { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    }
}
