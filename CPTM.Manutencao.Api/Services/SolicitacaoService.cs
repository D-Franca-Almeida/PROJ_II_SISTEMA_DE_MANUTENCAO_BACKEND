using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    public interface ISolicitacaoService
    {
        Task<SolicitacaoCriadaDto> CriarAsync(CriarSolicitacaoRequest requisicao, string matricula);
    }

    public class SolicitacaoService : ISolicitacaoService
    {
        public const long TamanhoMaximoFoto = 5 * 1024 * 1024;
        private const int TamanhoMaximoDescricao = 500;

        private static readonly Dictionary<string, string[]> TiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new[] { "image/jpeg" },
            [".jpeg"] = new[] { "image/jpeg" },
            [".png"] = new[] { "image/png" }
        };

        private readonly IWorkflowService _workflow;
        private readonly ICatalogoRepository _catalogo;
        private readonly IFotoStorage _fotos;

        public SolicitacaoService(IWorkflowService workflow, ICatalogoRepository catalogo, IFotoStorage fotos)
        {
            _workflow = workflow;
            _catalogo = catalogo;
            _fotos = fotos;
        }

        public async Task<SolicitacaoCriadaDto> CriarAsync(CriarSolicitacaoRequest requisicao, string matricula)
        {
            var descricao = requisicao.Descricao?.Trim();
            if (string.IsNullOrEmpty(descricao))
                throw new RegraDeNegocioException("A descrição é obrigatória.");
            if (descricao.Length > TamanhoMaximoDescricao)
                throw new RegraDeNegocioException($"A descrição deve ter no máximo {TamanhoMaximoDescricao} caracteres.");

            if (requisicao.EquipamentoId is null)
                throw new RegraDeNegocioException("O equipamento é obrigatório.");
            var equipamento = _catalogo.ObterEquipamento(requisicao.EquipamentoId.Value)
                ?? throw new RegraDeNegocioException($"Equipamento {requisicao.EquipamentoId} não encontrado.");

            var foto = requisicao.Foto;
            if (foto is null || foto.Length == 0)
                throw new RegraDeNegocioException("A foto é obrigatória.");
            if (foto.Length > TamanhoMaximoFoto)
                throw new RegraDeNegocioException("A foto deve ter no máximo 5 MB.");

            var extensao = Path.GetExtension(foto.FileName);
            if (!TiposPermitidos.TryGetValue(extensao, out var contentTypes) ||
                !contentTypes.Contains(foto.ContentType, StringComparer.OrdinalIgnoreCase))
                throw new RegraDeNegocioException("A foto deve ser JPG, JPEG ou PNG.");

            var url = await _fotos.SalvarAsync(foto, extensao.ToLowerInvariant());
            try
            {
                var solicitacao = new Solicitacao
                {
                    Descricao = descricao,
                    EquipamentoId = equipamento.Id,
                    FotoUrl = url,
                    DataAbertura = DateTime.UtcNow,
                    MatriculaSolicitante = matricula
                };
                _workflow.Transicionar(solicitacao, StatusSolicitacao.Solicitada, matricula);

                return new SolicitacaoCriadaDto(
                    solicitacao.Id,
                    solicitacao.Status,
                    solicitacao.Descricao,
                    url,
                    solicitacao.DataAbertura,
                    solicitacao.MatriculaSolicitante,
                    new EquipamentoDto(equipamento.Id, equipamento.EstacaoId, equipamento.Descricao, equipamento.Patrimonio),
                    new EstacaoDto(equipamento.EstacaoId, equipamento.EstacaoNome, equipamento.Linha));
            }
            catch
            {
                _fotos.Excluir(url);
                throw;
            }
        }
    }
}
