using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PickleballApi
{
    public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var host = Environment.GetEnvironmentVariable("MYSQLHOST") ?? "localhost";
            var port = Environment.GetEnvironmentVariable("MYSQLPORT") ?? "3306";
            var database = Environment.GetEnvironmentVariable("MYSQLDATABASE") ?? "dinkdb";
            var user = Environment.GetEnvironmentVariable("MYSQLUSER") ?? "root";
            var password = Environment.GetEnvironmentVariable("MYSQLPASSWORD") ?? "password";
            var connectionString = $"server={host};port={port};database={database};user={user};password={password}";

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36)));

            return new AppDbContext(optionsBuilder.Options);
        }
    }
}
