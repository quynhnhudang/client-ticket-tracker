using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TicketTracker.Api.Domain;

namespace TicketTracker.Api.Data;

/// <summary>
/// Creates the first support-agent login, and only when the Agents table is empty.
///
/// The email and password come from configuration (user-secrets in development,
/// environment variables on the server), never from source code. Seeding them
/// with EF's HasData would put a password hash in a migration file, committed to Git.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedInitialAgentAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var email = config["SeedAgent:Email"]?.Trim().ToLowerInvariant();
        var password = config["SeedAgent:Password"];
        var displayName = config["SeedAgent:DisplayName"] ?? "Support Agent";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            return;

        if (password.Length < 12)
        {
            logger.LogWarning("SeedAgent:Password must be at least 12 characters; no agent was created.");
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Agent>>();

        try
        {
            if (await db.Agents.AnyAsync())
                return;

            var agent = new Agent { Email = email, DisplayName = displayName, CreatedAt = DateTimeOffset.UtcNow };
            agent.PasswordHash = hasher.HashPassword(agent, password);
            db.Agents.Add(agent);
            await db.SaveChangesAsync();
            logger.LogInformation("Created initial agent {Email}.", email);
        }
        catch (SqlException ex)
        {
            // Most likely the schema hasn't been created yet. Keep starting up
            // (so /api/health still answers) but say what to do.
            logger.LogError(ex,
                "Could not seed the initial agent. Has the database been created? Run: dotnet ef database update");
        }
    }
}
