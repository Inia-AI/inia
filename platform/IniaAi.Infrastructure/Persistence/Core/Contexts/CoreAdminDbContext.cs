using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.Core;

public class CoreAdminDbContext : DbContext
{
    public CoreAdminDbContext(DbContextOptions<CoreAdminDbContext> options) : base(options)
    {
    }
}
