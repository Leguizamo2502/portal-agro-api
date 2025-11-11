using Entity.Domain.Models.Implements.Auth.Token;
using Entity.Validation.Service;
using Entity.Validations.interfaces;
using FluentValidation;
using FluentValidation.AspNetCore;
using Web.Hubs.Implements.Notifications;
using Web.ProgramService;

var builder = WebApplication.CreateBuilder(args);

//Configuración de servicios base
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

//FluentValidation
builder.Services.AddScoped<IValidatorService, ValidatorService>();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterUserDtoValidator>();
builder.Services.AddFluentValidationAutoValidation();

//Autenticación y JWT
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<CookieSettings>(builder.Configuration.GetSection("Cookie"));

//Servicios externos
builder.Services.AddCloudinaryServices(builder.Configuration);

//Capa de aplicación, base de datos, background y cache
builder.Services.AddApplicationServices();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddBackgroundServices(builder.Configuration);
builder.Services.AddOutputCachePolicies();

//SignalR y CORS
builder.Services.AddSignalR();
builder.Services.AddCustomCors(builder.Configuration);

// Construcción de la aplicación
var app = builder.Build();

//Archivos estáticos y Swagger
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "PortalAgro API v1");
    c.RoutePrefix = "swagger";
});


app.UseHttpsRedirection();

// CORS
app.UseCors();

// Autenticación
app.UseAuthentication();

// Luego autorización
app.UseAuthorization();

// Cache
app.UseOutputCache();

// Controladores
app.MapControllers();

// Hubs
app.MapHub<NotificationHub>("/hubs/notifications");

// Migraciones en arranque
//MigrationManager.MigrateAllDatabases(app.Services, builder.Configuration);


app.Run();
