using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    [ConcurrencyCheck]
    public decimal Price { get; set; }
}

public class ProductContext : DbContext
{
    public DbSet<Product> Products { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=MockDatabase;Trusted_Connection=True;");
    }
}

public class Program
{
    public static void Main(string[] args)
    {
    }
}

public class CaseReproduction : IDisposable
{
    public CaseReproduction()
    {
        using var context = new ProductContext();
        context.Database.EnsureDeleted();
        context.Database.EnsureCreated();

        context.Add(new Product
        {
            Name = "Coconut",
            Description = "A very tasty coconut.",
            Price = 10
        });

        context.SaveChanges();
    }

    public void Dispose()
    {
        using var context = new ProductContext();
        context.Database.EnsureDeleted();
    }

    [Fact]
    public async Task ApplyDiscountToCoconut_UsingAssignmentSubtractionOperator_ThrowsDbUpdateConcurrencyException()
    {
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ApplyDiscountToCoconut(coconut => coconut.Price -= 2));
    }

    [Fact]
    public async Task ApplyDiscountToCoconut_UsingAssignmentOperator_ThrowsRetryLimitExceededException()
    {
        await Assert.ThrowsAsync<RetryLimitExceededException>(() => ApplyDiscountToCoconut(coconut => coconut.Price = 8));
    }

    private async Task ApplyDiscountToCoconut(Action<Product> discountAction)
    {
        using var context = new ProductContext();

        var executionStrategy = new SqlServerRetryingExecutionStrategy(
            context,
            2,
            TimeSpan.FromSeconds(10),
            new int[] { 17142 }); // Add server paused exception to transient exceptions for emulating transient error.

        await executionStrategy.ExecuteAsync(async () =>
        {
            using var transaction = await context.Database.BeginTransactionAsync();

            var coconut = await context.Products.SingleAsync(x => x.Name == "Coconut");

            discountAction(coconut);

            await context.SaveChangesAsync(true);

            await context.Database.ExecuteSqlRawAsync("RAISERROR(17142,16,0) WITH LOG;");

            await transaction.CommitAsync();
        });
    }
}
