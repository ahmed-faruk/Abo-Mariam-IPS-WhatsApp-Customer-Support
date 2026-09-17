using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WhatsAppMonitorAssistant.Modules.Storefront.Domain;

namespace WhatsAppMonitorAssistant.Modules.Storefront.Infrastructure.Persistence.Configurations;

internal sealed class BusinessInfoConfiguration : IEntityTypeConfiguration<BusinessInfo>
{
    public void Configure(EntityTypeBuilder<BusinessInfo> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("business_info");

        builder.HasKey(info => info.Id);
        builder.Property(info => info.Id).HasColumnName("id").UseIdentityAlwaysColumn();

        builder.Property(info => info.Key).HasColumnName("key").IsRequired();
        builder.HasIndex(info => info.Key).IsUnique();

        builder.Property(info => info.AnswerAr).HasColumnName("answer_ar").IsRequired();
        builder.Property(info => info.AnswerEn).HasColumnName("answer_en");
        builder.Property(info => info.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(info => info.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
