using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace ProConnect.WebAPI.Services
{
    public class AiService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public AiService(IConfiguration configuration)
        {
            _apiKey = configuration["AiSettings:GeminiApiKey"];
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("x-goog-api-key", _apiKey);
        }

        public async Task<string> AnalyzeImageAsync(byte[] imageBytes, string mimeType = "image/jpeg")
        {
            var base64Image = Convert.ToBase64String(imageBytes);
            var prompt = @"
                You are a home service assistant. Analyze the image and return a JSON object with:
                - title: short title (max 60 chars)
                - description: detailed description of the issue (max 200 chars)
                - suggestedCategory: one of [Plumbing, Electrical, Painting, Transport, Carpentry, Cleaning, HVAC, Gardening]
                - isUrgent: boolean (true if it looks like an emergency)
                - estimatedBudgetMin: number (minimum reasonable cost)
                - estimatedBudgetMax: number (maximum reasonable cost)
                Only return valid JSON, no extra text.
            ";

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { text = prompt },
                            new { inline_data = new { mime_type = mimeType, data = base64Image } }
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent",
                content);

            var responseString = await response.Content.ReadAsStringAsync();
            return responseString;
        }
    }
}
