using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kotori.SharedCore.EntityFrameworkCore.Inbox;

public class InboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<InboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<InboxMessageEntity> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(message => new { message.MessageId, message.Consumer });

        builder.Property(message => message.Consumer)
            .HasMaxLength(InboxMessageEntity.MaxConsumerLength);

        builder.HasIndex(message => message.ProcessedOnUtc);
    }
}
