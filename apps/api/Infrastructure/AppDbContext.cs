using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Infrastructure
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
        : IdentityDbContext<ApplicationUser>(options),
            IDataProtectionKeyContext
    {
        /// <summary>Data Protection key ring (cookies, CSRF, chat confirmations) shared by all instances.</summary>
        public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
        public DbSet<LoyaltyAccount> LoyaltyAccounts => Set<LoyaltyAccount>();
        public DbSet<FuelPurchase> FuelPurchases => Set<FuelPurchase>();
        public DbSet<PointsLedgerEntry> PointsLedgerEntries => Set<PointsLedgerEntry>();
        public DbSet<CashierStation> CashierStations => Set<CashierStation>();
        public DbSet<LoyaltyCard> LoyaltyCards => Set<LoyaltyCard>();
        public DbSet<RewardOffer> RewardOffers => Set<RewardOffer>();
        public DbSet<Coupon> Coupons => Set<Coupon>();
        public DbSet<Modules.Assistant.Tracing.AssistantTurn> AssistantTurns =>
            Set<Modules.Assistant.Tracing.AssistantTurn>();
        public DbSet<Modules.Observability.SecurityEvent> SecurityEvents =>
            Set<Modules.Observability.SecurityEvent>();
        public DbSet<Idempotency.IdempotencyRecord> IdempotencyRecords =>
            Set<Idempotency.IdempotencyRecord>();

        /// <summary>
        /// Every tracked EF update of an offer raises its version and UpdatedAt, so a form loaded before
        /// that change gets 409. The admin endpoint writes with a conditional UPDATE and bumps both itself.
        /// ExecuteUpdate bypasses SaveChanges: it must set "Version" = "Version" + 1 explicitly.
        /// </summary>
        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            BumpRewardOfferVersions();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            BumpRewardOfferVersions();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        private void BumpRewardOfferVersions()
        {
            // Entries() runs change detection only when AutoDetectChangesEnabled, like SaveChanges itself.
            foreach (
                var entry in ChangeTracker
                    .Entries<RewardOffer>()
                    .Where(entry => entry.State == EntityState.Modified)
            )
            {
                var version = entry.Property(offer => offer.Version);
                version.CurrentValue = version.OriginalValue + 1;
                // The previous value may come from the database clock (now()), which can run slightly
                // ahead of this process: never move UpdatedAt backwards (1 µs is the column's resolution).
                var updatedAt = entry.Property(offer => offer.UpdatedAt);
                var next = updatedAt.OriginalValue.AddTicks(10);
                var now = DateTimeOffset.UtcNow;
                updatedAt.CurrentValue = now > next ? now : next;
            }
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
