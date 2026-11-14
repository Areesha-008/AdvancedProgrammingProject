using MarksApi.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using MarksApi.Dtos;
using MarksApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerUI;
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
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("AppCors", policy => policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Marks API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    options.AddSecurityDefinition("Bearer", scheme);
    options.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = [] });
});
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
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddScoped<IEnrollmentService, EnrollmentService>();


var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseCors("AppCors");
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
    var forbidden = OwnStudentOnly(user, id, "You can only view your own student record.");
    if (forbidden is not null)
    {
        return forbidden;
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

app.MapPost("/api/students", async (StudentWriteRequest request, IStudentService students, CancellationToken cancellationToken) =>
{
    var invalid = InvalidStudent(request);
    if (invalid is not null)
    {
        return invalid;
    }

    try
    {
        var created = await students.CreateAsync(request, cancellationToken);
        return Results.Created($"/api/students/{created.Id}", created);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapPut("/api/students/{id:int}", async (int id, StudentWriteRequest request, IStudentService students, CancellationToken cancellationToken) =>
{
    var invalid = InvalidStudent(request);
    if (invalid is not null)
    {
        return invalid;
    }

    try
    {
        var updated = await students.UpdateAsync(id, request, cancellationToken);
        return updated is null
            ? Results.Problem(title: "Student not found", detail: "No student with this id exists.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(updated);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapDelete("/api/students/{id:int}", async (int id, IStudentService students, CancellationToken cancellationToken) =>
{
    try
    {
        var deleted = await students.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.Problem(title: "Student not found", detail: "No student with this id exists.", statusCode: StatusCodes.Status404NotFound);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapGet("/api/courses", async (int? page, int? pageSize, string? search, string? sort, ICourseService courses, CancellationToken cancellationToken) =>
{
    return Results.Ok(await courses.ListAsync(page ?? 1, pageSize ?? 10, search, sort, cancellationToken));
}).RequireAuthorization();

app.MapGet("/api/courses/{id:int}", async (int id, ICourseService courses, CancellationToken cancellationToken) =>
{
    var course = await courses.GetAsync(id, cancellationToken);
    return course is null
        ? Results.Problem(title: "Course not found", detail: "No course with this id exists.", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(course);
}).RequireAuthorization();

app.MapPost("/api/courses", async (CourseWriteRequest request, ICourseService courses, CancellationToken cancellationToken) =>
{
    var invalid = InvalidCourse(request);
    if (invalid is not null)
    {
        return invalid;
    }

    try
    {
        var created = await courses.CreateAsync(request, cancellationToken);
        return Results.Created($"/api/courses/{created.Id}", created);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapPut("/api/courses/{id:int}", async (int id, CourseWriteRequest request, ICourseService courses, CancellationToken cancellationToken) =>
{
    var invalid = InvalidCourse(request);
    if (invalid is not null)
    {
        return invalid;
    }

    try
    {
        var updated = await courses.UpdateAsync(id, request, cancellationToken);
        return updated is null
            ? Results.Problem(title: "Course not found", detail: "No course with this id exists.", statusCode: StatusCodes.Status404NotFound)
            : Results.Ok(updated);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapDelete("/api/courses/{id:int}", async (int id, ICourseService courses, CancellationToken cancellationToken) =>
{
    try
    {
        var deleted = await courses.DeleteAsync(id, cancellationToken);
        return deleted
            ? Results.NoContent()
            : Results.Problem(title: "Course not found", detail: "No course with this id exists.", statusCode: StatusCodes.Status404NotFound);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapPost("/api/courses/{courseId:int}/enrollments", async (int courseId, EnrollRequest request, IEnrollmentService enrollments, CancellationToken cancellationToken) =>
{
    if (request.StudentId is null or < 1)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["studentId"] = ["Student id is required."] });
    }

    try
    {
        var created = await enrollments.EnrollAsync(courseId, request.StudentId.Value, cancellationToken);
        return Results.Created($"/api/enrollments/{created.Id}", created);
    }
    catch (ApiNotFoundException exception)
    {
        return Results.Problem(title: "Not found", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (ApiConflictException exception)
    {
        return Conflict(exception);
    }
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapGet("/api/courses/{courseId:int}/enrollments", async (int courseId, int? page, int? pageSize, string? search, string? sort, IEnrollmentService enrollments, CancellationToken cancellationToken) =>
{
    var result = await enrollments.ListAsync(courseId, page ?? 1, pageSize ?? 10, search, sort, cancellationToken);
    return result is null
        ? Results.Problem(title: "Course not found", detail: "No course with this id exists.", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(result);
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapPut("/api/enrollments/{id:int}/mark", async (int id, MarkRequest request, IEnrollmentService enrollments, CancellationToken cancellationToken) =>
{
    if (request.Mark is null or < 0 or > 100)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["mark"] = ["Mark must be a whole number from 0 to 100."] });
    }

    var updated = await enrollments.UpdateMarkAsync(id, request.Mark.Value, cancellationToken);
    return updated is null
        ? Results.Problem(title: "Enrollment not found", detail: "No enrollment with this id exists.", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(updated);
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapDelete("/api/enrollments/{id:int}", async (int id, IEnrollmentService enrollments, CancellationToken cancellationToken) =>
{
    var deleted = await enrollments.DeleteAsync(id, cancellationToken);
    return deleted
        ? Results.NoContent()
        : Results.Problem(title: "Enrollment not found", detail: "No enrollment with this id exists.", statusCode: StatusCodes.Status404NotFound);
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapGet("/api/courses/{id:int}/stats", async (int id, ICourseService courses, CancellationToken cancellationToken) =>
{
    var stats = await courses.GetStatsAsync(id, cancellationToken);
    return stats is null
        ? Results.Problem(title: "Course not found", detail: "No course with this id exists.", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(stats);
}).RequireAuthorization(policy => policy.RequireRole("Teacher"));

app.MapGet("/api/students/{id:int}/transcript", async (int id, ClaimsPrincipal user, IStudentService students, CancellationToken cancellationToken) =>
{
    var forbidden = OwnStudentOnly(user, id, "You can only view your own transcript.");
    if (forbidden is not null)
    {
        return forbidden;
    }

    var transcript = await students.GetTranscriptAsync(id, cancellationToken);
    return transcript is null
        ? Results.Problem(title: "Student not found", detail: "No student with this id exists.", statusCode: StatusCodes.Status404NotFound)
        : Results.Ok(transcript);
}).RequireAuthorization();

app.MapGet("/", () => "Hello World!");

static IResult? InvalidStudent(StudentWriteRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.RegistrationNo) || request.RegistrationNo.Trim().Length > 20)
    {
        errors["registrationNo"] = ["Registration number is required and must be at most 20 characters."];
    }
    if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100)
    {
        errors["fullName"] = ["Full name is required and must be at most 100 characters."];
    }
    if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Trim().Length > 150 || !request.Email.Contains('@'))
    {
        errors["email"] = ["A valid email is required and must be at most 150 characters."];
    }

    return errors.Count == 0 ? null : Results.ValidationProblem(errors);
}

static IResult? OwnStudentOnly(ClaimsPrincipal user, int id, string detail)
{
    if (!user.IsInRole("Student"))
    {
        return null;
    }

    return user.FindFirst("studentId")?.Value == id.ToString()
        ? null
        : Results.Problem(title: "Forbidden", detail: detail, statusCode: StatusCodes.Status403Forbidden);
}

static IResult? InvalidCourse(CourseWriteRequest request)
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 10)
    {
        errors["code"] = ["Course code is required and must be at most 10 characters."];
    }
    if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 150)
    {
        errors["title"] = ["Title is required and must be at most 150 characters."];
    }
    if (request.CreditHours is null or < 1 or > 4)
    {
        errors["creditHours"] = ["Credit hours must be a whole number from 1 to 4."];
    }

    return errors.Count == 0 ? null : Results.ValidationProblem(errors);
}

static IResult Conflict(ApiConflictException exception)
{
    return Results.Problem(title: exception.Title, detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
}

app.Run();
