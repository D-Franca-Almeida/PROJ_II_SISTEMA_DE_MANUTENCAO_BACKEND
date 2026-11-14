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
        private static string? _hashFantasma;

        private readonly IConfiguration _configuration;
        private readonly ManutencaoDbContext _db;
        private readonly IPasswordHasher<Usuario> _hasher;
        private readonly INotificacaoQueue? _fila;

        public AuthService(IConfiguration configuration, ManutencaoDbContext db, IPasswordHasher<Usuario> hasher, INotificacaoQueue? fila = null)
        {
            _configuration = configuration;
            _db = db;
            _hasher = hasher;
            _fila = fila;
        }

        public string? RealizarLogin(string matricula, string senha)
        {
            if (string.IsNullOrWhiteSpace(matricula) || string.IsNullOrEmpty(senha)) return null;

            var matriculaLimpa = matricula.Trim();
            var usuario = _db.Usuarios.AsNoTracking().FirstOrDefault(u => u.Matricula == matriculaLimpa);

            // Matrícula inexistente executa a mesma verificação (um PBKDF2) contra um hash fictício, para não distinguir os casos pelo tempo de resposta.
            var hashFantasma = _hashFantasma ??= _hasher.HashPassword(UsuarioFantasma, Guid.NewGuid().ToString());
            var resultado = _hasher.VerifyHashedPassword(
                usuario ?? UsuarioFantasma,
                usuario?.SenhaHash ?? hashFantasma,
                senha);

            if (usuario == null || resultado == PasswordVerificationResult.Failed)
            {
                _fila?.Publicar(new EventoSistemaMensagem(
                    "LOGIN_FALHA", matriculaLimpa, null, null, null,
                    "Tentativa de login com credenciais inválidas.", DateTime.UtcNow));
                return null;
            }

            _fila?.Publicar(new EventoSistemaMensagem(
                "LOGIN_SUCESSO", usuario.Matricula, null, null, null,
                $"Login autenticado (Perfil: {usuario.Perfil}).", DateTime.UtcNow));

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
