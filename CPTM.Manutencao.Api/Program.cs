using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CPTM.Manutencao.Api.Services;
using CPTM.Manutencao.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Injeção de Dependência do Serviço de autenticação
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

// CORS para os front-ends (web Vite e Expo web). As origens vêm de Cors:Origens.
var origensPermitidas = builder.Configuration.GetSection("Cors:Origens").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(opcoes =>
    opcoes.AddPolicy("Frontends", politica => politica
        .WithOrigins(origensPermitidas)
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Motor de Segurança JWT (a chave vem de appsettings: Jwt:Key)
var chaveSecreta = Encoding.UTF8.GetBytes(
    builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key não configurada."));

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
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("Frontends");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
