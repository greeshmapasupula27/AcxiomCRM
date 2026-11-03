using AcxiomCRM.Data;
using AcxiomCRM.DTOs;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers.Api
{
    [Route("api/leads")]
    [ApiController]
    public class LeadsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public LeadsApiController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        // GET: api/leads
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<LeadDto>>>> GetLeads([FromQuery] string? status)
        {
            var query = _context.Leads.AsNoTracking();

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

        // POST: api/leads
        [HttpPost]
        public async Task<ActionResult<ApiResponse<LeadDto>>> CreateLead([FromBody] LeadDto dto)
        {
            if (dto.ExpectedValue <= 0)
            {
                ModelState.AddModelError("ExpectedValue", "Expected Value must be greater than 0.");
            }

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<LeadDto> { Success = false, Message = "Validation failed.", Errors = errors });
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
                Notes = dto.Notes,
                CreatedDate = DateTime.UtcNow
            };

            _context.Leads.Add(lead);
            await _context.SaveChangesAsync();

            dto.LeadId = lead.LeadId;
            dto.LeadCode = lead.LeadCode;
            dto.CreatedDate = lead.CreatedDate;

            await _auditService.LogAsync(null, "Create", "Lead", lead.LeadId.ToString(), null, dto.LeadName, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return StatusCode(201, new ApiResponse<LeadDto>
            {
                Success = true,
                Message = "Lead created successfully.",
                Data = dto
            });
        }
    }
}
