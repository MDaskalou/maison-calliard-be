using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.OrderRequests;
using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Repositories;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stripe;

namespace MaisonCalliard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddScoped<INewsRepository, NewsRepository>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();

        var uploadsPath = Path.Combine(environment.ContentRootPath, "wwwroot", "uploads");
        var baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5000";
        var imageOptions = new ImageUploadOptions();
        configuration.GetSection(ImageUploadOptions.SectionName).Bind(imageOptions);
        services.AddSingleton(imageOptions);
        services.AddSingleton<ImageProcessor>();

        var supabaseOptions = new SupabaseStorageOptions();
        configuration.GetSection(SupabaseStorageOptions.SectionName).Bind(supabaseOptions);
        services.Configure<SupabaseStorageOptions>(configuration.GetSection(SupabaseStorageOptions.SectionName));

        if (supabaseOptions.IsConfigured)
        {
            var serviceRoleKey = supabaseOptions.ServiceRoleKey.Trim();

            services.AddHttpClient("SupabaseStorage", client =>
            {
                // Supabase requires both headers; legacy service_role is a JWT that works as Bearer.
                client.DefaultRequestHeaders.Add("apikey", serviceRoleKey);
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", serviceRoleKey);
            });

            services.AddSingleton<IFileStorageService>(provider =>
                new SupabaseStorageService(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("SupabaseStorage"),
                    provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SupabaseStorageOptions>>(),
                    provider.GetRequiredService<ImageProcessor>(),
                    provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SupabaseStorageService>>()));
        }
        else
        {
            services.AddSingleton<IFileStorageService>(provider =>
                new LocalFileStorageService(uploadsPath, baseUrl, provider.GetRequiredService<ImageProcessor>()));
        }

        services.AddScoped<IExistingImageOptimizationService>(provider =>
            new ExistingImageOptimizationService(
                provider.GetRequiredService<AppDbContext>(),
                provider.GetRequiredService<IFileStorageService>(),
                imageOptions,
                uploadsPath,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ExistingImageOptimizationService>>()));

        var stripeSecretKey = configuration["Stripe:SecretKey"] ?? string.Empty;
        StripeConfiguration.ApiKey = stripeSecretKey;

        services.Configure<ResendOptions>(configuration.GetSection(ResendOptions.SectionName));
        services.Configure<ReceiptOptions>(options =>
        {
            configuration.GetSection(ReceiptOptions.SectionName).Bind(options);

            var orderNotificationEmail = configuration["ORDER_NOTIFICATION_EMAIL"];
            if (!string.IsNullOrWhiteSpace(orderNotificationEmail))
            {
                options.OrderNotificationEmail = orderNotificationEmail;
            }
        });
        services.Configure<OrderRequestOptions>(options =>
        {
            configuration.GetSection(OrderRequestOptions.SectionName).Bind(options);

            var toEmail = configuration["ORDER_REQUEST_TO_EMAIL"];
            if (!string.IsNullOrWhiteSpace(toEmail))
            {
                options.ToEmail = toEmail;
            }

            var fromEmail = configuration["ORDER_REQUEST_FROM_EMAIL"];
            if (!string.IsNullOrWhiteSpace(fromEmail))
            {
                options.FromEmail = fromEmail;
            }
        });

        services.AddHttpClient("Resend", client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<IOrderReceiptSender, ResendOrderReceiptSender>();
        services.AddScoped<IOrderReceiptService, OrderReceiptService>();
        services.AddScoped<IOrderRequestService, OrderRequestService>();
        services.AddScoped<IPaymentService, StripePaymentService>();

        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.PostConfigure<AuthOptions>(options => options.ApplyEnvironmentOverrides(configuration));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));
        services.PostConfigure<GoogleAuthOptions>(options => options.ApplyEnvironmentOverrides(configuration));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AuthOptions>>().Value;
            return new AuthPolicy
            {
                LoginLockoutThreshold = options.LoginLockoutThreshold > 0 ? options.LoginLockoutThreshold : 5,
                LockoutDuration = TimeSpan.FromMinutes(options.LockoutMinutes > 0 ? options.LockoutMinutes : 15),
                RefreshTokenLifetime = TimeSpan.FromDays(options.RefreshTokenDays > 0 ? options.RefreshTokenDays : 14),
                LoginEmailVerification = options.LoginEmailVerification
            };
        });
        services.AddSingleton<IPasswordHasher, IdentityPasswordHasher>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddHttpClient(GoogleAccountVerifier.HttpClientName, client =>
        {
            client.BaseAddress = new Uri("https://oauth2.googleapis.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IGoogleAccountVerifier, GoogleAccountVerifier>();
        services.AddScoped<IAdminUserRepository, AdminUserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IAdminCredentialStore, AdminCredentialStore>();
        if (environment.IsDevelopment())
        {
            services.AddSingleton<ILoginVerificationEmailSender, DevelopmentLoginCodeFileSender>();
        }
        else
        {
            services.AddScoped<ILoginVerificationEmailSender, ResendLoginVerificationEmailSender>();
        }

        services.AddScoped<ILoginFlow, LoginFlow>();
        services.AddHostedService<AdminUserSeeder>();

        return services;
    }
}
