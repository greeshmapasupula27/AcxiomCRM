using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Email or Username is required.")]
        [Display(Name = "Email or Username")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember Me?")]
        public bool RememberMe { get; set; }
    }

    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Full Name is required.")]
        [StringLength(100, ErrorMessage = "Full Name cannot exceed 100 characters.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        // Note: For public self-registration, role is always enforced as SalesExecutive on server.
        // Admins can select roles when provisioning users in User Management.
        public string Role { get; set; } = "SalesExecutive";

        [Display(Name = "Reporting Manager")]
        public string? ManagerId { get; set; }
    }

    public class UserEditRoleViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Role is required.")]
        [Display(Name = "Role Scope")]
        public string CurrentRole { get; set; } = string.Empty;

        [Required(ErrorMessage = "New role is required.")]
        [Display(Name = "New Role")]
        public string NewRole { get; set; } = "SalesExecutive";

        [Display(Name = "Reporting Manager")]
        public string? ManagerId { get; set; }
    }

    public class ResetPasswordAdminViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        public string UserEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "New password is required.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
        [DataType(DataType.Password)]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$",
            ErrorMessage = "Password must contain uppercase, lowercase, digit, and special character.")]
        public string NewPassword { get; set; } = string.Empty;
    }
}
