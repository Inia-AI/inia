using Microsoft.EntityFrameworkCore;

namespace IniaAi.Domain.UserData;

public class UserDataAdminDbContext : DbContext
{
    public UserDataAdminDbContext(DbContextOptions<UserDataAdminDbContext> options) : base(options)
    {
    }
}
