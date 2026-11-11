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

        public DbSet<Usuario> Usuarios => Set<Usuario>();
        public DbSet<Estacao> Estacoes => Set<Estacao>();
        public DbSet<Equipamento> Equipamentos => Set<Equipamento>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Usuario>(entidade =>
            {
                entidade.ToTable("USUARIO");
                entidade.HasKey(u => u.Matricula);
                entidade.Property(u => u.Matricula).HasMaxLength(30);
                entidade.Property(u => u.Nome).HasMaxLength(100).IsRequired();
                entidade.Property(u => u.SenhaHash).HasMaxLength(200).IsRequired();
                entidade.Property(u => u.Perfil).HasMaxLength(30).IsRequired();

                entidade.HasData(DadosIniciais.Usuarios());
            });

            modelBuilder.Entity<Estacao>(entidade =>
            {
                entidade.ToTable("ESTACAO");
                entidade.HasKey(e => e.Id);
                entidade.Property(e => e.Nome).HasMaxLength(100).IsRequired();
                entidade.Property(e => e.Linha).HasMaxLength(30).IsRequired();

                entidade.HasData(DadosIniciais.Estacoes());
            });

            modelBuilder.Entity<Equipamento>(entidade =>
            {
                entidade.ToTable("EQUIPAMENTO");
                entidade.HasKey(e => e.Id);
                entidade.Property(e => e.Descricao).HasMaxLength(200).IsRequired();
                entidade.Property(e => e.Patrimonio).HasMaxLength(30).IsRequired();
                entidade.HasIndex(e => e.Patrimonio).IsUnique();
                entidade.HasIndex(e => e.EstacaoId);
                entidade.HasOne(e => e.Estacao)
                    .WithMany()
                    .HasForeignKey(e => e.EstacaoId)
                    .OnDelete(DeleteBehavior.Restrict);

                entidade.HasData(DadosIniciais.Equipamentos());
            });

            modelBuilder.Entity<Solicitacao>(entidade =>
            {
                entidade.ToTable("SOLICITACOES");
                entidade.HasKey(s => s.Id);
                entidade.Property(s => s.Descricao).HasMaxLength(500).IsRequired();
                entidade.Property(s => s.Status).HasMaxLength(30).IsRequired();
                entidade.Property(s => s.MotivoDevolucao).HasMaxLength(500);
                entidade.Property(s => s.MatriculaSolicitante).HasMaxLength(30).IsRequired();
                entidade.Property(s => s.MatriculaTecnico).HasMaxLength(30);
                entidade.Property(s => s.FotoUrl).HasMaxLength(300);
                entidade.Property(s => s.DataAbertura)
                    .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
                entidade.Property(s => s.DataInicioManutencao)
                    .HasConversion(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
                entidade.HasOne(s => s.Equipamento)
                    .WithMany()
                    .HasForeignKey(s => s.EquipamentoId)
                    .OnDelete(DeleteBehavior.Restrict);
                entidade.HasIndex(s => s.EquipamentoId);
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
                entidade.Property(h => h.Data)
                    .HasConversion(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
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
