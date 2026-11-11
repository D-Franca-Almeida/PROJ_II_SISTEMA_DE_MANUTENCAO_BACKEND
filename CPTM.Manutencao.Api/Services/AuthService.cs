using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    public interface IAuthService
    {
        string? RealizarLogin(string matricula, string senha);
    }

    public class AuthService : IAuthService
    {
        private static readonly Usuario UsuarioFantasma = new() { Matricula = string.Empty };

        private readonly IConfiguration _configuration;
        private readonly ManutencaoDbContext _db;
        private readonly IPasswordHasher<Usuario> _hasher;

        public AuthService(IConfiguration configuration, ManutencaoDbContext db, IPasswordHasher<Usuario> hasher)
        {
            _configuration = configuration;
            _db = db;
            _hasher = hasher;
        }

        public string? RealizarLogin(string matricula, string senha)
        {
            if (string.IsNullOrWhiteSpace(matricula) || string.IsNullOrEmpty(senha)) return null;

            var matriculaLimpa = matricula.Trim();
            var usuario = _db.Usuarios.AsNoTracking().FirstOrDefault(u => u.Matricula == matriculaLimpa);

            // Matrícula inexistente também executa um hash, para não distinguir os casos pelo tempo de resposta.
            var resultado = _hasher.VerifyHashedPassword(
                usuario ?? UsuarioFantasma,
                usuario?.SenhaHash ?? _hasher.HashPassword(UsuarioFantasma, senha),
                senha);

            if (usuario == null) return null;
            if (resultado == PasswordVerificationResult.Failed) return null;

            return GerarTokenJwt(usuario);
        }

        private string GerarTokenJwt(Usuario usuario)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var chave = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key não configurada.");
            var key = Encoding.UTF8.GetBytes(chave);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("Matricula", usuario.Matricula),
                    new Claim("Nome", usuario.Nome),
                    new Claim("Perfil", usuario.Perfil)
                }),
                Expires = DateTime.UtcNow.AddHours(8),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
