using Microsoft.EntityFrameworkCore;
using Regal.Web.Data.Entities;
using Regal.Web.Features.Products;

namespace Regal.Web.Data;

public sealed class RegalDbContext(DbContextOptions<RegalDbContext> options) : DbContext(options)
{
    public const string InternalEanSequence = "InternalEanNumbers";

    public DbSet<User> Users => Set<User>();

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Contractor> Contractors => Set<Contractor>();

    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<StockLevel> Stock => Set<StockLevel>();

    public DbSet<StockDocument> StockDocuments => Set<StockDocument>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<DocumentCounter> DocumentCounters => Set<DocumentCounter>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.ToTable("Invoices");
            invoice.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
            invoice.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            invoice.Property(i => i.ExternalOrderId).HasMaxLength(64);
            invoice.Property(i => i.Number).HasMaxLength(100);
            invoice.Property(i => i.PdfUrl).HasMaxLength(500);
            invoice.Property(i => i.PaymentMethod).HasMaxLength(20);
            invoice.Property(i => i.NetTotal).HasPrecision(18, 2);
            invoice.Property(i => i.VatTotal).HasPrecision(18, 2);
            invoice.Property(i => i.GrossTotal).HasPrecision(18, 2);
            invoice.Property(i => i.RemoteGrossTotal).HasPrecision(18, 2);
            invoice.HasIndex(i => i.ExternalOrderId).IsUnique();
            invoice.HasIndex(i => i.IssueDate);
            invoice.HasIndex(i => new { i.Status, i.IssueDate });
            invoice.HasOne(i => i.Contractor).WithMany().HasForeignKey(i => i.ContractorId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasOne(i => i.StockDocument).WithMany().HasForeignKey(i => i.StockDocumentId).OnDelete(DeleteBehavior.Restrict);
            invoice.HasOne(i => i.User).WithMany().HasForeignKey(i => i.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceLine>(line =>
        {
            line.ToTable("InvoiceLines", table =>
            {
                table.HasCheckConstraint("CK_InvoiceLines_Quantity_Positive", "\"Quantity\" > 0");
                table.HasCheckConstraint("CK_InvoiceLines_Discount_Range", "\"DiscountPercent\" BETWEEN 0 AND 100");
            });
            line.Property(l => l.ProductName).HasMaxLength(200);
            line.Property(l => l.Unit).HasMaxLength(20);
            line.Property(l => l.VatRate).HasMaxLength(3);
            line.Property(l => l.Quantity).HasPrecision(18, 3);
            line.Property(l => l.NetPrice).HasPrecision(18, 2);
            line.Property(l => l.DiscountPercent).HasPrecision(5, 2);
            line.Property(l => l.NetValue).HasPrecision(18, 2);
            line.Property(l => l.VatValue).HasPrecision(18, 2);
            line.Property(l => l.GrossValue).HasPrecision(18, 2);
            line.Property(l => l.UnitCost).HasPrecision(18, 4);
            line.HasOne(l => l.Invoice).WithMany(i => i.Lines).HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            line.HasOne(l => l.Product).WithMany().HasForeignKey(l => l.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.ToTable("OutboxMessages");
            message.Property(m => m.Type).HasMaxLength(50);
            message.Property(m => m.LastError).HasMaxLength(1000);
            message.HasIndex(m => m.NextAttemptAt).HasFilter("\"NextAttemptAt\" IS NOT NULL");
            message.HasIndex(m => m.InvoiceId).IsUnique();
            message.HasOne(m => m.Invoice).WithMany().HasForeignKey(m => m.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Batch>(batch =>
        {
            batch.ToTable("Batches");
            batch.Property(b => b.Number).HasMaxLength(50);
            batch.HasIndex(b => new { b.ProductId, b.Number }).IsUnique();
            batch.HasIndex(b => b.ExpiryDate).HasFilter("\"ExpiryDate\" IS NOT NULL");
            batch.HasOne(b => b.Product)
                .WithMany()
                .HasForeignKey(b => b.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockLevel>(stock =>
        {
            stock.ToTable("Stock", table =>
                table.HasCheckConstraint("CK_Stock_Quantity_NonNegative", "\"Quantity\" >= 0"));
            stock.HasKey(s => new { s.WarehouseId, s.BatchId });
            stock.Property(s => s.Quantity).HasPrecision(18, 3);
            stock.HasOne(s => s.Warehouse)
                .WithMany()
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
            stock.HasOne(s => s.Batch)
                .WithMany()
                .HasForeignKey(s => s.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockDocument>(document =>
        {
            document.ToTable("StockDocuments");
            document.Property(d => d.Type).HasConversion<string>().HasMaxLength(2);
            document.Property(d => d.Number).HasMaxLength(30);
            document.Property(d => d.Notes).HasMaxLength(500);
            document.HasIndex(d => d.Number).IsUnique();
            document.HasIndex(d => d.RequestId).IsUnique();
            document.HasIndex(d => d.ReversesDocumentId).IsUnique().HasFilter("\"ReversesDocumentId\" IS NOT NULL");
            document.HasIndex(d => d.DocumentDate);
            document.HasIndex(d => new { d.Type, d.DocumentDate });
            document.HasOne(d => d.Contractor)
                .WithMany()
                .HasForeignKey(d => d.ContractorId)
                .OnDelete(DeleteBehavior.Restrict);
            document.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            document.HasOne(d => d.ReversesDocument)
                .WithMany()
                .HasForeignKey(d => d.ReversesDocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.ToTable("StockMovements", table =>
            {
                table.HasCheckConstraint("CK_StockMovements_QuantityChange_NonZero", "\"QuantityChange\" <> 0");
                table.HasCheckConstraint("CK_StockMovements_UnitCost_NonNegative", "\"UnitCost\" >= 0");
            });
            movement.Property(m => m.QuantityChange).HasPrecision(18, 3);
            movement.Property(m => m.UnitCost).HasPrecision(18, 4);
            movement.HasIndex(m => new { m.BatchId, m.Id });
            movement.HasOne(m => m.Document)
                .WithMany(d => d.Movements)
                .HasForeignKey(m => m.DocumentId)
                .OnDelete(DeleteBehavior.Restrict);
            movement.HasOne(m => m.Batch)
                .WithMany()
                .HasForeignKey(m => m.BatchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentCounter>(counter =>
        {
            counter.ToTable("DocumentCounters");
            counter.HasKey(c => new { c.Type, c.Year });
            counter.Property(c => c.Type).HasConversion<string>().HasMaxLength(2);
        });

        modelBuilder.HasSequence<long>(InternalEanSequence).StartsAt(1).HasMax(Ean13.MaxInternalNumber);

        modelBuilder.Entity<Warehouse>(warehouse =>
        {
            warehouse.ToTable("Warehouses");
            // The id is assigned by hand: a seeded row would not advance an identity counter.
            warehouse.Property(w => w.Id).ValueGeneratedNever();
            warehouse.Property(w => w.Name).HasMaxLength(100);
            warehouse.HasData(new Warehouse { Id = Warehouse.MainWarehouseId, Name = "Magazyn główny" });
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.ToTable("Categories");
            // Case-insensitive uniqueness is the index IX_Categories_Name_Lower, created in the migration.
            category.Property(c => c.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products", table =>
            {
                table.HasCheckConstraint("CK_Products_NetPrice_NonNegative", "\"NetPrice\" >= 0");
                table.HasCheckConstraint("CK_Products_AverageCost_NonNegative", "\"AverageCost\" >= 0");
            });
            product.Property(p => p.Sku).HasMaxLength(50);
            product.Property(p => p.Ean).HasMaxLength(13);
            product.Property(p => p.Name).HasMaxLength(200);
            product.Property(p => p.Unit).HasMaxLength(20);
            product.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
            product.Property(p => p.NetPrice).HasPrecision(18, 2);
            product.Property(p => p.AverageCost).HasPrecision(18, 4);
            product.Property(p => p.VatRate).HasMaxLength(3);
            product.HasIndex(p => p.Sku).IsUnique();
            product.HasIndex(p => p.Ean).IsUnique();
            product.HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Contractor>(contractor =>
        {
            contractor.ToTable("Contractors");
            contractor.Property(c => c.Name).HasMaxLength(200);
            contractor.Property(c => c.Nip).HasMaxLength(10);
            contractor.Property(c => c.Address).HasMaxLength(500);
            contractor.Property(c => c.Email).HasMaxLength(320);
            contractor.Property(c => c.Phone).HasMaxLength(30);
        });

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("Users", table =>
                table.HasCheckConstraint("CK_Users_Email_Lowercase", "\"Email\" = lower(\"Email\")"));
            user.Property(u => u.Email).HasMaxLength(320);
            user.Property(u => u.Role).HasMaxLength(50);
            user.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<AuditLogEntry>(entry =>
        {
            entry.ToTable("AuditLog");
            entry.Property(e => e.Action).HasMaxLength(100);
            entry.Property(e => e.EntityType).HasMaxLength(100);
            entry.Property(e => e.EntityId).HasMaxLength(100);
            entry.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
