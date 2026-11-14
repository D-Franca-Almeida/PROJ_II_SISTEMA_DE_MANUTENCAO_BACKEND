using Microsoft.AspNetCore.Identity;
using CPTM.Manutencao.Api.Models;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CPTM.Manutencao.Api.Services;
using CPTM.Manutencao.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Injeção de Dependência do Serviço de autenticação
builder.Services.AddSingleton<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Persistência (EF Core). Provedor escolhido por Database:Provider ("Sqlite" | "Oracle").
// SQLite é temporário até existir uma instância Oracle: débito técnico CPTM-92.
var provedor = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContext<ManutencaoDbContext>(opcoes =>
{
    var connectionString = builder.Configuration.GetConnectionString(provedor)
        ?? throw new InvalidOperationException($"ConnectionStrings:{provedor} não configurada.");

    if (provedor.Equals("Oracle", StringComparison.OrdinalIgnoreCase))
        opcoes.UseOracle(connectionString);
    else
        opcoes.UseSqlite(connectionString);
});

// Motor de Triagem (Workflow)
builder.Services.AddScoped<ISolicitacaoRepository, SolicitacaoRepository>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<ICatalogoRepository, CatalogoRepository>();
builder.Services.AddScoped<IOrdemServicoRepository, OrdemServicoRepository>();
builder.Services.AddScoped<IPedidoPecaRepository, PedidoPecaRepository>();
builder.Services.AddScoped<IPedidoPecaService, PedidoPecaService>();
builder.Services.AddScoped<ISolicitacaoService, SolicitacaoService>();

// Expiração (US-20): prazo e agendamento em Prazos:*; o relógio é injetável (TimeProvider) para os testes.
var prazos = builder.Configuration.GetSection("Prazos").Get<PrazosOptions>() ?? new PrazosOptions();
builder.Services.AddSingleton(prazos);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IExpiracaoService, ExpiracaoService>();
builder.Services.AddHostedService<ExpiracaoBackgroundService>();

// Fotos das SSLs ficam em disco (nunca no banco). A raiz vem de Uploads:Raiz (padrão: wwwroot/uploads).
var raizUploads = builder.Configuration["Uploads:Raiz"];
if (string.IsNullOrWhiteSpace(raizUploads))
    raizUploads = Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads");
raizUploads = Path.GetFullPath(raizUploads);
Directory.CreateDirectory(raizUploads);
builder.Services.AddSingleton(new UploadsOptions { Raiz = raizUploads });
builder.Services.AddSingleton<IFotoStorage, FotoStorage>();

// CORS para os front-ends (web Vite e Expo web). As origens vêm de Cors:Origens.
var origensPermitidas = builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(opcoes =>
    opcoes.AddPolicy("Frontends", politica => politica
        .WithOrigins(origensPermitidas)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Motor de Segurança JWT. A chave NÃO fica em appsettings.json: em produção vem de variável de ambiente
// (Jwt__Key, secret do Container Apps/Key Vault); em Development, de appsettings.Development.json.
var chaveJwt = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(chaveJwt))
    throw new InvalidOperationException("Jwt:Key não configurada. Defina a variável de ambiente Jwt__Key.");
if (chaveJwt.Length < 32)
    throw new InvalidOperationException("Jwt:Key deve ter pelo menos 32 caracteres.");
var chaveSecreta = Encoding.UTF8.GetBytes(chaveJwt);

// Health check (liveness/readiness): GET /health
builder.Services.AddHealthChecks().AddCheck<BancoHealthCheck>("banco");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(chaveSecreta),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

// Políticas (Policies) baseadas em Claims de perfil
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequerPerfilAdmin", policy => policy.RequireClaim("Perfil", "Administrativo"));
    options.AddPolicy("RequerPerfilComum", policy => policy.RequireClaim("Perfil", "Comum"));
    options.AddPolicy("RequerPerfilTecnico", policy => policy.RequireClaim("Perfil", "Tecnico"));
});

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Cole apenas o token JWT (o prefixo 'Bearer' é adicionado automaticamente)."
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Aplica as migrations ao subir. Com Database:RecriarAoIniciar=true (só Development), o banco é
// apagado e recriado a cada execução, voltando à massa inicial do roteiro de testes.
using (var escopo = app.Services.CreateScope())
{
    var contexto = escopo.ServiceProvider.GetRequiredService<ManutencaoDbContext>();
    if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:RecriarAoIniciar"))
        contexto.Database.EnsureDeleted();
    contexto.Database.Migrate();
}

// Configure the HTTP request pipeline.
// Swagger: sempre em Development; na nuvem só se Swagger__Habilitar=true (útil para a demonstração)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Habilitar"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Frontends");

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(raizUploads),
    RequestPath = FotoStorage.RequestPath
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
