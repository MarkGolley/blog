using Google.Cloud.Firestore;
using MyBlog.Services;

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

var firestoreProjectId =
    Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT") ??
    builder.Configuration["Firestore:ProjectId"];
var firestoreDatabaseId =
    Environment.GetEnvironmentVariable("FIRESTORE_DATABASE_ID") ??
    builder.Configuration["Firestore:DatabaseId"] ??
    "(default)";

if (string.IsNullOrWhiteSpace(firestoreProjectId))
{
    throw new InvalidOperationException(
        "Firestore project id is missing. Set GOOGLE_CLOUD_PROJECT or Firestore:ProjectId.");
}

builder.Services.AddSingleton(_ => new FirestoreDbBuilder
{
    ProjectId = firestoreProjectId,
    DatabaseId = firestoreDatabaseId
}.Build());
builder.Services.AddSingleton<BlogService>();
builder.Services.AddScoped<CommentService>();
builder.Services.AddScoped<LikeService>();

builder.Services.AddRouting(options => options.LowercaseUrls = true);

var app = builder.Build();

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
