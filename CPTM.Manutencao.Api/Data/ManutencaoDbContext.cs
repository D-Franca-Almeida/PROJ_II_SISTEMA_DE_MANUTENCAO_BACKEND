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
        }
    }
}
