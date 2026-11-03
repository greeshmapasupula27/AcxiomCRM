using AcxiomCRM.Data;
using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers.Api
{
    [Route("api/opportunities")]
    [ApiController]
    public class OpportunitiesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public OpportunitiesApiController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        // GET: api/opportunities
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<OpportunityDto>>>> GetOpportunities([FromQuery] string? stage)
        {
            var query = _context.Opportunities.Include(o => o.Customer).AsNoTracking();

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

        // POST: api/opportunities
        [HttpPost]
        public async Task<ActionResult<ApiResponse<OpportunityDto>>> CreateOpportunity([FromBody] OpportunityDto dto)
        {
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
                Notes = dto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            _context.Opportunities.Add(opp);
            await _context.SaveChangesAsync();

            dto.OpportunityId = opp.OpportunityId;
            dto.CreatedDate = opp.CreatedDate;
            dto.WeightedAmount = (opp.Amount * opp.Probability) / 100m;

            await _auditService.LogAsync(null, "Create", "Opportunity", opp.OpportunityId.ToString(), null, dto.OpportunityName, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(201, new ApiResponse<OpportunityDto>
            {
                Success = true,
                Message = "Opportunity created successfully.",
                Data = dto
            });
        }
    }
}
