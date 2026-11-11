namespace CPTM.Manutencao.Api.Models
{
    // Estados possíveis de uma Solicitação de Serviço Local (SSL)
    public static class StatusSolicitacao
    {
        public const string Solicitada = "SOLICITADA";
        public const string Reenviada = "REENVIADA";
        public const string Aprovada = "APROVADA";
        public const string Recebida = "RECEBIDA";
        public const string Devolvida = "DEVOLVIDA";
        public const string Cancelada = "CANCELADA";
        public const string EmManutencao = "EM MANUTENCAO";
        public const string Concluida = "CONCLUIDA";
        public const string NaoExecutada = "NAO EXECUTADA";
        public const string Expirada = "EXPIRADA";

        public static readonly string[] Todos = { Solicitada, Reenviada, Aprovada, Recebida, Devolvida, Cancelada, EmManutencao, Concluida, NaoExecutada, Expirada };
    }
}
