using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    // 1. A Interface (O Contrato)
    public interface IAuthService
    {
        string? RealizarLogin(string matricula, string senha);
    }

    // 2. A Implementação (A Regra de Negócio Real)
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _configuration;

        public AuthService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string? RealizarLogin(string matricula, string senha)
        {
            // MOCK: Na Sprint 2, esta camada vai chamar a pasta Data (Oracle)
            Usuario? usuarioValido = null;

            if (matricula == "admin123" && senha == "senha")
                usuarioValido = new Usuario { Matricula = "admin123", Perfil = "Administrativo" };
            else if (matricula == "tec123" && senha == "senha")
                usuarioValido = new Usuario { Matricula = "tec123", Perfil = "Tecnico" };
            else if (matricula == "comum123" && senha == "senha")
                usuarioValido = new Usuario { Matricula = "comum123", Perfil = "Comum" };

            if (usuarioValido == null) return null;

            return GerarTokenJwt(usuarioValido);
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
                    new Claim("Perfil", usuario.Perfil) // <--- O RBAC NASCE AQUI!
                }),
                Expires = DateTime.UtcNow.AddHours(8),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
    }
}
