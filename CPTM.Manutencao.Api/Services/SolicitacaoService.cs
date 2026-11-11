using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Services
{
    public interface ISolicitacaoService
    {
        Task<SolicitacaoDto> CriarAsync(CriarSolicitacaoRequest requisicao, string matricula);

        List<SolicitacaoDto> Listar(string? status);

        List<SolicitacaoDto> ListarDoSolicitante(string matricula);

        SolicitacaoDetalheDto ObterDetalhe(int id, string matricula, string perfil);
    }

    public class SolicitacaoService : ISolicitacaoService
    {
        public const long TamanhoMaximoFoto = 5 * 1024 * 1024;
        private const int TamanhoMaximoDescricao = 500;

        public const long TamanhoMaximoRequisicao = TamanhoMaximoFoto + 64 * 1024;

        private static readonly byte[] AssinaturaJpeg = { 0xFF, 0xD8, 0xFF };
        private static readonly byte[] AssinaturaPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        private static readonly Dictionary<string, string[]> TiposPermitidos = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = new[] { "image/jpeg" },
            [".jpeg"] = new[] { "image/jpeg" },
            [".png"] = new[] { "image/png" }
        };

        private readonly IWorkflowService _workflow;
        private readonly ICatalogoRepository _catalogo;
        private readonly IFotoStorage _fotos;
        private readonly ISolicitacaoRepository _solicitacoes;

        public SolicitacaoService(IWorkflowService workflow, ICatalogoRepository catalogo, IFotoStorage fotos, ISolicitacaoRepository solicitacoes)
        {
            _solicitacoes = solicitacoes;
            _workflow = workflow;
            _catalogo = catalogo;
            _fotos = fotos;
        }

        public async Task<SolicitacaoDto> CriarAsync(CriarSolicitacaoRequest requisicao, string matricula)
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
            if (!await ConteudoCorrespondeAsync(foto, foto.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? AssinaturaPng : AssinaturaJpeg))
                throw new RegraDeNegocioException("O conteúdo da foto não é uma imagem JPG ou PNG válida.");

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

                return new SolicitacaoDto(
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

        public List<SolicitacaoDto> Listar(string? status)
        {
            string? normalizado = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                var pedido = status.Trim().Replace('_', ' ');
                normalizado = StatusSolicitacao.Todos.FirstOrDefault(s => s.Equals(pedido, StringComparison.OrdinalIgnoreCase))
                    ?? throw new RegraDeNegocioException(
                        $"Status inválido '{status}'. Valores válidos: {string.Join(", ", StatusSolicitacao.Todos)}.");
            }
            return _solicitacoes.Listar(normalizado, null);
        }

        public List<SolicitacaoDto> ListarDoSolicitante(string matricula) => _solicitacoes.Listar(null, matricula);

        public SolicitacaoDetalheDto ObterDetalhe(int id, string matricula, string perfil)
        {
            var detalhe = _solicitacoes.ObterDetalhe(id) ?? throw new SolicitacaoNaoEncontradaException(id);
            var veQualquer = perfil is "Administrativo" or "Tecnico";
            if (!veQualquer && detalhe.MatriculaSolicitante != matricula)
                throw new UnauthorizedAccessException();
            return detalhe;
        }

        private static async Task<bool> ConteudoCorrespondeAsync(IFormFile foto, byte[] assinatura)
        {
            var cabecalho = new byte[assinatura.Length];
            await using var stream = foto.OpenReadStream();
            var lidos = 0;
            while (lidos < cabecalho.Length)
            {
                var n = await stream.ReadAsync(cabecalho.AsMemory(lidos));
                if (n == 0) return false;
                lidos += n;
            }
            return cabecalho.AsSpan().SequenceEqual(assinatura);
        }
    }
}
