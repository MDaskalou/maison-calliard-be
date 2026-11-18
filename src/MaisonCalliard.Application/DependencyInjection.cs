using MaisonCalliard.Application.Auth;
using MaisonCalliard.Application.Menu;
using MaisonCalliard.Application.News;
using MaisonCalliard.Application.Orders;
using MaisonCalliard.Application.Products;
using MaisonCalliard.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace MaisonCalliard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<INewsService, NewsService>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
