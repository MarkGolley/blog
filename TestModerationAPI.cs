using System.Net.Http.Headers;
using System.Text.Json;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("ERROR: OPENAI_API_KEY not set");
    return;
}

using var client = new HttpClient();

var testCases = new[]
{
    "This is a great article, thanks for sharing!",
    "hate violence illegal drugs",
    "I love this post",
    "Kill all humans"
};

foreach (var content in testCases)
{
    var requestBody = new { input = content };
    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/moderations")
    {
        Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json")
    };
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    try
    {
        var response = await client.SendAsync(request);
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"\nContent: {content}");
        Console.WriteLine($"Response: {responseContent}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error testing '{content}': {ex.Message}");
    }
}
