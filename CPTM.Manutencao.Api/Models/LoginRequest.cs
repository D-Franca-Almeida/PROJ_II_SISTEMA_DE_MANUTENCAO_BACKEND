namespace CPTM.Manutencao.Api.Models
{
    // Corpo da requisição de login (o perfil não é enviado pelo cliente)
    public class LoginRequest
    {
        public string Matricula { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
    }
}
