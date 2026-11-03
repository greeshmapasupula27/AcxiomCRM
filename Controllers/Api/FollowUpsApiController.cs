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
    [Route("api/followups")]
    [ApiController]
    [Authorize] // PDF Section 10.1 & 17.14
    public class FollowUpsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public FollowUpsApiController(
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

        // GET: api/followups
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<FollowUpDto>>>> GetFollowUps([FromQuery] string? status)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<FollowUp> query = _context.FollowUps.AsNoTracking();

            if (accessibleUserIds != null)
            {
                query = query.Where(f => f.AssignedTo != null && accessibleUserIds.Contains(f.AssignedTo));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(f => f.Status == status);
            }

            var dtos = await query.Select(f => new FollowUpDto
            {
                FollowUpId = f.FollowUpId,
                CustomerId = f.CustomerId,
                LeadId = f.LeadId,
                OpportunityId = f.OpportunityId,
                FollowUpDate = f.FollowUpDate,
                FollowUpType = f.FollowUpType,
                Subject = f.Subject,
                Remarks = f.Remarks,
                Status = f.Status,
                AssignedTo = f.AssignedTo,
                CreatedDate = f.CreatedDate
            }).ToListAsync();

            return Ok(new ApiResponse<IEnumerable<FollowUpDto>>
            {
                Success = true,
                Message = $"Retrieved {dtos.Count} follow-up record(s).",
                Data = dtos
            });
        }

        // POST: api/followups
        [HttpPost]
        public async Task<ActionResult<ApiResponse<FollowUpDto>>> CreateFollowUp([FromBody] FollowUpDto dto)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (dto.Status == "Planned" && dto.FollowUpDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("FollowUpDate", "Follow-up date cannot be earlier than today.");
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<FollowUpDto> { Success = false, Message = "Validation failed.", Errors = errors });
            }

            // Verify Customer ownership scope if linked
            if (dto.CustomerId.HasValue)
            {
                var customer = await _context.Customers.FindAsync(dto.CustomerId.Value);
                if (customer == null || !await _userScopeService.CanAccessRecordAsync(user, customer.CreatedBy))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<FollowUpDto>
                    {
                        Success = false,
                        Message = "Selected Customer is invalid or not accessible."
                    });
                }
            }

            // Verify Lead ownership scope if linked
            if (dto.LeadId.HasValue)
            {
                var lead = await _context.Leads.FindAsync(dto.LeadId.Value);
                if (lead == null || !await _userScopeService.CanAccessRecordAsync(user, lead.AssignedTo))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<FollowUpDto>
                    {
                        Success = false,
                        Message = "Selected Lead is invalid or not accessible."
                    });
                }
            }

            // Verify Opportunity ownership scope if linked
            if (dto.OpportunityId.HasValue)
            {
                var opp = await _context.Opportunities.FindAsync(dto.OpportunityId.Value);
                if (opp == null || !await _userScopeService.CanAccessRecordAsync(user, opp.AssignedTo))
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<FollowUpDto>
                    {
                        Success = false,
                        Message = "Selected Opportunity is invalid or not accessible."
                    });
                }
            }

            var roles = await _userManager.GetRolesAsync(user);
            string finalAssignedTo;

            if (roles.Contains("SalesExecutive"))
            {
                if (!string.IsNullOrEmpty(dto.AssignedTo) && dto.AssignedTo != user.Id)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<FollowUpDto>
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
                    return StatusCode(StatusCodes.Status403Forbidden, new ApiResponse<FollowUpDto>
                    {
                        Success = false,
                        Message = "Managers can only assign records to themselves or direct team members."
                    });
                }
            }
            else
            {
                finalAssignedTo = string.IsNullOrEmpty(dto.AssignedTo) ? user.Id : dto.AssignedTo;
                if (!await _userScopeService.CanAssignToUserAsync(user, finalAssignedTo))
                {
                    return BadRequest(new ApiResponse<FollowUpDto>
                    {
                        Success = false,
                        Message = "Assigned user is invalid or inactive."
                    });
                }
            }

            var fu = new FollowUp
            {
                CustomerId = dto.CustomerId,
                LeadId = dto.LeadId,
                OpportunityId = dto.OpportunityId,
                FollowUpDate = dto.FollowUpDate,
                FollowUpType = dto.FollowUpType,
                Subject = dto.Subject,
                Remarks = dto.Remarks,
                Status = string.IsNullOrEmpty(dto.Status) ? "Planned" : dto.Status,
                AssignedTo = finalAssignedTo,
                CreatedDate = DateTime.UtcNow
            };

            _context.FollowUps.Add(fu);
            await _context.SaveChangesAsync();

            dto.FollowUpId = fu.FollowUpId;
            dto.CreatedDate = fu.CreatedDate;
            dto.AssignedTo = fu.AssignedTo;

            await _auditService.LogAsync(user.Id, "Create", "FollowUp", fu.FollowUpId.ToString(), null, dto.Subject, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(201, new ApiResponse<FollowUpDto>
            {
                Success = true,
                Message = "Follow-up created successfully.",
                Data = dto
            });
        }
    }

    [Route("api/reports")]
    [ApiController]
    [Authorize] // PDF Section 10.1 & 17.14
    public class ReportsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;

        public ReportsApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IUserScopeService userScopeService)
        {
            _context = context;
            _userManager = userManager;
            _userScopeService = userScopeService;
        }

        // GET: api/reports/pipeline (PDF Section 10 & 17.14)
        [HttpGet("pipeline")]
        public async Task<ActionResult<ApiResponse<object>>> GetPipelineReport()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Opportunity> query = _context.Opportunities.AsNoTracking();

            if (accessibleUserIds != null)
            {
                query = query.Where(o => o.AssignedTo != null && accessibleUserIds.Contains(o.AssignedTo));
            }

            var opportunities = await query.ToListAsync();

            var stageData = opportunities
                .GroupBy(o => o.Stage)
                .Select(g => new
                {
                    Stage = g.Key,
                    Count = g.Count(),
                    TotalAmount = g.Sum(x => x.Amount),
                    WeightedAmount = g.Sum(x => (x.Amount * x.Probability) / 100m)
                })
                .ToList();

            var totalPipeline = opportunities.Where(o => o.Status == "Open").Sum(o => o.Amount);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Pipeline report data retrieved successfully.",
                Data = new
                {
                    TotalOpenPipeline = totalPipeline,
                    Stages = stageData
                }
            });
        }
    }
}
