using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AcxiomCRM.Models
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        // Primary role (Admin, Manager, SalesExecutive)
        public string PrimaryRole { get; set; } = "SalesExecutive";

        // Team / Manager Hierarchy (PDF Section 7 & 17.10)
        public string? ManagerId { get; set; }

        [ForeignKey("ManagerId")]
        public ApplicationUser? Manager { get; set; }
    }
}
