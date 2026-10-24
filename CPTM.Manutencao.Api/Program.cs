using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using CPTM.Manutencao.Api.Services;
using CPTM.Manutencao.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Injeção de Dependência do Serviço de autenticação
builder.Services.AddScoped<IAuthService, AuthService>();

// Motor de Triagem (Workflow). O repositório mock é Singleton para manter os dados em memória.
builder.Services.AddSingleton<ISolicitacaoRepository, SolicitacaoRepositoryMock>();
builder.Services.AddScoped<IWorkflowService, WorkflowService>();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
