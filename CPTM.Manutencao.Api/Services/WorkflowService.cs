using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Api.Services
{
    // Solicitação inexistente -> o Controller responde 404
    public class SolicitacaoNaoEncontradaException : Exception
    {
        public SolicitacaoNaoEncontradaException(int id)
            : base($"Solicitação {id} não encontrada.") { }
    }

    // Violação de regra de negócio (transição inválida, motivo ausente) -> 400
    public class RegraDeNegocioException : Exception
    {
        public RegraDeNegocioException(string message) : base(message) { }
    }

    // Outra operação mudou o status da SSL entre a leitura e a gravação (ex.: dois técnicos assumindo ao mesmo tempo) -> 409
    public class ConflitoDeConcorrenciaException : Exception
    {
        public ConflitoDeConcorrenciaException(int id, Exception? interna = null)
            : base($"A solicitação {id} foi alterada por outra operação ao mesmo tempo. Atualize a lista e tente novamente.", interna) { }

        public ConflitoDeConcorrenciaException(string mensagem, Exception? interna = null) : base(mensagem, interna) { }
    }

    public class OrdemServicoNaoEncontradaException : Exception
    {
        public OrdemServicoNaoEncontradaException(int id)
            : base($"Ordem de serviço {id} não encontrada.") { }
    }

    public class PedidoPecaNaoEncontradoException : Exception
    {
        public PedidoPecaNaoEncontradoException(int id)
            : base($"Pedido de peça {id} não encontrado.") { }
    }

    // US-23: concluir com pedido SOLICITADO/ENCAMINHADO -> 400 listando os pendentes
    public class ConclusaoBloqueadaException : RegraDeNegocioException
    {
        public ConclusaoBloqueadaException(int ordemServicoId, IReadOnlyList<PedidoPendenteDto> pendentes)
            : base($"Não é possível concluir a OSM {ordemServicoId}: há {pendentes.Count} pedido(s) de peça pendente(s) " +
                   $"({string.Join("; ", pendentes.Select(p => $"#{p.Id} {p.Item} x{p.Quantidade} - {p.Status}"))}). " +
                   "Aguarde a chegada ou cancele os pedidos.")
        {
            Pendentes = pendentes;
        }

        public IReadOnlyList<PedidoPendenteDto> Pendentes { get; }
    }

    public record EncerramentoOsm(OrdemServico Ordem, IReadOnlyList<int> PedidosCancelados);

    public record RecebimentoResultado(int SolicitacaoId, string NumeroSsm, DateTime DataRecebimento);

    // 1. A Interface (O Contrato)
    public interface IWorkflowService
    {
        void AprovarTriagem(int id, string matriculaLogada);
        void DevolverTriagem(int id, string motivo, string matriculaLogada);
        void CancelarTriagem(int id, string matriculaLogada, string perfilLogado);
        // US-07: RECEBIDA -> EM MANUTENCAO (Técnico); abre a OSM na mesma gravação
        OrdemServico IniciarManutencao(int id, string matriculaTecnico);

        // US-09: EM MANUTENCAO -> CONCLUIDA (laudo) / NAO EXECUTADA (motivo), só pelo técnico dono da OSM
        // US-23: concluir é bloqueado com pedido pendente; não executar cancela os pendentes na mesma gravação
        EncerramentoOsm ConcluirManutencao(int id, string? laudo, string matriculaTecnico);
        EncerramentoOsm NaoExecutarManutencao(int id, string? motivo, string matriculaTecnico);

        // US-10 (Admin): destino da SSL depois da OSM concluída. CONCLUIDA -> AGUARDANDO RETIRADA | SEM REPARO
        void DisponibilizarParaRetirada(int id, string matriculaAdmin, string? observacao = null);
        void CondenarSemReparo(int id, string matriculaAdmin, string? observacao = null);

        // US-11 (Admin): protocolo de retirada. AGUARDANDO RETIRADA -> RETIRADO, com CPF/matrícula de quem retirou
        void RegistrarRetirada(int id, string matriculaAdmin, string identificacaoRecebedor);

        // US-22: APROVADA -> RECEBIDA (Admin), gera NumeroSsm SSM-AAAA-NNNNNN e DataRecebimento
        RecebimentoResultado ReceberNaOficina(int id, string matriculaAdmin);

        // US-16: ponto único de mudança de status. Grava exatamente 1 linha em HistoricoStatus.
        // Solicitação nova (Id = 0) registra a abertura com StatusAnterior nulo.
        // Transições automáticas usam HistoricoStatus.MatriculaSistema.
        void Transicionar(Solicitacao solicitacao, string novoStatus, string matricula, string? observacao = null);
    }

    // 2. A Implementação: motor de triagem (máquina de estados)
    public class WorkflowService : IWorkflowService
    {
        private readonly ISolicitacaoRepository _repositorio;
        private readonly TimeProvider _relogio;
        private readonly INotificacaoQueue? _fila;

        public WorkflowService(ISolicitacaoRepository repositorio, TimeProvider? relogio = null, INotificacaoQueue? fila = null)
        {
            _repositorio = repositorio;
            _relogio = relogio ?? TimeProvider.System;
            _fila = fila;
        }

        public void AprovarTriagem(int id, string matriculaLogada)
        {
            var solicitacao = ObterParaTriagem(id, "aprovar");

            Transicionar(solicitacao, StatusSolicitacao.Aprovada, matriculaLogada);
        }

        public void DevolverTriagem(int id, string motivo, string matriculaLogada)
        {
            var solicitacao = ObterParaTriagem(id, "devolver");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new RegraDeNegocioException("O motivo da devolução é obrigatório.");

            solicitacao.MotivoDevolucao = motivo.Trim();
            Transicionar(solicitacao, StatusSolicitacao.Devolvida, matriculaLogada, solicitacao.MotivoDevolucao);
        }

        public void CancelarTriagem(int id, string matriculaLogada, string perfilLogado)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            // REGRA 1: O Usuário Comum SÓ pode cancelar a PRÓPRIA solicitação.
            // (O Administrativo pode cancelar a de qualquer um, por isso não cai neste IF)
            if (perfilLogado == "Comum" && solicitacao.MatriculaSolicitante != matriculaLogada)
                throw new UnauthorizedAccessException("Você só tem permissão para cancelar as suas próprias solicitações.");

            // REGRA 2: Bloquear cancelamento tardio (US-19: permitido até APROVADA, antes de o equipamento chegar à oficina)
            if (solicitacao.Status != StatusSolicitacao.Solicitada &&
                solicitacao.Status != StatusSolicitacao.Reenviada &&
                solicitacao.Status != StatusSolicitacao.Devolvida &&
                solicitacao.Status != StatusSolicitacao.Aprovada)
                throw new RegraDeNegocioException(
                    $"Não é possível cancelar uma solicitação com status {solicitacao.Status}.");

            Transicionar(solicitacao, StatusSolicitacao.Cancelada, matriculaLogada);
        }

        // US-07: o técnico assume uma SSL RECEBIDA na oficina. RECEBIDA -> EM MANUTENCAO + OSM (uma única gravação)
        public OrdemServico IniciarManutencao(int id, string matriculaTecnico)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.Recebida)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível iniciar a manutenção de uma solicitação RECEBIDA na oficina (status atual: {solicitacao.Status}).");

            if (string.IsNullOrWhiteSpace(matriculaTecnico))
                throw new RegraDeNegocioException("A identificação do técnico responsável é obrigatória.");

            var tecnico = matriculaTecnico.Trim();
            var agora = _relogio.GetUtcNow().UtcDateTime;
            var ordem = new OrdemServico { MatriculaTecnico = tecnico, DataAbertura = agora };
            solicitacao.MatriculaTecnico = tecnico;
            solicitacao.DataInicioManutencao = agora;
            solicitacao.OrdemServico = ordem;
            try
            {
                Transicionar(solicitacao, StatusSolicitacao.EmManutencao, tecnico);
            }
            catch (DbUpdateException ex)
            {
                throw new ConflitoDeConcorrenciaException(id, ex);
            }
            return ordem;
        }

        public const int TamanhoMaximoLaudo = 2000;
        public const int TamanhoMaximoMotivoNaoExecucao = 500;

        public const string MotivoCancelamentoAutomatico = "Cancelado automaticamente: OSM não executada.";

        public EncerramentoOsm ConcluirManutencao(int id, string? laudo, string matriculaTecnico)
        {
            var (solicitacao, ordem) = ObterParaEncerrar(id, matriculaTecnico, "concluir");

            var pendentes = ordem.Pedidos
                .Where(p => StatusPedidoPeca.Pendente(p.Status))
                .OrderBy(p => p.Id)
                .Select(p => new PedidoPendenteDto(p.Id, p.Item, p.Quantidade, p.Status))
                .ToList();
            if (pendentes.Count > 0)
                throw new ConclusaoBloqueadaException(ordem.Id, pendentes);

            if (string.IsNullOrWhiteSpace(laudo))
                throw new RegraDeNegocioException("O laudo técnico é obrigatório para concluir a OSM.");
            var texto = laudo.Trim();
            if (texto.Length > TamanhoMaximoLaudo)
                throw new RegraDeNegocioException($"O laudo deve ter no máximo {TamanhoMaximoLaudo} caracteres.");

            ordem.Laudo = texto;
            ordem.DataConclusao = _relogio.GetUtcNow().UtcDateTime;
            // Um pedido criado entre a leitura e a gravação também bloqueia: a checagem é refeita no banco, na mesma transação
            _repositorio.EmTransacao(() =>
            {
                Transicionar(solicitacao, StatusSolicitacao.Concluida, ordem.MatriculaTecnico, $"OSM {ordem.Id} concluída.");
                var pendentesAgora = _repositorio.ListarPedidosPendentes(ordem.Id);
                if (pendentesAgora.Count > 0)
                    throw new ConclusaoBloqueadaException(ordem.Id, pendentesAgora);
            });
            return new EncerramentoOsm(ordem, Array.Empty<int>());
        }

        public EncerramentoOsm NaoExecutarManutencao(int id, string? motivo, string matriculaTecnico)
        {
            var (solicitacao, ordem) = ObterParaEncerrar(id, matriculaTecnico, "registrar a não execução de");

            if (string.IsNullOrWhiteSpace(motivo))
                throw new RegraDeNegocioException("O motivo da não execução é obrigatório.");
            var texto = motivo.Trim();
            if (texto.Length > TamanhoMaximoMotivoNaoExecucao)
                throw new RegraDeNegocioException($"O motivo deve ter no máximo {TamanhoMaximoMotivoNaoExecucao} caracteres.");

            var agora = _relogio.GetUtcNow().UtcDateTime;
            var cancelados = new List<int>();
            foreach (var pedido in ordem.Pedidos.Where(p => StatusPedidoPeca.Pendente(p.Status)).OrderBy(p => p.Id))
            {
                pedido.Status = StatusPedidoPeca.Cancelado;
                pedido.DataCancelamento = agora;
                pedido.MatriculaCancelamento = ordem.MatriculaTecnico;
                pedido.MotivoCancelamento = MotivoCancelamentoAutomatico;
                cancelados.Add(pedido.Id);
            }

            ordem.MotivoNaoExecucao = texto;
            ordem.DataConclusao = agora;
            // Um pedido criado entre a leitura e a gravação ficaria pendente numa OSM encerrada: desfaz tudo e responde 409
            _repositorio.EmTransacao(() =>
            {
                Transicionar(solicitacao, StatusSolicitacao.NaoExecutada, ordem.MatriculaTecnico, texto);
                if (_repositorio.ListarPedidosPendentes(ordem.Id).Count > 0)
                    throw new ConflitoDeConcorrenciaException(
                        $"A OSM {ordem.Id} recebeu um pedido de peça ao mesmo tempo. Atualize e tente novamente.");
            });
            return new EncerramentoOsm(ordem, cancelados);
        }

        // Dono da OSM primeiro (outro técnico -> 403, qualquer que seja o status), depois o status EM MANUTENCAO (400)
        private (Solicitacao Solicitacao, OrdemServico Ordem) ObterParaEncerrar(int id, string matriculaTecnico, string acao)
        {
            var solicitacao = _repositorio.ObterComOrdemServico(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);
            var ordem = solicitacao.OrdemServico;

            if (ordem is not null && !string.Equals(ordem.MatriculaTecnico, matriculaTecnico?.Trim(), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Somente o técnico responsável pela OSM pode encerrá-la.");

            if (solicitacao.Status != StatusSolicitacao.EmManutencao || ordem is null)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível {acao} uma solicitação EM MANUTENCAO (status atual: {solicitacao.Status}).");

            return (solicitacao, ordem);
        }

        public RecebimentoResultado ReceberNaOficina(int id, string matriculaAdmin)
        {
            lock (TravaSsm)
            {
                for (var tentativa = 1; ; tentativa++)
                {
                    var solicitacao = _repositorio.ObterPorId(id)
                        ?? throw new SolicitacaoNaoEncontradaException(id);

                    if (solicitacao.Status != StatusSolicitacao.Aprovada)
                        throw new RegraDeNegocioException(
                            $"Transição inválida: só é possível receber na oficina uma solicitação APROVADA (status atual: {solicitacao.Status}).");

                    var agora = _relogio.GetUtcNow().UtcDateTime;
                    solicitacao.NumeroSsm = ProximoNumeroSsm(agora.Year);
                    solicitacao.DataRecebimento = agora;
                    try
                    {
                        Transicionar(solicitacao, StatusSolicitacao.Recebida, matriculaAdmin, $"SSM {solicitacao.NumeroSsm} gerada.");
                        return new RecebimentoResultado(solicitacao.Id, solicitacao.NumeroSsm, agora);
                    }
                    catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
                    {
                        if (tentativa >= TentativasSsm)
                            throw new ConflitoDeConcorrenciaException(id, ex);
                    }
                }
            }
        }

        public const int TentativasSsm = 3;
        private static readonly object TravaSsm = new();

        private string ProximoNumeroSsm(int ano)
        {
            var prefixo = $"SSM-{ano}-";
            var ultimo = _repositorio.UltimoNumeroSsm(prefixo);
            var sequencial = ultimo is null ? 1 : int.Parse(ultimo[prefixo.Length..]) + 1;
            if (sequencial > 999999)
                throw new RegraDeNegocioException($"Limite anual de números SSM atingido para {ano}.");
            return $"{prefixo}{sequencial:D6}";
        }

        public void Transicionar(Solicitacao solicitacao, string novoStatus, string matricula, string? observacao = null)
        {
            if (string.IsNullOrWhiteSpace(novoStatus))
                throw new RegraDeNegocioException("O novo status é obrigatório.");
            if (string.IsNullOrWhiteSpace(matricula))
                throw new RegraDeNegocioException("A matrícula responsável pela transição é obrigatória.");
            if (solicitacao.Id != 0 && solicitacao.Status == novoStatus)
                throw new RegraDeNegocioException($"A solicitação já está com status {novoStatus}.");

            var historico = new HistoricoStatus
            {
                StatusAnterior = solicitacao.Id == 0 ? null : solicitacao.Status,
                StatusNovo = novoStatus,
                Matricula = matricula.Trim(),
                Data = _relogio.GetUtcNow().UtcDateTime,
                Observacao = observacao
            };

            solicitacao.Status = novoStatus;
            _repositorio.Salvar(solicitacao, historico);

            _fila?.Publicar(new EventoSistemaMensagem(
                "MUDANCA_STATUS",
                historico.Matricula,
                solicitacao.Id,
                historico.StatusAnterior,
                historico.StatusNovo,
                historico.Observacao,
                historico.Data));
        }

        public const int TamanhoMaximoObservacao = 500;
        public const int TamanhoMaximoIdentificacao = 100;

        // US-10 (caminho 1): CONCLUIDA -> AGUARDANDO RETIRADA
        public void DisponibilizarParaRetirada(int id, string matriculaAdmin, string? observacao = null) =>
            DefinirDestino(id, StatusSolicitacao.AguardandoRetirada, matriculaAdmin, observacao);

        // US-10 (caminho 2): CONCLUIDA -> SEM REPARO (fim do fluxo operacional: não há retirada)
        public void CondenarSemReparo(int id, string matriculaAdmin, string? observacao = null) =>
            DefinirDestino(id, StatusSolicitacao.SemReparo, matriculaAdmin, observacao);

        private void DefinirDestino(int id, string novoStatus, string matriculaAdmin, string? observacao)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.Concluida)
                throw new RegraDeNegocioException(
                    $"Transição inválida: a solicitação precisa estar CONCLUIDA para ir para {novoStatus} (status atual: {solicitacao.Status}).");

            var texto = string.IsNullOrWhiteSpace(observacao) ? null : observacao.Trim();
            if (texto is not null && texto.Length > TamanhoMaximoObservacao)
                throw new RegraDeNegocioException($"A observação deve ter no máximo {TamanhoMaximoObservacao} caracteres.");

            solicitacao.ObservacaoDestino = texto;
            Transicionar(solicitacao, novoStatus, matriculaAdmin, texto);
        }

        // US-11: AGUARDANDO RETIRADA -> RETIRADO
        public void RegistrarRetirada(int id, string matriculaAdmin, string identificacaoRecebedor)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.AguardandoRetirada)
                throw new RegraDeNegocioException(
                    $"Transição inválida: apenas equipamentos AGUARDANDO RETIRADA podem ser entregues (status atual: {solicitacao.Status}).");

            if (string.IsNullOrWhiteSpace(identificacaoRecebedor))
                throw new RegraDeNegocioException("O CPF ou matrícula do responsável pela retirada é obrigatório.");
            var identificacao = identificacaoRecebedor.Trim();
            if (identificacao.Length > TamanhoMaximoIdentificacao)
                throw new RegraDeNegocioException($"A identificação do recebedor deve ter no máximo {TamanhoMaximoIdentificacao} caracteres.");

            solicitacao.IdentificacaoRetirada = identificacao;
            solicitacao.DataRetirada = _relogio.GetUtcNow().UtcDateTime;
            // O CPF/matrícula fica só na SSL; o histórico registra apenas o Admin que fez o protocolo
            Transicionar(solicitacao, StatusSolicitacao.Retirado, matriculaAdmin, "Protocolo de retirada registrado.");
        }

        // Busca a solicitação e garante que ela está apta para triagem:
        // só SOLICITADA ou REENVIADA podem sofrer aprovação/devolução/cancelamento.
        private Solicitacao ObterParaTriagem(int id, string acao)
        {
            var solicitacao = _repositorio.ObterPorId(id)
                ?? throw new SolicitacaoNaoEncontradaException(id);

            if (solicitacao.Status != StatusSolicitacao.Solicitada &&
                solicitacao.Status != StatusSolicitacao.Reenviada)
                throw new RegraDeNegocioException(
                    $"Transição inválida: não é possível {acao} uma solicitação com status {solicitacao.Status}.");

            return solicitacao;
        }
    }
}
