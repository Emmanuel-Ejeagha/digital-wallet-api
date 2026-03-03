namespace DigitalWallet.Infrastructure.Persistence.Configurations
{
    /// <summary>
    /// Configures the Account entity for EF Core.
    /// </summary>
    public class AccountConfiguration : IEntityTypeConfiguration<Account>
    {
        public void Configure(EntityTypeBuilder<Account> builder)
        {
            builder.ToTable("Accounts");

            builder.HasKey(a => a.Id);

            builder.Ignore(a => a.CurrencyCode);

            builder.Property(a => a.UserId)
                .IsRequired();

            builder.Property(a => a.Type)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.OwnsOne(a => a.Currency, currency =>
            {
                currency.Property(c => c.Code)
                    .HasColumnName("CurrencyCode")
                    .IsRequired()
                    .HasMaxLength(3);

                currency.Property(c => c.DecimalPlaces)
                    .HasColumnName("CurrencyDecimalPlaces");

                currency.Property(c => c.Symbol)
                    .HasColumnName("CurrencySymbol")
                    .HasMaxLength(10);

                currency.Property(c => c.Name)
                    .HasColumnName("CurrencyName")
                    .HasMaxLength(50);

                currency.WithOwner();
            });

            builder.Property(a => a.Balance)
                .IsRequired()
                .HasColumnType("numeric(19,4)");

            builder.Property(a => a.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(a => a.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(a => a.ConcurrencyToken)
                .IsRowVersion()
                .HasColumnName("xmin");

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex("UserId", "CurrencyCode", "Type")
                .IsUnique()
                .HasDatabaseName("IX_Accounts_UserId_CurrencyCode_Type");

            builder.HasIndex(a => a.UserId)
                .HasDatabaseName("IX_Accounts_UserId");
        }
    }
}
