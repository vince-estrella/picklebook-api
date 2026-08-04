using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PickleballApi.Controllers
{
    // C# port of the Node/Express route your friend wrote (routes/players.js,
    // POST /players/extract-names). Same contract: multipart/form-data with a
    // field named "image" in, { names: [...] } out — matches exactly what
    // QueueManager.jsx already calls.
    //
    // Env var required (same as the Node version): ANTHROPIC_API_KEY

    [ApiController]
    [Route("api/players")]
    public class PlayersController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;

        private static readonly HashSet<string> AllowedMimeTypes = new()
        {
            "image/jpeg", "image/png", "image/webp", "image/gif"
        };

        private const long MaxFileSizeBytes = 8 * 1024 * 1024; // 8MB, matches the Node multer limit

        private const string ExtractionPrompt =
            "This is a screenshot of a participant list from an event or booking app. " +
            "Identify every distinct person's first name (or full name if shown) that " +
            "represents an actual attendee/participant. Ignore: ads, banners, app UI " +
            "labels (e.g. \"Friend\", \"Confirmed\", sort/filter controls), stats or ratings " +
            "shown next to names, and any \"+1\" or \"+N\" badges (do not invent extra names " +
            "for those, just skip them). Respond with ONLY a JSON array of strings, no " +
            "other text, no markdown fences. Example: [\"Kevin\",\"Kai\",\"Khyle\",\"Ellen\"]";

        public PlayersController(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        // POST: api/players/extract-names
        // Queue Manager (QueueManager.jsx) is a public page with no login
        // wall of its own, so this can't require CourtOwner auth — it never
        // sees an owner token in normal use, which was causing every real
        // call to 403. Rate-limited instead (see Program.cs, "extract-names"
        // policy): unauthenticated + costs real money per call via
        // Anthropic's API, so the rate limit is what actually protects this
        // now, not a login wall.
        [EnableRateLimiting("extract-names")]
        [HttpPost("extract-names")]
        [RequestSizeLimit(MaxFileSizeBytes)]
        public async Task<ActionResult> ExtractNames(IFormFile? image)
        {
            if (image == null || image.Length == 0)
            {
                return BadRequest(new { error = "No image uploaded.", names = Array.Empty<string>() });
            }

            if (!AllowedMimeTypes.Contains(image.ContentType))
            {
                return BadRequest(new { error = "Unsupported image type.", names = Array.Empty<string>() });
            }

            if (image.Length > MaxFileSizeBytes)
            {
                return BadRequest(new { error = "Image too large (8MB max).", names = Array.Empty<string>() });
            }

            var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("ANTHROPIC_API_KEY is not set.");
                return StatusCode(500, new { error = "Server is not configured for image extraction.", names = Array.Empty<string>() });
            }

            string base64Image;
            using (var ms = new MemoryStream())
            {
                await image.CopyToAsync(ms);
                base64Image = Convert.ToBase64String(ms.ToArray());
            }

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var payload = new
            {
                model = "claude-sonnet-4-6",
                max_tokens = 1024,
                messages = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new
                            {
                                type = "image",
                                source = new
                                {
                                    type = "base64",
                                    media_type = image.ContentType,
                                    data = base64Image
                                }
                            },
                            new
                            {
                                type = "text",
                                text = ExtractionPrompt
                            }
                        }
                    }
                }
            };

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsJsonAsync("https://api.anthropic.com/v1/messages", payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"extract-names request failed: {ex.Message}");
                return StatusCode(502, new { error = "Failed to reach the image processing service.", names = Array.Empty<string>() });
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Anthropic API error ({(int)response.StatusCode}): {json}");
                return StatusCode(502, new { error = "Failed to process image.", names = Array.Empty<string>() });
            }

            string? rawText = null;
            if (json.TryGetProperty("content", out var contentBlocks))
            {
                foreach (var block in contentBlocks.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "text")
                    {
                        rawText = block.GetProperty("text").GetString();
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawText))
            {
                return StatusCode(502, new { error = "Could not parse names from image.", names = Array.Empty<string>() });
            }

            // Strip stray ```json fences in case the model adds them anyway.
            var cleaned = Regex.Replace(rawText.Trim(), @"^```json\s*|^```\s*|```$", "").Trim();

            List<string>? names;
            try
            {
                names = JsonSerializer.Deserialize<List<string>>(cleaned);
            }
            catch
            {
                return StatusCode(502, new { error = "Could not parse names from image.", names = Array.Empty<string>() });
            }

            if (names == null)
            {
                return StatusCode(502, new { error = "Unexpected response shape.", names = Array.Empty<string>() });
            }

            // Clean up: dedupe, trim, drop empties, cap absurd lengths — same rules as the Node version.
            var seen = new HashSet<string>();
            var cleanNames = new List<string>();
            foreach (var n in names)
            {
                var trimmed = (n ?? "").Trim();
                if (trimmed.Length == 0 || trimmed.Length >= 60) continue;
                var key = trimmed.ToLowerInvariant();
                if (seen.Contains(key)) continue;
                seen.Add(key);
                cleanNames.Add(trimmed);
            }

            return Ok(new { names = cleanNames });
        }
    }
}