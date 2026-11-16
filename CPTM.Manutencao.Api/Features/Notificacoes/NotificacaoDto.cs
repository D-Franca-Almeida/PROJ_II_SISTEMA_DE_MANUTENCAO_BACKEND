using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Features.Notificacoes
{
    public record NotificacaoDto(int Id, int SolicitacaoId, string Mensagem, bool Lida, DateTime Data, DateTime? DataLeitura)
    {
        public static NotificacaoDto De(Notificacao n) => new(n.Id, n.SolicitacaoId, n.Mensagem, n.Lida, n.Data, n.DataLeitura);
    }
}
