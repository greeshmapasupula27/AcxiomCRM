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
    [Route("api/customers")]
    [ApiController]
    [Authorize] // PDF Section 10.1 & 17.14: Protected endpoint requires authentication
    public class CustomersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUserScopeService _userScopeService;
        private readonly IAuditService _auditService;

        public CustomersApiController(
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

        // GET: api/customers
        [HttpGet]
        public async Task<ActionResult<ApiResponse<IEnumerable<CustomerDto>>>> GetCustomers([FromQuery] string? search)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var accessibleUserIds = await _userScopeService.GetAccessibleUserIdsAsync(user);
            IQueryable<Customer> query = _context.Customers.AsNoTracking();

            // Role and Team scoping (PDF Section 7 & 17.10)
            if (accessibleUserIds != null)
            {
                query = query.Where(c => c.CreatedBy != null && accessibleUserIds.Contains(c.CreatedBy));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(c => c.CustomerName.Contains(search) ||
                                         c.Email.Contains(search) ||
                                         c.Phone.Contains(search) ||
                                         (c.CompanyName != null && c.CompanyName.Contains(search)));
            }

            var dtos = await query.Select(c => new CustomerDto
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Email = c.Email,
                Phone = c.Phone,
                CompanyName = c.CompanyName,
                Address = c.Address,
                City = c.City,
                State = c.State,
                Status = c.Status,
                CreatedDate = c.CreatedDate,
                CreatedBy = c.CreatedBy,
                Notes = c.Notes
            }).ToListAsync();

            return Ok(new ApiResponse<IEnumerable<CustomerDto>>
            {
                Success = true,
                Message = $"Retrieved {dtos.Count} customer(s).",
                Data = dtos
            });
        }

        // GET: api/customers/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ApiResponse<CustomerDto>>> GetCustomer(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var c = await _context.Customers.FindAsync(id);
            if (c == null)
            {
                return NotFound(new ApiResponse<CustomerDto> { Success = false, Message = $"Customer with ID {id} not found." });
            }

            if (!await _userScopeService.CanAccessRecordAsync(user, c.CreatedBy))
            {
                return Forbid();
            }

            var dto = new CustomerDto
            {
                CustomerId = c.CustomerId,
                CustomerCode = c.CustomerCode,
                CustomerName = c.CustomerName,
                Email = c.Email,
                Phone = c.Phone,
                CompanyName = c.CompanyName,
                Address = c.Address,
                City = c.City,
                State = c.State,
                Status = c.Status,
                CreatedDate = c.CreatedDate,
                CreatedBy = c.CreatedBy,
                Notes = c.Notes
            };

            return Ok(new ApiResponse<CustomerDto> { Success = true, Data = dto });
        }

        // POST: api/customers
        [HttpPost]
        public async Task<ActionResult<ApiResponse<CustomerDto>>> CreateCustomer([FromBody] CustomerDto dto)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<CustomerDto> { Success = false, Message = "Validation failed.", Errors = errors });
            }

            // Check email uniqueness
            if (await _context.Customers.AnyAsync(c => c.Email.ToLower() == dto.Email.ToLower()))
            {
                return Conflict(new ApiResponse<CustomerDto> { Success = false, Message = "Email is already registered to another customer." });
            }

            // Check phone uniqueness
            if (await _context.Customers.AnyAsync(c => c.Phone == dto.Phone))
            {
                return Conflict(new ApiResponse<CustomerDto> { Success = false, Message = "Phone number is already registered to another customer." });
            }

            var customer = new Customer
            {
                CustomerCode = string.IsNullOrEmpty(dto.CustomerCode) ? "CUST-" + new Random().Next(1000, 9999) : dto.CustomerCode,
                CustomerName = dto.CustomerName,
                Email = dto.Email,
                Phone = dto.Phone,
                CompanyName = dto.CompanyName,
                Address = dto.Address,
                City = dto.City,
                State = dto.State,
                Status = string.IsNullOrEmpty(dto.Status) ? "Active" : dto.Status,
                Notes = dto.Notes,
                CreatedBy = user.Id, // Assign authenticated user
                CreatedDate = DateTime.UtcNow
            };

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            dto.CustomerId = customer.CustomerId;
            dto.CustomerCode = customer.CustomerCode;
            dto.CreatedDate = customer.CreatedDate;
            dto.CreatedBy = customer.CreatedBy;

            await _auditService.LogAsync(user.Id, "Create", "Customer", customer.CustomerId.ToString(), null, dto.CustomerName, "Success", "Created via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return CreatedAtAction(nameof(GetCustomer), new { id = customer.CustomerId }, new ApiResponse<CustomerDto>
            {
                Success = true,
                Message = "Customer created successfully.",
                Data = dto
            });
        }

        // PUT: api/customers/5
        [HttpPut("{id}")]
        public async Task<ActionResult<ApiResponse<CustomerDto>>> UpdateCustomer(int id, [FromBody] CustomerDto dto)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
                return BadRequest(new ApiResponse<CustomerDto> { Success = false, Message = "Validation failed.", Errors = errors });
            }

            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                return NotFound(new ApiResponse<CustomerDto> { Success = false, Message = $"Customer with ID {id} not found." });
            }

            if (!await _userScopeService.CanAccessRecordAsync(user, customer.CreatedBy))
            {
                return Forbid();
            }

            // Check email & phone uniqueness excluding current
            if (await _context.Customers.AnyAsync(c => c.Email.ToLower() == dto.Email.ToLower() && c.CustomerId != id))
            {
                return Conflict(new ApiResponse<CustomerDto> { Success = false, Message = "Email already in use by another customer." });
            }

            if (await _context.Customers.AnyAsync(c => c.Phone == dto.Phone && c.CustomerId != id))
            {
                return Conflict(new ApiResponse<CustomerDto> { Success = false, Message = "Phone already in use by another customer." });
            }

            customer.CustomerName = dto.CustomerName;
            customer.Email = dto.Email;
            customer.Phone = dto.Phone;
            customer.CompanyName = dto.CompanyName;
            customer.Address = dto.Address;
            customer.City = dto.City;
            customer.State = dto.State;
            customer.Status = dto.Status;
            customer.Notes = dto.Notes;
            customer.ModifiedDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(user.Id, "Update", "Customer", id.ToString(), null, dto.CustomerName, "Success", "Updated via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            dto.CustomerId = id;
            return Ok(new ApiResponse<CustomerDto> { Success = true, Message = "Customer updated successfully.", Data = dto });
        }

        // DELETE: api/customers/5
        [HttpDelete("{id}")]
        public async Task<ActionResult<ApiResponse<bool>>> DeleteCustomer(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var customer = await _context.Customers.FindAsync(id);
            if (customer == null)
            {
                return NotFound(new ApiResponse<bool> { Success = false, Message = $"Customer with ID {id} not found." });
            }

            if (!await _userScopeService.CanAccessRecordAsync(user, customer.CreatedBy))
            {
                return Forbid();
            }

            _context.Customers.Remove(customer);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(user.Id, "Delete", "Customer", id.ToString(), customer.CustomerName, null, "Success", "Deleted via REST API", HttpContext.Connection.RemoteIpAddress?.ToString());

            return Ok(new ApiResponse<bool> { Success = true, Message = "Customer deleted successfully.", Data = true });
        }
    }
}
