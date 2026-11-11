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

        public static Solicitacao[] Solicitacoes() => new[]
        {
            // IDs 1, 2 e 3: SOLICITADA, pertencentes ao usuário Comum (comum123)
            Nova(1, "Lâmpada queimada na plataforma 2", StatusSolicitacao.Solicitada),
            Nova(2, "Escada rolante com ruído", StatusSolicitacao.Solicitada),
            Nova(3, "Catraca travada", StatusSolicitacao.Solicitada),
            // ID 4: SOLICITADA de OUTRO dono (teste de bloqueio: Comum não cancela chamado alheio)
            Nova(4, "Vazamento no banheiro", StatusSolicitacao.Solicitada, "outro456"),

            Nova(5, "Porta do vagão desalinhada", StatusSolicitacao.Cancelada),
            Nova(6, "Aparelho de ar-condicionado com defeito", StatusSolicitacao.Solicitada),
            Nova(7, "Grade de proteção solta", StatusSolicitacao.Solicitada),
            Nova(8, "Ventilador do túnel sem funcionar", StatusSolicitacao.Aprovada),
            Nova(9, "Sinalização apagada (devolvida)", StatusSolicitacao.Devolvida),
            Nova(10, "Bebedouro sem pressão", StatusSolicitacao.EmManutencao),
            Nova(11, "Piso solto na estação", StatusSolicitacao.Concluida),
            Nova(12, "Escada rolante com ruído (corrigida)", StatusSolicitacao.Reenviada)
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

        private static Solicitacao Nova(int id, string descricao, string status, string matriculaSolicitante = "comum123") =>
            new()
            {
                Id = id,
                Descricao = descricao,
                Status = status,
                MatriculaSolicitante = matriculaSolicitante
            };
    }
}
