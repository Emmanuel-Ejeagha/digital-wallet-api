using System.Reflection;
using DigitalWallet.API.Controllers.v1;
using DigitalWallet.API.Middleware;
using DigitalWallet.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

var auth0Settings = builder.Configuration.GetSection("Auth0").Get<Auth0Settings>();
builder.Services.Configure<Auth0Settings>(builder.Configuration.GetSection("Auth0"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://{auth0Settings.Domain}/";
        options.Audience = auth0Settings.Audience;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("User", policy => policy.RequireAuthenticatedUser().RequireClaim("role", "User"));
    options.AddPolicy("Admin", policy => policy.RequireAuthenticatedUser().RequireClaim("role", "Admin"));
    options.AddPolicy("Compliance", policy => policy.RequireAuthenticatedUser().RequireClaim("role", "Compliance"));
});

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = new UrlSegmentApiVersionReader();
})
.AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Digital Wallet API", Version = "v1" });
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    c.IncludeXmlComments(xmlPath);

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter JWT with Bearer into field",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer"}
            },
            new string[] {}
        }
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Digital Wallet API v1");
});

app.UseHttpsRedirection();

app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
        var migrationsExist = appliedMigrations.Any() || pendingMigrations.Any();

        if (migrationsExist)
        {
            logger.LogInformation("Applying database migrations...");
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        else
        {
            var canConnect = await dbContext.Database.CanConnectAsync();
            if (!canConnect)
            {
                logger.LogInformation("Database does not exist. Creating with schema...");
                await dbContext.Database.EnsureCreatedAsync();
                logger.LogInformation("Database created with schema.");
            }
            else
            {
                var tableExists = false;
                try
                {
                    tableExists = await dbContext.Users.AnyAsync();
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
                {
                    tableExists = false;
                }

                if (!tableExists)
                {
                    logger.LogWarning("Database exists but is empty. Dropping and recreating with schema...");
                    await dbContext.Database.EnsureDeletedAsync();
                    await dbContext.Database.EnsureCreatedAsync();
                    logger.LogInformation("Database recreated with schema.");
                }
                else
                {
                    logger.LogInformation("Database already has schema (no migrations applied).");
                }
            }
        }

        logger.LogInformation("Starting database seeding...");
        await ApplicationDbContextSeed.SeedAsync(dbContext, logger);
        logger.LogInformation("Database seeding completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database setup failed. The application will continue, but database operations may not work.");;
    }
}

app.Run();