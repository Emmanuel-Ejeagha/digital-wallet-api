using System.Threading.Tasks;

namespace DigitalWallet.Infrastructure.Persistence
{
    /// <summary>
    /// Provides seed data for the application. Idempotency-safe to run on every start.
    /// </summary>
    public static class ApplicationDbContextSeed
    {
        public static async Task SeedAsync(ApplicationDbContext context, ILogger logger)
        {
            await context.Database.MigrateAsync();

            // Ensure system user exists
            var systemUser = await context.Users.FirstOrDefaultAsync(u => u.Email == "system@internal");
            if (systemUser == null)
            {
                systemUser = new User("system", "system@internal", "System", "User");
                context.Users.Add(systemUser);
                await context.SaveChangesAsync();
                logger.LogInformation("System user created with Id: {Id}", systemUser.Id);
            }

            var systemUserId = systemUser.Id;

            var systemAccounts = new[]
            {
                (Currency.USD, AccountType.SystemReserve, "System Reserve USD"),
                (Currency.NGN, AccountType.SystemReserve, "System Reserve NGN"),
                (Currency.EUR, AccountType.SystemReserve, "System Reserve EUR"),
                (Currency.GBP, AccountType.SystemReserve, "System Reserve GBP"),
                (Currency.USD, AccountType.FeeIncome, "Fee Income USD"),
                (Currency.NGN, AccountType.FeeIncome, "Fee Income NGN"),
                (Currency.EUR, AccountType.FeeIncome, "Fee Income EUR"),
                (Currency.GBP, AccountType.FeeIncome, "Fee Income GBP")
            };

            foreach (var (currency, type, name) in systemAccounts)
            {
                var exists = await context.Accounts
                    .AnyAsync(a => a.UserId == systemUserId
                                   && a.Currency.Code == currency.Code
                                   && a.Type == type);

                if (!exists)
                {
                    var account = new Account(systemUserId, type, currency, name);
                    context.Accounts.Add(account);
                    logger.LogInformation("Seeding account: {Name} ({Currency})", name, currency.Code);
                }
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Database seeding completed.");
        }
    }
}