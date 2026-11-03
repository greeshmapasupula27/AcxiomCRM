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
    [Route("api/opportunities")]
    [ApiController]
    [Authorize] // PDF Section 10.1 & 17.14
    public class OpportunitiesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public OpportunitiesApiController(
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

        // GET: api/opportunities
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<OpportunityDto>>>> GetOpportunities([FromQuery] string? stage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Opportunity> query = _context.Opportunities.Include(o => o.Customer).AsNoTracking();

            if (accessibleUserIds != null)
            {
                query = query.Where(o => o.AssignedTo != null && accessibleUserIds.Contains(o.AssignedTo));
            }

            if (!string.IsNullOrWhiteSpace(stage))
            {
                query = query.Where(o => o.Stage == stage);
            }

            var dtos = await query.Select(o => new OpportunityDto
            {
                OpportunityId = o.OpportunityId,
                OpportunityName = o.OpportunityName,
                CustomerId = o.CustomerId,
                CustomerName = o.Customer != null ? o.Customer.CustomerName : null,
                LeadId = o.LeadId,
                Amount = o.Amount,
                Stage = o.Stage,
                Probability = o.Probability,
                WeightedAmount = (o.Amount * o.Probability) / 100m,
                ExpectedCloseDate = o.ExpectedCloseDate,
                Status = o.Status,
                CreatedDate = o.CreatedDate,
                AssignedTo = o.AssignedTo,
                Notes = o.Notes
            }).ToListAsync();

            return Ok(new ApiResponse<IEnumerable<OpportunityDto>>
            {
                Success = true,
                Message = $"Retrieved {dtos.Count} opportunity records.",
                Data = dtos
            });
        }

        // GET: api/opportunities/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<OpportunityDto>>> GetOpportunity(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var opp = await _context.Opportunities.Include(o => o.Customer).FirstOrDefaultAsync(o => o.OpportunityId == id);
            if (opp == null)
            {
                return NotFound(new ApiResponse<OpportunityDto> { Success = false, Message = $"Opportunity with ID {id} not found." });
            }

            if (!await _userScopeService.CanAccessRecordAsync(user, opp.AssignedTo))
            {
                return Forbid();
            }

            var dto = new OpportunityDto
            {
                OpportunityId = opp.OpportunityId,
                OpportunityName = opp.OpportunityName,
                CustomerId = opp.CustomerId,
                CustomerName = opp.Customer?.CustomerName,
                LeadId = opp.LeadId,
                Amount = opp.Amount,
                Stage = opp.Stage,
                Probability = opp.Probability,
                WeightedAmount = (opp.Amount * opp.Probability) / 100m,
                ExpectedCloseDate = opp.ExpectedCloseDate,
                Status = opp.Status,
                CreatedDate = opp.CreatedDate,
                AssignedTo = opp.AssignedTo,
                Notes = opp.Notes
            };

            return Ok(new ApiResponse<OpportunityDto> { Success = true, Data = dto });
        }

        // POST: api/opportunities
        [HttpPost]
        public async Task<ActionResult<ApiResponse<OpportunityDto>>> CreateOpportunity([FromBody] OpportunityDto dto)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            // Business validation (Section 5.3 & 17.7)
            if (dto.Amount <= 0)
            {
                ModelState.AddModelError("Amount", "Opportunity Amount must be greater than 0.");
            }

            if (dto.Probability < 0 || dto.Probability > 100)
            {
                ModelState.AddModelError("Probability", "Probability must be between 0 and 100.");
            }

            if (dto.Stage != "Won" && dto.Stage != "Lost" && dto.ExpectedCloseDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("ExpectedCloseDate", "Expected Close Date cannot be in the past.");
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<OpportunityDto> { Success = false, Message = "Validation failed.", Errors = errors });
            }

            var opp = new Opportunity
            {
                OpportunityName = dto.OpportunityName,
                CustomerId = dto.CustomerId,
                LeadId = dto.LeadId,
                Amount = dto.Amount,
                Stage = string.IsNullOrEmpty(dto.Stage) ? "Qualification" : dto.Stage,
                Probability = dto.Probability,
                ExpectedCloseDate = dto.ExpectedCloseDate,
                Status = dto.Stage == "Won" ? "Won" : (dto.Stage == "Lost" ? "Lost" : "Open"),
                AssignedTo = string.IsNullOrEmpty(dto.AssignedTo) ? user.Id : dto.AssignedTo,
                Notes = dto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            _context.Opportunities.Add(opp);
            await _context.SaveChangesAsync();

            dto.OpportunityId = opp.OpportunityId;
            dto.CreatedDate = opp.CreatedDate;
            dto.AssignedTo = opp.AssignedTo;
            dto.WeightedAmount = (opp.Amount * opp.Probability) / 100m;

            await _auditService.LogAsync(user.Id, "Create", "Opportunity", opp.OpportunityId.ToString(), null, dto.OpportunityName, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(201, new ApiResponse<OpportunityDto>
            {
                Success = true,
                Message = "Opportunity created successfully.",
                Data = dto
            });
        }
    }
}
