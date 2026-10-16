using ECommerce.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ECommerce.Infrastructure.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        if (context.Customers.Any() && context.Products.Any())
        {
            return;
        }

        logger.LogInformation("Seeding initial eCommerce catalog and demo customers...");

        var customers = new List<Customer>
        {
            new()
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Alice Montgomery",
                Email = "alice.montgomery@example.com",
                PhoneNumber = "+1 (555) 234-5678",
                Address = "742 Evergreen Terrace, Springfield, OR",
                Tier = "Platinum",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-30)
            },
            new()
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Bruce Wayne",
                Email = "bruce.wayne@waynecorp.com",
                PhoneNumber = "+1 (555) 987-6543",
                Address = "1007 Mountain Drive, Gotham City, NJ",
                Tier = "Gold",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-20)
            },
            new()
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Clara Oswald",
                Email = "clara.oswald@example.co.uk",
                PhoneNumber = "+44 20 7946 0912",
                Address = "221B Baker Street, London, UK",
                Tier = "Standard",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-10)
            }
        };

        var products = new List<Product>
        {
            new()
            {
                Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Name = "Quantum Wireless Noise-Cancelling Headphones",
                Sku = "AUDIO-QW-001",
                Description = "High-fidelity wireless sound with ultra-low latency and 40-hour battery life.",
                Category = "Audio",
                Price = 299.99m,
                StockQuantity = 150,
                ImageUrl = "https://images.unsplash.com/photo-1505740420928-5e560c06d30e?w=500&auto=format&fit=crop&q=60",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-60)
            },
            new()
            {
                Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Name = "Mechanical RGB Gaming Keyboard Pro",
                Sku = "KEYB-MECH-002",
                Description = "Custom linear switches, hot-swappable PCB, and per-key RGB backlighting.",
                Category = "Accessories",
                Price = 149.50m,
                StockQuantity = 220,
                ImageUrl = "https://images.unsplash.com/photo-1587829741301-dc798b83add3?w=500&auto=format&fit=crop&q=60",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-60)
            },
            new()
            {
                Id = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Name = "Ultra-Wide 34-Inch Curved Monitor 165Hz",
                Sku = "DISP-CURV-003",
                Description = "Immersive IPS display with HDR400, 1ms response time, and USB-C power delivery.",
                Category = "Displays",
                Price = 649.00m,
                StockQuantity = 45,
                ImageUrl = "https://images.unsplash.com/photo-1527443224154-c4a3942d3acf?w=500&auto=format&fit=crop&q=60",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-60)
            },
            new()
            {
                Id = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Name = "Ergonomic Precision Wireless Mouse",
                Sku = "MOUSE-ERGO-004",
                Description = "Sculpted ergonomic design with infinite scroll wheel and multi-device pairing.",
                Category = "Accessories",
                Price = 89.95m,
                StockQuantity = 300,
                ImageUrl = "https://images.unsplash.com/photo-1527864550417-7fd91fc51a46?w=500&auto=format&fit=crop&q=60",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-60)
            },
            new()
            {
                Id = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                Name = "4K Studio USB-C Streaming Webcam",
                Sku = "CAM-4K-005",
                Description = "Broadcast-quality video with AI auto-framing and dual noise-cancelling mics.",
                Category = "Video",
                Price = 179.99m,
                StockQuantity = 80,
                ImageUrl = "https://images.unsplash.com/photo-1588872657578-7efd1f1555ed?w=500&auto=format&fit=crop&q=60",
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-60)
            }
        };

        if (!context.Customers.Any())
        {
            await context.Customers.AddRangeAsync(customers);
        }

        if (!context.Products.Any())
        {
            await context.Products.AddRangeAsync(products);
        }

        await context.SaveChangesAsync();
        logger.LogInformation("Database seeded successfully with {CustomerCount} customers and {ProductCount} products.",
            customers.Count, products.Count);
    }
}
