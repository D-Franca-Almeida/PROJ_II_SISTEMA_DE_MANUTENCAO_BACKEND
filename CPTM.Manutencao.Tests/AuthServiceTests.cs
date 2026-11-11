using System.IdentityModel.Tokens.Jwt;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CPTM.Manutencao.Tests;

public sealed class AuthServiceTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly PasswordHasher<Usuario> _hasher = new();
    private readonly AuthService _servico;

    public AuthServiceTests()
    {
        _conexao.Open();
        var opcoes = new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options;
        _db = new ManutencaoDbContext(opcoes);
        _db.Database.EnsureCreated();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "chave-de-teste-com-mais-de-32-caracteres-123456" })
            .Build();
        _servico = new AuthService(config, _db, _hasher);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
    }

    [Theory]
    [InlineData("admin123", "Administrativo")]
    [InlineData("tec123", "Tecnico")]
    [InlineData("comum123", "Comum")]
    public void Login_ok_retorna_token_com_matricula_perfil_e_nome(string matricula, string perfil)
    {
        var token = _servico.RealizarLogin(matricula, "senha");

        Assert.NotNull(token);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(matricula, jwt.Claims.First(c => c.Type == "Matricula").Value);
        Assert.Equal(perfil, jwt.Claims.First(c => c.Type == "Perfil").Value);
        Assert.False(string.IsNullOrEmpty(jwt.Claims.First(c => c.Type == "Nome").Value));
    }

    [Fact]
    public void Senha_errada_retorna_nulo() => Assert.Null(_servico.RealizarLogin("admin123", "errada"));

    [Fact]
    public void Matricula_inexistente_retorna_nulo() => Assert.Null(_servico.RealizarLogin("naoexiste", "senha"));

    [Theory]
    [InlineData("", "senha")]
    [InlineData("admin123", "")]
    public void Campos_vazios_retornam_nulo(string matricula, string senha) =>
        Assert.Null(_servico.RealizarLogin(matricula, senha));

    [Fact]
    public void Senha_e_armazenada_como_hash_verificavel()
    {
        foreach (var u in _db.Usuarios.AsNoTracking().ToList())
        {
            Assert.NotEqual("senha", u.SenhaHash);
            Assert.DoesNotContain("senha", u.SenhaHash, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(u, u.SenhaHash, "senha"));
        }
    }

    [Fact]
    public void Seed_tem_tres_usuarios_com_perfis_esperados()
    {
        var perfis = _db.Usuarios.AsNoTracking().ToDictionary(u => u.Matricula, u => u.Perfil);
        Assert.Equal(3, perfis.Count);
        Assert.Equal("Administrativo", perfis["admin123"]);
        Assert.Equal("Tecnico", perfis["tec123"]);
        Assert.Equal("Comum", perfis["comum123"]);
    }
    [Fact]
    public void Matricula_inexistente_faz_o_mesmo_trabalho_de_hash_que_senha_errada()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "chave-de-teste-com-mais-de-32-caracteres-123456" })
            .Build();
        var contador = new HasherContador();
        var servico = new AuthService(config, _db, contador);
        servico.RealizarLogin("aquecimento", "x");

        contador.Zerar();
        servico.RealizarLogin("admin123", "errada");
        var (hashesSenhaErrada, verificacoesSenhaErrada) = (contador.Hashes, contador.Verificacoes);

        contador.Zerar();
        servico.RealizarLogin("naoexiste", "errada");

        Assert.Equal(hashesSenhaErrada, contador.Hashes);
        Assert.Equal(verificacoesSenhaErrada, contador.Verificacoes);
        Assert.Equal(1, contador.Verificacoes);
    }

    private sealed class HasherContador : IPasswordHasher<Usuario>
    {
        private readonly PasswordHasher<Usuario> _real = new();
        public int Hashes { get; private set; }
        public int Verificacoes { get; private set; }

        public void Zerar() => (Hashes, Verificacoes) = (0, 0);

        public string HashPassword(Usuario user, string password)
        {
            Hashes++;
            return _real.HashPassword(user, password);
        }

        public PasswordVerificationResult VerifyHashedPassword(Usuario user, string hashedPassword, string providedPassword)
        {
            Verificacoes++;
            return _real.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
