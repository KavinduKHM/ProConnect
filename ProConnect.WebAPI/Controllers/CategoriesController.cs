using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProConnect.Infrastructure.Data;

namespace ProConnect.WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ServiceCategoriesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public ServiceCategoriesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.ServiceCategories
                .Where(c => c.IsActive)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            return Ok(categories);
        }
    }
}