using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaisonCalliard.Application;
using MaisonCalliard.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

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

app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

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
