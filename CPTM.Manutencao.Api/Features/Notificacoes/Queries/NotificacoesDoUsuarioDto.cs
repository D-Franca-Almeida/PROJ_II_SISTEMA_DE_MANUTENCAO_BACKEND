namespace CPTM.Manutencao.Api.Features.Notificacoes.Queries
{
    // NaoLidas é o contador do sino e não depende do filtro apenasNaoLidas
    public record NotificacoesDoUsuarioDto(int NaoLidas, IReadOnlyList<NotificacaoDto> Notificacoes);
}
