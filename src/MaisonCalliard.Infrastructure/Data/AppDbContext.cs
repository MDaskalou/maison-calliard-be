using System.Text.Json;
using MaisonCalliard.Domain.Entities;
using MaisonCalliard.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MaisonCalliard.Infrastructure.Data;

internal sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<NewsItem> NewsItems => Set<NewsItem>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<ReceiptSequence> ReceiptSequences => Set<ReceiptSequence>();
    public DbSet<AppSettings> Settings => Set<AppSettings>();
    public DbSet<AdminUser> AdminUsers => Set<AdminUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<LoginEmailChallenge> LoginEmailChallenges => Set<LoginEmailChallenge>();
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();
    public DbSet<LoginEmailQuota> LoginEmailQuotas => Set<LoginEmailQuota>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var jsonOptions = new JsonSerializerOptions();

        ConfigureLocalizedText(modelBuilder, jsonOptions);
        ConfigureNewsItem(modelBuilder);
        ConfigureMenuItem(modelBuilder, jsonOptions);
        ConfigureProduct(modelBuilder, jsonOptions);
        ConfigureOrder(modelBuilder);
        ConfigureReceiptSequence(modelBuilder);
        ConfigureSettings(modelBuilder);
        ConfigureAdminUser(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureLoginEmailChallenge(modelBuilder);
        ConfigureTrustedDevice(modelBuilder);
        ConfigureLoginEmailQuota(modelBuilder);
    }

    private static void ConfigureLocalizedText(ModelBuilder modelBuilder, JsonSerializerOptions jsonOptions)
    {
        var stringListComparer = new ValueComparer<List<string>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
            v => JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(v, jsonOptions), jsonOptions)!);

        modelBuilder.Entity<NewsItem>().OwnsOne(n => n.Title);
        modelBuilder.Entity<NewsItem>().OwnsOne(n => n.Subtitle);

        modelBuilder.Entity<MenuItem>().OwnsOne(m => m.Name);
        modelBuilder.Entity<MenuItem>().OwnsOne(m => m.Description);
        modelBuilder.Entity<MenuItem>().OwnsOne(m => m.Ingredients);
        modelBuilder.Entity<MenuItem>()
            .Property(m => m.Allergies)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        modelBuilder.Entity<Product>().OwnsOne(p => p.Name);
        modelBuilder.Entity<Product>().OwnsOne(p => p.Description);
        modelBuilder.Entity<Product>().OwnsOne(p => p.Ingredients);
        modelBuilder.Entity<Product>()
            .Property(p => p.Allergies)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, jsonOptions) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        var priceOptionsComparer = new ValueComparer<List<PriceOption>>(
            (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
            v => JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<List<PriceOption>>(JsonSerializer.Serialize(v, jsonOptions), jsonOptions)!);

        modelBuilder.Entity<Product>()
            .Property(p => p.PriceOptions)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => JsonSerializer.Deserialize<List<PriceOption>>(v, jsonOptions) ?? new List<PriceOption>())
            .Metadata.SetValueComparer(priceOptionsComparer);

        var stockComparer = new ValueComparer<Dictionary<string, int>?>(
            (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
            v => JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, int>>(JsonSerializer.Serialize(v, jsonOptions), jsonOptions));

        modelBuilder.Entity<Product>()
            .Property(p => p.Stock)
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, jsonOptions),
                v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, int>>(v, jsonOptions))
            .Metadata.SetValueComparer(stockComparer);
    }

    private static void ConfigureNewsItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<NewsItem>().HasKey(n => n.Id);
    }

    private static void ConfigureMenuItem(ModelBuilder modelBuilder, JsonSerializerOptions jsonOptions)
    {
        modelBuilder.Entity<MenuItem>().HasKey(m => m.Id);
        modelBuilder.Entity<MenuItem>().Property(m => m.Price).HasPrecision(18, 2);
    }

    private static void ConfigureProduct(ModelBuilder modelBuilder, JsonSerializerOptions jsonOptions)
    {
        modelBuilder.Entity<Product>().HasKey(p => p.Id);
        modelBuilder.Entity<Product>().Property(p => p.ImagePosition).HasDefaultValue("50% 50%");
    }

    private static void ConfigureOrder(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().HasKey(o => o.Id);
        modelBuilder.Entity<Order>()
            .HasIndex(o => o.ReceiptNumber)
            .IsUnique()
            .HasFilter("\"ReceiptNumber\" IS NOT NULL");
        modelBuilder.Entity<Order>()
            .HasIndex(o => o.StripePaymentIntentId)
            .IsUnique()
            .HasFilter("\"StripePaymentIntentId\" IS NOT NULL");
        modelBuilder.Entity<Order>()
            .HasIndex(o => o.StripeSessionId)
            .IsUnique()
            .HasFilter("\"StripeSessionId\" IS NOT NULL");
        modelBuilder.Entity<Order>().Property(o => o.Total).HasPrecision(18, 2);
        modelBuilder.Entity<Order>().Property(o => o.TaxAmount).HasPrecision(18, 2);
        modelBuilder.Entity<Order>()
            .HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CartItem>().HasKey(c => c.Id);
        modelBuilder.Entity<CartItem>().Property(c => c.Price).HasPrecision(18, 2);
        modelBuilder.Entity<CartItem>().Property(c => c.TaxRate).HasPrecision(5, 2);
        modelBuilder.Entity<CartItem>().Property(c => c.IsPaid).HasDefaultValue(true);
    }

    private static void ConfigureReceiptSequence(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReceiptSequence>().HasKey(s => s.Year);
        modelBuilder.Entity<ReceiptSequence>().Property(s => s.Year).ValueGeneratedNever();
    }

    private static void ConfigureSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppSettings>().HasKey(s => s.Id);
        modelBuilder.Entity<AppSettings>().HasData(new AppSettings { Id = Guid.Parse("00000000-0000-0000-0000-000000000001"), LeadTimeDays = 3 });
    }

    private static void ConfigureAdminUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>().HasKey(u => u.Id);
        modelBuilder.Entity<AdminUser>().Property(u => u.Email).HasMaxLength(320).IsRequired();
        modelBuilder.Entity<AdminUser>().Property(u => u.NormalizedEmail).HasMaxLength(320).IsRequired();
        modelBuilder.Entity<AdminUser>().HasIndex(u => u.NormalizedEmail).IsUnique();
        modelBuilder.Entity<AdminUser>().Property(u => u.Role).HasMaxLength(32).IsRequired();
        modelBuilder.Entity<AdminUser>().Property(u => u.PasswordHash).HasMaxLength(512);
        modelBuilder.Entity<AdminUser>().Property(u => u.MustChangePassword).HasDefaultValue(false);
        modelBuilder.Entity<AdminUser>().Property(u => u.TokenVersion).HasDefaultValue(0).IsConcurrencyToken();
        modelBuilder.Entity<AdminUser>().Property(u => u.GoogleSubject).HasMaxLength(255);
        modelBuilder.Entity<AdminUser>()
            .HasIndex(u => u.GoogleSubject)
            .IsUnique()
            .HasFilter("\"GoogleSubject\" IS NOT NULL");
    }

    private static void ConfigureRefreshToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>().HasKey(t => t.Id);
        modelBuilder.Entity<RefreshToken>().Property(t => t.TokenHash).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();
        modelBuilder.Entity<RefreshToken>()
            .HasOne<AdminUser>()
            .WithMany()
            .HasForeignKey(t => t.AdminUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLoginEmailChallenge(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoginEmailChallenge>().ToTable("LoginEmailChallenges");
        modelBuilder.Entity<LoginEmailChallenge>().HasKey(challenge => challenge.Id);
        modelBuilder.Entity<LoginEmailChallenge>().Property(challenge => challenge.TokenHash).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<LoginEmailChallenge>().Property(challenge => challenge.CodeHash).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<LoginEmailChallenge>().HasIndex(challenge => challenge.TokenHash).IsUnique();
        modelBuilder.Entity<LoginEmailChallenge>().HasIndex(challenge => challenge.AdminUserId);
        modelBuilder.Entity<LoginEmailChallenge>()
            .HasOne<AdminUser>()
            .WithMany()
            .HasForeignKey(challenge => challenge.AdminUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTrustedDevice(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrustedDevice>().ToTable("TrustedDevices");
        modelBuilder.Entity<TrustedDevice>().HasKey(device => device.Id);
        modelBuilder.Entity<TrustedDevice>().Property(device => device.TokenHash).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<TrustedDevice>().Property(device => device.Label).HasMaxLength(200);
        modelBuilder.Entity<TrustedDevice>().HasIndex(device => device.TokenHash).IsUnique();
        modelBuilder.Entity<TrustedDevice>().HasIndex(device => device.AdminUserId);
        modelBuilder.Entity<TrustedDevice>()
            .HasOne<AdminUser>()
            .WithMany()
            .HasForeignKey(device => device.AdminUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureLoginEmailQuota(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LoginEmailQuota>().ToTable("LoginEmailQuotas");
        modelBuilder.Entity<LoginEmailQuota>().HasKey(quota => quota.AdminUserId);
        modelBuilder.Entity<LoginEmailQuota>()
            .HasOne<AdminUser>()
            .WithMany()
            .HasForeignKey(quota => quota.AdminUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
