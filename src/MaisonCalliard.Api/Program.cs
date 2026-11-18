using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using MaisonCalliard.Api.Auth;
using MaisonCalliard.Application;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Infrastructure;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;

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

var authOptions = AuthOptions.FromConfiguration(builder.Configuration);
authOptions.EnsureSigningKey();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = AuthTokenValidation.CreateParameters(authOptions);
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var frontendOrigins = FrontendOriginPolicy.Create(builder.Configuration);
builder.Services.AddSingleton(frontendOrigins);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(frontendOrigins.CorsOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddScoped<AdminAccessMiddleware>();

var app = builder.Build();
await LoginVerificationStartup.EnsureAsync(app.Services, app.Environment, app.Logger);

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AdminAccessMiddleware>();
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

public partial class Program;
