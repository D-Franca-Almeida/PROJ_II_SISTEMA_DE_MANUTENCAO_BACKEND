namespace CPTM.Manutencao.Api.Models
{
    // Estados possíveis de uma Solicitação de Serviço Local (SSL)
    public static class StatusSolicitacao
    {
        public const string Solicitada = "SOLICITADA";
        public const string Reenviada = "REENVIADA";
        public const string Aprovada = "APROVADA";
        public const string Devolvida = "DEVOLVIDA";
        public const string Cancelada = "CANCELADA";
        public const string EmManutencao = "EM MANUTENCAO";
        public const string Concluida = "CONCLUIDA";
    }
}
