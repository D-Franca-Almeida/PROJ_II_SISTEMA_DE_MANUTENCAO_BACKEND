using CPTM.Manutencao.Api.Data;
using CPTM.Manutencao.Api.Models;
using CPTM.Manutencao.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CPTM.Manutencao.Tests;

public sealed class SolicitacaoServiceTests : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");
    private readonly ManutencaoDbContext _db;
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), $"cptm-ut-{Guid.NewGuid():N}");

    public SolicitacaoServiceTests()
    {
        _conexao.Open();
        _db = new ManutencaoDbContext(new DbContextOptionsBuilder<ManutencaoDbContext>().UseSqlite(_conexao).Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _conexao.Dispose();
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    private SolicitacaoService CriarServico(ISolicitacaoRepository? repositorio = null) =>
        new(new WorkflowService(repositorio ?? new SolicitacaoRepository(_db)),
            new CatalogoRepository(_db),
            new FotoStorage(new UploadsOptions { Raiz = _raiz }),
            repositorio ?? new SolicitacaoRepository(_db));

    private static IFormFile Arquivo(string nome = "f.png", string contentType = "image/png", int tamanho = 10)
    {
        var bytes = new byte[tamanho];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, tamanho, "foto", nome) { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private string[] Fotos() =>
        Directory.Exists(Path.Combine(_raiz, "ssl")) ? Directory.GetFiles(Path.Combine(_raiz, "ssl")) : Array.Empty<string>();

    private sealed class RepositorioQueFalha : ISolicitacaoRepository
    {
        public Solicitacao? ObterPorId(int id) => null;
        public List<SolicitacaoDto> Listar(string? status, string? matriculaSolicitante) => new();
        public SolicitacaoDetalheDto? ObterDetalhe(int id) => null;
        public List<CandidatoExpiracao> ObterCandidatosExpiracao() => new();
        public void Salvar(Solicitacao solicitacao, HistoricoStatus historico) => throw new InvalidOperationException("falha simulada");
    }

    [Fact]
    public async Task Criar_grava_solicitada_com_historico_unico_e_foto()
    {
        var dto = await CriarServico().CriarAsync(
            new CriarSolicitacaoRequest { EquipamentoId = 4, Descricao = "Catraca quebrada", Foto = Arquivo() }, "comum123");

        Assert.Equal(StatusSolicitacao.Solicitada, dto.Status);
        Assert.Equal("Brás", dto.Estacao!.Nome);
        Assert.Equal(DateTimeKind.Utc, dto.DataAbertura.Kind);
        Assert.Single(Fotos());

        var salva = _db.Solicitacoes.AsNoTracking().Single(s => s.Id == dto.Id);
        Assert.Equal(4, salva.EquipamentoId);
        Assert.Equal(dto.FotoUrl, salva.FotoUrl);
        var h = Assert.Single(_db.HistoricoStatus.Where(x => x.SolicitacaoId == dto.Id));
        Assert.Null(h.StatusAnterior);
        Assert.Equal("comum123", h.Matricula);
    }

    [Fact]
    public async Task Falha_ao_salvar_remove_a_foto_gravada()
    {
        var servico = CriarServico(new RepositorioQueFalha());

        await Assert.ThrowsAsync<InvalidOperationException>(() => servico.CriarAsync(
            new CriarSolicitacaoRequest { EquipamentoId = 1, Descricao = "x", Foto = Arquivo() }, "comum123"));

        Assert.Empty(Fotos());
    }

    [Fact]
    public async Task Validacao_falha_antes_de_gravar_a_foto()
    {
        var servico = CriarServico();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CriarAsync(
            new CriarSolicitacaoRequest { EquipamentoId = 999, Descricao = "x", Foto = Arquivo() }, "comum123"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CriarAsync(
            new CriarSolicitacaoRequest { EquipamentoId = 1, Descricao = " ", Foto = Arquivo() }, "comum123"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CriarAsync(
            new CriarSolicitacaoRequest { EquipamentoId = 1, Descricao = "x", Foto = Arquivo("f.bmp", "image/bmp") }, "comum123"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.CriarAsync(
            new CriarSolicitacaoRequest { EquipamentoId = 1, Descricao = "x", Foto = null }, "comum123"));

        Assert.Empty(Fotos());
        Assert.Equal(12, _db.Solicitacoes.Count());
    }

    [Theory]
    [InlineData("Comum")]
    [InlineData("Almoxarifado")]
    [InlineData("")]
    public void Detalhe_de_ssl_alheia_e_negado_para_perfil_que_nao_e_admin_nem_tecnico(string perfil)
    {
        Assert.Throws<UnauthorizedAccessException>(() => CriarServico().ObterDetalhe(4, "comum123", perfil));
    }

    [Fact]
    public void Detalhe_devolve_datas_do_historico_em_utc()
    {
        var detalhe = CriarServico().ObterDetalhe(1, "comum123", "Comum");
        Assert.All(detalhe.Historico, h => Assert.Equal(DateTimeKind.Utc, h.Data.Kind));
    }
}
