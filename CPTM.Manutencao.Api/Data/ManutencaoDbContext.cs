using Microsoft.EntityFrameworkCore;
using CPTM.Manutencao.Api.Models;

namespace CPTM.Manutencao.Api.Data
{
    // Contexto EF Core. O provedor (Sqlite ou Oracle) é escolhido no Program.cs via Database:Provider.
    // SQLite é temporário: ver débito técnico CPTM-92.
    public class ManutencaoDbContext : DbContext
    {
        public ManutencaoDbContext(DbContextOptions<ManutencaoDbContext> options) : base(options) { }

        public DbSet<Solicitacao> Solicitacoes => Set<Solicitacao>();
        public DbSet<HistoricoStatus> HistoricoStatus => Set<HistoricoStatus>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Solicitacao>(entidade =>
            {
                entidade.ToTable("SOLICITACOES");
                entidade.HasKey(s => s.Id);
                entidade.Property(s => s.Descricao).HasMaxLength(500).IsRequired();
                entidade.Property(s => s.Status).HasMaxLength(30).IsRequired();
                entidade.Property(s => s.MotivoDevolucao).HasMaxLength(500);
                entidade.Property(s => s.MatriculaSolicitante).HasMaxLength(30).IsRequired();
                entidade.Property(s => s.MatriculaTecnico).HasMaxLength(30);
                entidade.HasIndex(s => s.Status);
                entidade.HasIndex(s => s.MatriculaSolicitante);

                entidade.HasData(DadosIniciais.Solicitacoes());
            });

            modelBuilder.Entity<HistoricoStatus>(entidade =>
            {
                entidade.ToTable("HISTORICO_STATUS");
                entidade.HasKey(h => h.Id);
                entidade.Property(h => h.StatusAnterior).HasMaxLength(30);
                entidade.Property(h => h.StatusNovo).HasMaxLength(30).IsRequired();
                entidade.Property(h => h.Matricula).HasMaxLength(30).IsRequired();
                entidade.Property(h => h.Observacao).HasMaxLength(500);
                entidade.HasOne(h => h.Solicitacao)
                    .WithMany()
                    .HasForeignKey(h => h.SolicitacaoId)
                    .OnDelete(DeleteBehavior.Cascade);
                entidade.HasIndex(h => new { h.SolicitacaoId, h.Data });

                entidade.HasData(DadosIniciais.Historico());
            });
        }
    }
}
