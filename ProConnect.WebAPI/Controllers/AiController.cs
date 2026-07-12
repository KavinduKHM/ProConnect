using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using ProConnect.WebAPI.Services;

namespace ProConnect.WebAPI.Controllers
{
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiService _aiService;

    public AiController(AiService aiService)
    {
        _aiService = aiService;
    }

    [HttpPost("analyze-image")]
    public async Task<IActionResult> AnalyzeImage(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream);
        var imageBytes = memoryStream.ToArray();

        var result = await _aiService.AnalyzeImageAsync(imageBytes, file.ContentType);

        // Parse the AI response (extract the JSON)
        var json = ExtractJsonFromResponse(result);
        return Ok(json);
    }

    private string ExtractJsonFromResponse(string geminiResponse)
    {
        // Gemini returns a nested JSON; extract the text field
        using var doc = JsonDocument.Parse(geminiResponse);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        return text ?? "{}";
    }
}
}