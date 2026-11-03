using AcxiomCRM.Data;
using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers.Api
{
    [Route("api/leads")]
    [ApiController]
    [Authorize] // PDF Section 10.1 & 17.14
    public class LeadsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public LeadsApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IUserScopeService userScopeService,
            IAuditService auditService)
        {
            _context = context;
            _userManager = userManager;
            _userScopeService = userScopeService;
            _auditService = auditService;
        }

        // GET: api/leads
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<LeadDto>>>> GetLeads([FromQuery] string? status)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Lead> query = _context.Leads.AsNoTracking();

            if (accessibleUserIds != null)
            {
                query = query.Where(l => l.AssignedTo != null && accessibleUserIds.Contains(l.AssignedTo));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(l => l.Status == status);
            }

            var dtos = await query.Select(l => new LeadDto
            {
                LeadId = l.LeadId,
                LeadCode = l.LeadCode,
                LeadName = l.LeadName,
                Email = l.Email,
                Phone = l.Phone,
                CompanyName = l.CompanyName,
                Source = l.Source,
                Status = l.Status,
                Priority = l.Priority,
                ExpectedValue = l.ExpectedValue,
                CreatedDate = l.CreatedDate,
                AssignedTo = l.AssignedTo,
                Notes = l.Notes
            }).ToListAsync();

            return Ok(new ApiResponse<IEnumerable<LeadDto>>
            {
                Success = true,
                Message = $"Retrieved {dtos.Count} lead(s).",
                Data = dtos
            });
        }

        // GET: api/leads/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<LeadDto>>> GetLead(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var l = await _context.Leads.FindAsync(id);
            if (l == null)
            {
                return NotFound(new ApiResponse<LeadDto> { Success = false, Message = $"Lead with ID {id} not found." });
            }

            if (!await _userScopeService.CanAccessRecordAsync(user, l.AssignedTo))
            {
                return Forbid();
            }

            var dto = new LeadDto
            {
                LeadId = l.LeadId,
                LeadCode = l.LeadCode,
                LeadName = l.LeadName,
                Email = l.Email,
                Phone = l.Phone,
                CompanyName = l.CompanyName,
                Source = l.Source,
                Status = l.Status,
                Priority = l.Priority,
                ExpectedValue = l.ExpectedValue,
                CreatedDate = l.CreatedDate,
                AssignedTo = l.AssignedTo,
                Notes = l.Notes
            };

            return Ok(new ApiResponse<LeadDto> { Success = true, Data = dto });
        }

        // POST: api/leads
        [HttpPost]
        public async Task<ActionResult<ApiResponse<LeadDto>>> CreateLead([FromBody] LeadDto dto)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (dto.ExpectedValue <= 0)
            {
                ModelState.AddModelError("ExpectedValue", "Expected Value must be greater than 0.");
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<LeadDto> { Success = false, Message = "Validation failed.", Errors = errors });
            }

            var roles = await _userManager.GetRolesAsync(user);
            string finalAssignedTo;

            if (roles.Contains("SalesExecutive"))
            {
                // SalesExecutives can strictly assign to themselves only
                if (!string.IsNullOrEmpty(dto.AssignedTo) && dto.AssignedTo != user.Id)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<LeadDto>
                    {
                        Success = false,
                        Message = "Sales Executives can only assign records to themselves."
                    });
                }
                finalAssignedTo = user.Id;
            }
            else if (roles.Contains("Manager"))
            {
                finalAssignedTo = string.IsNullOrEmpty(dto.AssignedTo) ? user.Id : dto.AssignedTo;
                if (!await _userScopeService.CanAssignToUserAsync(user, finalAssignedTo))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<LeadDto>
                    {
                        Success = false,
                        Message = "Managers can only assign records to themselves or direct team members."
                    });
                }
            }
            else
            {
                // Admin can assign to any permitted user or self
                finalAssignedTo = string.IsNullOrEmpty(dto.AssignedTo) ? user.Id : dto.AssignedTo;
                if (!await _userScopeService.CanAssignToUserAsync(user, finalAssignedTo))
                {
                    return BadRequest(new ApiResponse<LeadDto>
                    {
                        Success = false,
                        Message = "Assigned user is invalid or inactive."
                    });
                }
            }

            var lead = new Lead
            {
                LeadCode = string.IsNullOrEmpty(dto.LeadCode) ? "LEAD-" + new Random().Next(1000, 9999) : dto.LeadCode,
                LeadName = dto.LeadName,
                Email = dto.Email,
                Phone = dto.Phone,
                CompanyName = dto.CompanyName,
                Source = string.IsNullOrEmpty(dto.Source) ? "Website" : dto.Source,
                Status = string.IsNullOrEmpty(dto.Status) ? "New" : dto.Status,
                Priority = string.IsNullOrEmpty(dto.Priority) ? "Medium" : dto.Priority,
                ExpectedValue = dto.ExpectedValue,
                AssignedTo = finalAssignedTo,
                Notes = dto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            dto.LeadId = lead.LeadId;
            dto.LeadCode = lead.LeadCode;
            dto.AssignedTo = lead.AssignedTo;
            dto.CreatedDate = lead.CreatedDate;

            await _auditService.LogAsync(user.Id, "Create", "Lead", lead.LeadId.ToString(), null, dto.LeadName, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(201, new ApiResponse<LeadDto>
            {
                Success = true,
                Message = "Lead created successfully.",
                Data = dto
            });
        }
    }
}
