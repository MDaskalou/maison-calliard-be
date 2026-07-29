using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaisonCalliard.Application;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Infrastructure;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.IdentityModel.Tokens;

var optimizeExistingImages = args.Contains("--optimize-existing-images", StringComparer.OrdinalIgnoreCase);
var builderArgs = args.Where(arg => !string.Equals(arg, "--optimize-existing-images", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(builderArgs);

const long DefaultMaxImageUploadBytes = 10 * 1024 * 1024;
const int MaxInMemoryUploadBufferBytes = 8 * 1024 * 1024;

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

builder.Services.Configure<FormOptions>(options =>
{
    var maxImageUploadBytes = builder.Configuration.GetValue<long?>("Uploads:MaxImageBytes") ?? DefaultMaxImageUploadBytes;

    options.MultipartBodyLengthLimit = maxImageUploadBytes;
    options.MemoryBufferThreshold = (int)Math.Min(maxImageUploadBytes, MaxInMemoryUploadBufferBytes);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var logtoEndpoint = builder.Configuration["Logto:Endpoint"]?.TrimEnd('/');
var logtoAudience = builder.Configuration["Logto:Audience"];

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);

if (!string.IsNullOrWhiteSpace(logtoEndpoint))
{
    var roleClaimType = builder.Configuration["Logto:RoleClaimType"] ?? "roles";

    authBuilder.AddJwtBearer(options =>
    {
        options.Authority = $"{logtoEndpoint}/oidc";
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = !string.IsNullOrWhiteSpace(logtoAudience),
            ValidAudience = logtoAudience,
            ValidateLifetime = true,
            RoleClaimType = roleClaimType,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is not ClaimsIdentity identity)
                {
                    return Task.CompletedTask;
                }

                if (identity.HasClaim(c => c.Type == roleClaimType))
                {
                    return Task.CompletedTask;
                }

                var scope = context.Principal.FindFirst("scope")?.Value;
                if (string.IsNullOrWhiteSpace(scope))
                {
                    return Task.CompletedTask;
                }

                foreach (var role in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    identity.AddClaim(new Claim(roleClaimType, role));
                }

                return Task.CompletedTask;
            },
        };
    });
}
else
{
    authBuilder.AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MaisonCalliard";
        var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MaisonCalliard";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
}

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = GetAllowedOrigins(builder.Configuration);
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (ImageValidationException exception)
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Ogiltig bild",
            detail: exception.Message).ExecuteAsync(context);
    }
    catch (InvalidOperationException exception) when (exception.Message.StartsWith("Supabase Storage", StringComparison.Ordinal))
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bilduppladdning misslyckades",
            detail: exception.Message).ExecuteAsync(context);
    }
});

var uploadContentTypes = new FileExtensionContentTypeProvider();
uploadContentTypes.Mappings[".webp"] = "image/webp";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = uploadContentTypes,
    OnPrepareResponse = context =>
    {
        if (context.Context.Request.Path.StartsWithSegments("/uploads"))
        {
            context.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    }
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (optimizeExistingImages)
{
    await using var scope = app.Services.CreateAsyncScope();
    var optimizer = scope.ServiceProvider.GetRequiredService<IExistingImageOptimizationService>();
    var result = await optimizer.OptimizeAsync();
    app.Logger.LogInformation(
        "Existing image optimization completed: {OptimizedFiles} files, {UpdatedRecords} database records, {SkippedFiles} skipped, {MissingFiles} missing",
        result.OptimizedFiles, result.UpdatedRecords, result.SkippedFiles, result.MissingFiles);
    return;
}

app.Run();

static string[] GetAllowedOrigins(IConfiguration configuration)
{
    var origins = new List<string>();

    origins.AddRange(configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? []);
    origins.AddRange(SplitOrigins(configuration["Cors:AllowedOrigins"]));
    origins.AddRange(SplitOrigins(configuration["Cors:AllowedOriginsCsv"]));
    origins.AddRange(SplitOrigins(configuration["Frontend:Url"]));
    origins.AddRange(SplitOrigins(configuration["FrontendUrl"]));

    return origins
        .Select(origin => origin.Trim().TrimEnd('/'))
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static IEnumerable<string> SplitOrigins(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
