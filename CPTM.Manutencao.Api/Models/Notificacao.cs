namespace CPTM.Manutencao.Api.Models
{
    // US-25: aviso in-app para uma matrícula ou para todos de um perfil (ex.: "Administrativo")
    public class Notificacao
    {
        public const string PerfilAdministrativo = "Administrativo";

        public int Id { get; set; }
        public string? MatriculaDestino { get; set; }
        public string? PerfilDestino { get; set; }
        public int SolicitacaoId { get; set; }
        public Solicitacao? Solicitacao { get; set; }
        public string Mensagem { get; set; } = string.Empty;
        public bool Lida { get; set; }
        public DateTime Data { get; set; }
        public DateTime? DataLeitura { get; set; }

        public bool EhDestinatario(string matricula, string perfil) =>
            MatriculaDestino is not null ? MatriculaDestino == matricula : PerfilDestino == perfil;
    }
}
