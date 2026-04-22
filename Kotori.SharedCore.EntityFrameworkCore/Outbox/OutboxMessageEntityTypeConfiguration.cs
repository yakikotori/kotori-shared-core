using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kotori.SharedCore.EntityFrameworkCore.Outbox;

public class OutboxMessageEntityTypeConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("outbox_messages");
        
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasConversion(id => id.Value, id => new OutboxMessageEntityId(id));

        builder.HasIndex(message => message.State);
        
        builder.Property(message => message.Type)
            .HasMaxLength(200);

        builder.Property(message => message.Payload)
            .HasColumnType("jsonb");

        builder.Property(message => message.ErrorMessage)
            .HasMaxLength(3000);
    }
}