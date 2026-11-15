using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    // US-25: textos das notificações. Nunca incluir AutorizadoDocumento nem IdentificacaoRetirada.
    public static class TextosDeNotificacao
    {
        private static readonly Dictionary<string, Func<Solicitacao, string>> ParaSolicitante = new()
        {
            [StatusSolicitacao.Aprovada] = s => $"Sua SSL {s.Id} foi aprovada. Leve o equipamento à oficina.",
            [StatusSolicitacao.Devolvida] = s => $"Sua SSL {s.Id} foi devolvida para correção. Veja o motivo e reenvie.",
            [StatusSolicitacao.Cancelada] = s => $"Sua SSL {s.Id} foi cancelada.",
            [StatusSolicitacao.Expirada] = s => $"Sua SSL {s.Id} expirou por falta de ação no prazo.",
            [StatusSolicitacao.Recebida] = s => $"O equipamento da SSL {s.Id} foi recebido na oficina (SSM {s.NumeroSsm}).",
            [StatusSolicitacao.EmManutencao] = s => $"O equipamento da SSL {s.Id} entrou em manutenção.",
            [StatusSolicitacao.Concluida] = s => $"A manutenção da SSL {s.Id} foi concluída. Aguarde a liberação para retirada.",
            [StatusSolicitacao.NaoExecutada] = s => $"A manutenção da SSL {s.Id} não pôde ser executada. Aguarde a definição do destino.",
            [StatusSolicitacao.AguardandoRetirada] = s => $"O equipamento da SSL {s.Id} está pronto para retirada.",
            [StatusSolicitacao.SemReparo] = s => $"O equipamento da SSL {s.Id} não tem reparo. Avise o Administrativo se quiser recebê-lo de volta.",
            [StatusSolicitacao.Inutilizado] = s => $"O equipamento da SSL {s.Id} foi inutilizado (descarte).",
            [StatusSolicitacao.RetiradaAutorizada] = s => $"A retirada do equipamento da SSL {s.Id} foi autorizada para {s.AutorizadoNome}.",
            [StatusSolicitacao.Retirado] = s => $"O equipamento da SSL {s.Id} foi retirado. Ciclo encerrado."
        };

        private static readonly Dictionary<string, Func<Solicitacao, string>> ParaAdministrativo = new()
        {
            [StatusSolicitacao.Concluida] = s => $"A OSM da SSL {s.Id} foi concluída. Defina o destino do equipamento.",
            [StatusSolicitacao.NaoExecutada] = s => $"A OSM da SSL {s.Id} não foi executada. Defina o destino do equipamento."
        };

        public static string? DoSolicitante(Solicitacao solicitacao, string novoStatus) =>
            ParaSolicitante.TryGetValue(novoStatus, out var texto) ? texto(solicitacao) : null;

        public static string? DoAdministrativo(Solicitacao solicitacao, string novoStatus) =>
            ParaAdministrativo.TryGetValue(novoStatus, out var texto) ? texto(solicitacao) : null;

        public static string PedidoCriado(PedidoPeca pedido, OrdemServico ordem) =>
            $"Novo pedido de peça na OSM {ordem.Id} (SSL {ordem.SolicitacaoId}): {pedido.Item} x{pedido.Quantidade}.";

        public static string PedidoRecebido(PedidoPeca pedido, OrdemServico ordem) =>
            $"A peça do pedido {pedido.Id} chegou: {pedido.Item} x{pedido.Quantidade} (OSM {ordem.Id}, SSL {ordem.SolicitacaoId}).";
    }
}
