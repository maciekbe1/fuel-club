using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Infrastructure
{
    public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
        : IdentityDbContext<ApplicationUser>(options)
    {
        public DbSet<LoyaltyAccount> LoyaltyAccounts => Set<LoyaltyAccount>();
        public DbSet<FuelPurchase> FuelPurchases => Set<FuelPurchase>();
        public DbSet<PointsLedgerEntry> PointsLedgerEntries => Set<PointsLedgerEntry>();
        public DbSet<CashierStation> CashierStations => Set<CashierStation>();
        public DbSet<LoyaltyCard> LoyaltyCards => Set<LoyaltyCard>();
        public DbSet<RewardOffer> RewardOffers => Set<RewardOffer>();
        public DbSet<Coupon> Coupons => Set<Coupon>();
        public DbSet<Idempotency.IdempotencyRecord> IdempotencyRecords => Set<Idempotency.IdempotencyRecord>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        }
    }
}
