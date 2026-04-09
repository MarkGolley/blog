using MyBlog.Startup;

var builder = WebApplication.CreateBuilder(args);
var secureCookiePolicy = builder.Environment.IsDevelopment()
    ? CookieSecurePolicy.SameAsRequest
    : CookieSecurePolicy.Always;
var appVersion =
    Environment.GetEnvironmentVariable("APP_VERSION")
    ?? builder.Configuration["App:Version"]
    ?? "unknown";
if (!AppRequestPolicies.TryParseAppMode(
        Environment.GetEnvironmentVariable("APP_MODE") ?? builder.Configuration["App:Mode"],
        out var appMode))
{
    throw new InvalidOperationException("Invalid App:Mode value. Supported values are Combined, BlogOnly, AislePilotOnly.");
}

// Cloud Run Port Binding
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Services
builder.AddMyBlogApplicationServices(secureCookiePolicy);

var app = builder.Build();
app.ConfigureMyBlogPipeline(appMode, appVersion);
app.Run();

public partial class Program;
