using AcxiomCRM.Data;
using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers.Api
{
    [Route("api/followups")]
    [ApiController]
    public class FollowUpsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public FollowUpsApiController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        // GET: api/followups
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<FollowUpDto>>>> GetFollowUps([FromQuery] string? status)
        {
            var query = _context.FollowUps.AsNoTracking();

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
            if (dto.Status == "Planned" && dto.FollowUpDate.Date < DateTime.UtcNow.Date)
            {
                ModelState.AddModelError("FollowUpDate", "Follow-up date cannot be earlier than today.");
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<FollowUpDto> { Success = false, Message = "Validation failed.", Errors = errors });
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
                AssignedTo = dto.AssignedTo,
                CreatedDate = DateTime.UtcNow
            };

            _context.FollowUps.Add(fu);
            await _context.SaveChangesAsync();

            dto.FollowUpId = fu.FollowUpId;
            dto.CreatedDate = fu.CreatedDate;

            await _auditService.LogAsync(null, "Create", "FollowUp", fu.FollowUpId.ToString(), null, dto.Subject, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

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
    public class ReportsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ReportsApiController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/reports/pipeline (Section 10 & 17.14)
        [HttpGet("pipeline")]
        public async Task<ActionResult<ApiResponse<object>>> GetPipelineReport()
        {
            var opportunities = await _context.Opportunities.AsNoTracking().ToListAsync();

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
