using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TransferCredit.Data;

/// <summary>Lets "dotnet ef" create migrations from this project alone.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TransferCreditDbContext>
{
    public TransferCreditDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=TransferCredit;Trusted_Connection=True;TrustServerCertificate=True";
        var options = new DbContextOptionsBuilder<TransferCreditDbContext>()
            .UseSqlServer(connection)
            .Options;
        return new TransferCreditDbContext(options);
    }
}
