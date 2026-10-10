namespace CPTM.Manutencao.Api.Models
{
    public class Usuario
    {
        public string Matricula { get; set; } = string.Empty;
        public string Senha { get; set; } = string.Empty;
        public string Perfil { get; set; } = string.Empty; // "Comum", "Administrativo" ou "Tecnico"
    }
}
