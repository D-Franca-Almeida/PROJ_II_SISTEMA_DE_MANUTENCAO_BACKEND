using System.Text.RegularExpressions;
using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;

namespace CPTM.Manutencao.Api.Features.Retirada.Commands
{
    public interface IAutorizarRetiradaHandler
    {
        void Executar(int id, AutorizarRetiradaRequest? requisicao, string matriculaLogada, string perfilLogado);
    }

    // US-24: AGUARDANDO RETIRADA -> RETIRADA AUTORIZADA, pelo Comum dono da SSL ou pelo Admin
    public partial class AutorizarRetiradaHandler : IAutorizarRetiradaHandler
    {
        public const int TamanhoMaximoNome = 150;
        public const int TamanhoMaximoDocumento = 50;

        private readonly ISolicitacaoRepository _repositorio;
        private readonly IWorkflowService _workflow;

        public AutorizarRetiradaHandler(ISolicitacaoRepository repositorio, IWorkflowService workflow)
        {
            _repositorio = repositorio;
            _workflow = workflow;
        }

        public void Executar(int id, AutorizarRetiradaRequest? requisicao, string matriculaLogada, string perfilLogado)
        {
            var solicitacao = Carregar(id);
            ValidarPermissao(solicitacao, matriculaLogada, perfilLogado);
            ValidarStatus(solicitacao);
            var nome = ValidarNome(requisicao?.Nome);
            var documento = ValidarDocumento(requisicao?.Documento);

            solicitacao.AutorizadoNome = nome;
            solicitacao.AutorizadoDocumento = documento;
            // O documento fica só na SSL; o histórico registra apenas o nome
            _workflow.Transicionar(solicitacao, StatusSolicitacao.RetiradaAutorizada, matriculaLogada, $"Retirada autorizada para {nome}.");
        }

        private Solicitacao Carregar(int id) =>
            _repositorio.ObterPorId(id) ?? throw new SolicitacaoNaoEncontradaException(id);

        private static void ValidarPermissao(Solicitacao solicitacao, string matriculaLogada, string perfilLogado)
        {
            if (perfilLogado == "Administrativo")
                return;
            if (perfilLogado == "Comum" && solicitacao.MatriculaSolicitante == matriculaLogada)
                return;
            throw new UnauthorizedAccessException("Somente o solicitante ou o Administrativo pode autorizar a retirada.");
        }

        private static void ValidarStatus(Solicitacao solicitacao)
        {
            if (solicitacao.Status != StatusSolicitacao.AguardandoRetirada)
                throw new RegraDeNegocioException(
                    $"Transição inválida: só é possível autorizar a retirada de uma solicitação AGUARDANDO RETIRADA (status atual: {solicitacao.Status}).");
        }

        private static string ValidarNome(string? nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new RegraDeNegocioException("O nome de quem vai retirar é obrigatório.");
            var texto = nome.Trim();
            if (texto.Length > TamanhoMaximoNome)
                throw new RegraDeNegocioException($"O nome deve ter no máximo {TamanhoMaximoNome} caracteres.");
            return texto;
        }

        private static string ValidarDocumento(string? documento)
        {
            if (string.IsNullOrWhiteSpace(documento))
                throw new RegraDeNegocioException("O documento (CPF ou matrícula) de quem vai retirar é obrigatório.");
            var texto = documento.Trim();
            if (texto.Length > TamanhoMaximoDocumento)
                throw new RegraDeNegocioException($"O documento deve ter no máximo {TamanhoMaximoDocumento} caracteres.");
            if (!FormatoDocumento().IsMatch(texto))
                throw new RegraDeNegocioException("O documento deve ser um CPF ou uma matrícula (letras, números, ponto, hífen ou barra).");
            return texto;
        }

        [GeneratedRegex(@"^[./-]*[A-Za-z0-9][A-Za-z0-9./-]*$")]
        private static partial Regex FormatoDocumento();
    }
}
