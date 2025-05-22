using Microsoft.EntityFrameworkCore;

namespace FileStoringService.Models;

public class FileDbContext : DbContext
{
    public FileDbContext(DbContextOptions<FileDbContext> options) : base(options)
    {
    }

    public DbSet<FileEntity> Files { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileEntity>()
            .HasKey(f => f.Id);

        modelBuilder.Entity<FileEntity>()
            .Property(f => f.FileName)
            .IsRequired();

        modelBuilder.Entity<FileEntity>()
            .Property(f => f.Content)
            .IsRequired();

        modelBuilder.Entity<FileEntity>()
            .Property(f => f.UploadDate)
            .IsRequired();
    }
} 