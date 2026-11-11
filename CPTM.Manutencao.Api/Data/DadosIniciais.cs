using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Massa do roteiro de testes (Testes/Roteiro-Sprints-1-2-3.ps1), antes mantida no repositório mock.
    public static class DadosIniciais
    {
        // Hash PBKDF2 (PasswordHasher<Usuario>) da senha "senha", pré-calculado para o seed ficar estável nas migrations.
        private const string HashSenhaPadrao = "AQAAAAIAAYagAAAAEPG97joPfZDaH+3JpNWPl6yhYANv0qBd26FjONrdxmHnee+lA28Pnrmd00X1NSQpmQ==";

        public static Usuario[] Usuarios() => new[]
        {
            new Usuario { Matricula = "admin123", Nome = "Administrador CPTM", SenhaHash = HashSenhaPadrao, Perfil = "Administrativo" },
            new Usuario { Matricula = "tec123", Nome = "Técnico CPTM", SenhaHash = HashSenhaPadrao, Perfil = "Tecnico" },
            new Usuario { Matricula = "comum123", Nome = "Usuário Comum", SenhaHash = HashSenhaPadrao, Perfil = "Comum" }
        };

        public static Estacao[] Estacoes() => new[]
        {
            new Estacao { Id = 1, Nome = "Luz", Linha = "7-Rubi" },
            new Estacao { Id = 2, Nome = "Brás", Linha = "10-Turquesa" },
            new Estacao { Id = 3, Nome = "Corinthians-Itaquera", Linha = "11-Coral" },
            new Estacao { Id = 4, Nome = "Calmon Viana", Linha = "12-Safira" },
            new Estacao { Id = 5, Nome = "Aeroporto-Guarulhos", Linha = "13-Jade" }
        };

        public static Equipamento[] Equipamentos() => new[]
        {
            Equip(1, 1, "Escada rolante da plataforma 2", "PAT-LUZ-0001"),
            Equip(2, 1, "Catraca de acesso principal", "PAT-LUZ-0002"),
            Equip(3, 1, "Terminal de bilheteria 01", "PAT-LUZ-0003"),
            Equip(4, 2, "Catraca de acesso norte", "PAT-BRS-0001"),
            Equip(5, 2, "Escada rolante da plataforma 1", "PAT-BRS-0002"),
            Equip(6, 2, "Rádio de comunicação do controle da estação", "PAT-BRS-0003"),
            Equip(7, 3, "Terminal de bilheteria 01", "PAT-ITQ-0001"),
            Equip(8, 3, "Catraca de acesso sul", "PAT-ITQ-0002"),
            Equip(9, 3, "Escada rolante da passarela", "PAT-ITQ-0003"),
            Equip(10, 4, "Terminal de bilheteria 01", "PAT-CVN-0001"),
            Equip(11, 4, "Catraca de acesso principal", "PAT-CVN-0002"),
            Equip(12, 4, "Rádio de comunicação da plataforma", "PAT-CVN-0003"),
            Equip(13, 5, "Terminal de bilheteria 01", "PAT-AGU-0001"),
            Equip(14, 5, "Escada rolante do saguão", "PAT-AGU-0002"),
            Equip(15, 5, "Catraca de acesso ao terminal", "PAT-AGU-0003")
        };

        private static Equipamento Equip(int id, int estacaoId, string descricao, string patrimonio) =>
            new() { Id = id, EstacaoId = estacaoId, Descricao = descricao, Patrimonio = patrimonio };

        public static Solicitacao[] Solicitacoes() => new[]
        {
            // IDs 1, 2 e 3: SOLICITADA, pertencentes ao usuário Comum (comum123)
            Nova(1, "Lâmpada queimada na plataforma 2", StatusSolicitacao.Solicitada, 1),
            Nova(2, "Escada rolante com ruído", StatusSolicitacao.Solicitada, 5),
            Nova(3, "Catraca travada", StatusSolicitacao.Solicitada, 2),
            // ID 4: SOLICITADA de OUTRO dono (teste de bloqueio: Comum não cancela chamado alheio)
            Nova(4, "Vazamento no banheiro", StatusSolicitacao.Solicitada, 4, "outro456"),

            Nova(5, "Porta do vagão desalinhada", StatusSolicitacao.Cancelada, 8),
            Nova(6, "Aparelho de ar-condicionado com defeito", StatusSolicitacao.Solicitada, 10),
            Nova(7, "Grade de proteção solta", StatusSolicitacao.Solicitada, 11),
            Nova(8, "Ventilador do túnel sem funcionar", StatusSolicitacao.Aprovada, 12),
            Nova(9, "Sinalização apagada (devolvida)", StatusSolicitacao.Devolvida, 13),
            Nova(10, "Bebedouro sem pressão", StatusSolicitacao.EmManutencao, 14),
            Nova(11, "Piso solto na estação", StatusSolicitacao.Concluida, 9),
            Nova(12, "Escada rolante com ruído (corrigida)", StatusSolicitacao.Reenviada, 5)
        };

        // US-22: SSLs que já passaram pela recepção física têm número SSM
        private static readonly Dictionary<int, string> SsmDaCarga = new()
        {
            [10] = "SSM-2026-000001",
            [11] = "SSM-2026-000002"
        };

        // Histórico coerente com a massa: abertura (nulo -> SOLICITADA) de todas e, para as que já
        // estão em outro status, uma linha de carga inicial até o status atual.
        public static HistoricoStatus[] Historico()
        {
            var dataCarga = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            var linhas = new List<HistoricoStatus>();
            var proximoId = 1;

            foreach (var s in Solicitacoes())
            {
                linhas.Add(new HistoricoStatus
                {
                    Id = proximoId++,
                    SolicitacaoId = s.Id,
                    StatusAnterior = null,
                    StatusNovo = StatusSolicitacao.Solicitada,
                    Matricula = s.MatriculaSolicitante,
                    Data = dataCarga
                });

                if (s.Status != StatusSolicitacao.Solicitada)
                    linhas.Add(new HistoricoStatus
                    {
                        Id = proximoId++,
                        SolicitacaoId = s.Id,
                        StatusAnterior = StatusSolicitacao.Solicitada,
                        StatusNovo = s.Status,
                        Matricula = HistoricoStatus.MatriculaSistema,
                        Data = dataCarga.AddMinutes(1),
                        Observacao = "Carga inicial"
                    });
            }

            return linhas.ToArray();
        }

        // US-07: SSLs da carga que já passaram pela oficina (10 EM MANUTENCAO, 11 CONCLUIDA) têm OSM do técnico tec123
        private static readonly DateTime DataOsmCarga = new(2026, 11, 1, 12, 2, 0, DateTimeKind.Utc);

        public static OrdemServico[] OrdensServico() => new[]
        {
            new OrdemServico { Id = 1, SolicitacaoId = 10, MatriculaTecnico = "tec123", DataAbertura = DataOsmCarga },
            new OrdemServico
            {
                Id = 2, SolicitacaoId = 11, MatriculaTecnico = "tec123", DataAbertura = DataOsmCarga,
                DataConclusao = DataOsmCarga.AddMinutes(1), Laudo = "Piso refixado (carga inicial)."
            }
        };

        private static Solicitacao Nova(int id, string descricao, string status, int equipamentoId, string matriculaSolicitante = "comum123") =>
            new()
            {
                Id = id,
                Descricao = descricao,
                Status = status,
                EquipamentoId = equipamentoId,
                DataAbertura = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
                MatriculaSolicitante = matriculaSolicitante,
                NumeroSsm = SsmDaCarga.GetValueOrDefault(id),
                DataRecebimento = SsmDaCarga.ContainsKey(id) ? new DateTime(2026, 11, 1, 12, 1, 0, DateTimeKind.Utc) : null,
                MatriculaTecnico = SsmDaCarga.ContainsKey(id) ? "tec123" : null,
                DataInicioManutencao = SsmDaCarga.ContainsKey(id) ? DataOsmCarga : null
            };
    }
}
