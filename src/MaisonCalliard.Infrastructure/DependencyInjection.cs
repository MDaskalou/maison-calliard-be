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
        services.AddSingleton<IFileStorageService>(new LocalFileStorageService(uploadsPath, baseUrl));

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
