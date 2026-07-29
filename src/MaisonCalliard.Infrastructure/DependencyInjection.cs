using MaisonCalliard.Application.Files;
using MaisonCalliard.Application.Payments;
using MaisonCalliard.Application.Receipts;
using MaisonCalliard.Domain.Repositories;
using MaisonCalliard.Infrastructure.Data;
using MaisonCalliard.Infrastructure.Options;
using MaisonCalliard.Infrastructure.Repositories;
using MaisonCalliard.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
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
            services.AddHttpClient("SupabaseStorage", client =>
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", supabaseOptions.ServiceRoleKey);
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

        services.AddHttpClient("Resend", client =>
        {
            client.BaseAddress = new Uri("https://api.resend.com/");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        });

        services.AddScoped<IOrderReceiptSender, ResendOrderReceiptSender>();
        services.AddScoped<IOrderReceiptService, OrderReceiptService>();
        services.AddScoped<IPaymentService, StripePaymentService>();

        return services;
    }
}
