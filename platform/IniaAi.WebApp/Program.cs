using IniaAi.Application;
using IniaAi.Configuration.Extensions;
using IniaAi.Infrastructure;
using IniaAi.Infrastructure.Hosting;
using IniaAi.Infrastructure.Persistence.Setup;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.WebApp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Serilog;

// The order of the configurators is EXTREMELY important here

CultureBootstrap.PinInvariantCulture();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
await builder.Configuration.SetupConfigurationAsync();
builder.AddRunIntakeControl();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger);

#if DEBUG
builder.WebHost.UseStaticWebAssets();
#endif

builder.AddIniaAiDatabases();

// Add infrastructure services
builder.Services.AddInfrastructure(builder.Configuration);

// Add application services
builder.Services.AddApplication();

ServicesConfigurator servicesConfigurator = new(builder);
servicesConfigurator.SetupServices();

builder.Services.AddControllers();

builder.Services.Configure<FormOptions>(o =>
    {
        o.MultipartBodyLengthLimit = 5242880;
        o.ValueLengthLimit = 5242880;
    });

builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.MaximumReceiveMessageSize = 5242880;
    });

// Add Data Protection
builder.Services.AddDataProtection()
    .SetApplicationName("IniaAi")
    .PersistKeysToDbContext<UserDataDbContext>();

WebApplication app = builder.Build();

AppConfigurator appConfigurator = new();
appConfigurator.SetupBlazorWebApp(app);

app.MapControllers();

app.InitializeDatabases();

app.Run();
