using MyBlog.Data;
using MyBlog.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------
// Cloud Run Port Binding
// ----------------------------
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// ----------------------------
// Services
// ----------------------------
builder.Services.AddControllersWithViews();

builder.Services.AddSingleton<BlogService>();
builder.Services.AddScoped<CommentService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDbContext<BlogDbContext>(options =>
        options.UseInMemoryDatabase("BlogDevDb"));
}
else
{
    var connectionString = Environment.GetEnvironmentVariable("DefaultConnection");
    builder.Services.AddDbContext<BlogDbContext>(options =>
        options.UseNpgsql(connectionString));
}

builder.Services.AddRouting(options => options.LowercaseUrls = true);

var app = builder.Build();

try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BlogDbContext>();
    db.Database.Migrate();
}
catch (Exception ex)
{
    Console.WriteLine($"Warning: DB not ready yet: {ex.Message}");
}


// ----------------------------
// Middleware
// ----------------------------
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// ----------------------------
// Routes
// ----------------------------
app.MapControllerRoute(
    name: "blogPost",
    pattern: "blog/{slug?}",
    defaults: new { controller = "Blog", action = "Post" });

app.MapControllerRoute(
    name: "blogIndex",
    pattern: "blog",
    defaults: new { controller = "Blog", action = "Index" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.UseDeveloperExceptionPage();

app.Run();