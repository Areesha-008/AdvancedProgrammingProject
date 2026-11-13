using MarksApi.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using MarksApi.Dtos;
using MarksApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Database");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Connection string 'Database' is not configured.");
}
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Jwt:Key is not configured.");
}



builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IStudentService, StudentService>();


var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbSeeder.SeedAsync(db, app.Configuration, CancellationToken.None);
}


app.MapHealthChecks("/health");
app.MapPost("/api/auth/login", async (LoginRequest request, IAuthService auth, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors["username"] = ["Username is required."];
        }
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors["password"] = ["Password is required."];
        }
        return Results.ValidationProblem(errors);
    }

    var response = await auth.LoginAsync(request.Username, request.Password, cancellationToken);
    if (response is null)
    {
        return Results.Problem(
            title: "Unauthorized",
            detail: "The username or password is wrong.",
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(response);
});

app.MapGet("/api/students", async (int? page, int? pageSize, string? search, string? sort, IStudentService students, CancellationToken cancellationToken) =>
{
    var result = await students.ListAsync(page ?? 1, pageSize ?? 10, search, sort, cancellationToken);
    return Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapGet("/api/students/{id:int}", async (int id, ClaimsPrincipal user, IStudentService students, CancellationToken cancellationToken) =>
{
    if (user.IsInRole("Student"))
    {
        var ownId = user.FindFirst("studentId")?.Value;
        if (ownId != id.ToString())
        {
            return Results.Problem(
                title: "Forbidden",
                detail: "You can only view your own student record.",
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var student = await students.GetAsync(id, cancellationToken);
    if (student is null)
    {
        return Results.Problem(
            title: "Student not found",
            detail: "No student with this id exists.",
            statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Ok(student);
}).RequireAuthorization();

app.MapGet("/", () => "Hello World!");

app.Run();
