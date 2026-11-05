using IniaAi.Domain.Entities.UserManagement;

namespace IniaAi.WebApi.IntegrationTests.Helpers;

public static class UserPoolTestHelper
{
    public static async Task WithPooledUser(ConcurrentTestUserPool pool, Func<User, Task> testLogic)
    {
        User user = pool.CheckoutUser();
        try
        {
            await testLogic(user);
        }
        finally
        {
            pool.ReturnUser(user);
        }
    }
}
