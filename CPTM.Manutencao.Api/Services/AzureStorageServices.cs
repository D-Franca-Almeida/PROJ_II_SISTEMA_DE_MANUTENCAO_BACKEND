using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.AspNetCore.Http;

namespace CPTM.Manutencao.Api.Services
{
    public class AzureStorageOptions
    {
        public string? ConnectionString { get; set; }
        public string ContainerFotos { get; set; } = "uploads";
        public string FilaEventos { get; set; } = "eventos-manutencao";
    }

    // Representa um evento assíncrono publicado na fila de mensageria (Login ou Transição de Status da SSL)
    public record EventoSistemaMensagem(
        string TipoEvento,
        string Matricula,
        int? SolicitacaoId,
        string? StatusAnterior,
        string? StatusNovo,
        string? Detalhe,
        DateTime DataUtc);

    public interface INotificacaoQueue
    {
        void Publicar(EventoSistemaMensagem evento);
        IReadOnlyList<EventoSistemaMensagem> ObterRecentes(int limite = 50);
    }

    // Fallback em memória (usado automaticamente quando AzureStorage:ConnectionString não está configurada)
    public class InMemoryNotificacaoQueue : INotificacaoQueue
    {
        private readonly ConcurrentQueue<EventoSistemaMensagem> _fila = new();

        public void Publicar(EventoSistemaMensagem evento)
        {
            _fila.Enqueue(evento);
            while (_fila.Count > 200)
                _fila.TryDequeue(out _);
        }

        public IReadOnlyList<EventoSistemaMensagem> ObterRecentes(int limite = 50) =>
            _fila.Reverse().Take(Math.Clamp(limite, 1, 200)).ToList();
    }

    // Implementação real com Azure Queue Storage (stcptmmanut2026 / fila "eventos-manutencao")
    public class AzureQueueNotificacaoService : INotificacaoQueue
    {
        private readonly QueueClient _queueClient;
        private readonly InMemoryNotificacaoQueue _espelhoMemoria = new();

        public AzureQueueNotificacaoService(AzureStorageOptions opcoes)
        {
            if (string.IsNullOrWhiteSpace(opcoes.ConnectionString))
                throw new InvalidOperationException("AzureStorage:ConnectionString não configurada.");

            _queueClient = new QueueClient(opcoes.ConnectionString, opcoes.FilaEventos, new QueueClientOptions
            {
                MessageEncoding = QueueMessageEncoding.Base64
            });
            _queueClient.CreateIfNotExists();
        }

        public void Publicar(EventoSistemaMensagem evento)
        {
            _espelhoMemoria.Publicar(evento);
            try
            {
                var json = JsonSerializer.Serialize(evento);
                _queueClient.SendMessage(json);
            }
            catch
            {
                // Não interrompe a transação principal caso haja oscilação momentânea de rede com a nuvem
            }
        }

        public IReadOnlyList<EventoSistemaMensagem> ObterRecentes(int limite = 50) =>
            _espelhoMemoria.ObterRecentes(limite);
    }

    // Implementação real de Object Storage na nuvem com Azure Blob Storage (container "uploads")
    public class AzureBlobFotoStorage : IFotoStorage
    {
        private const string Subpasta = "ssl";
        private readonly BlobContainerClient _container;

        public AzureBlobFotoStorage(AzureStorageOptions opcoes)
        {
            if (string.IsNullOrWhiteSpace(opcoes.ConnectionString))
                throw new InvalidOperationException("AzureStorage:ConnectionString não configurada.");

            _container = new BlobContainerClient(opcoes.ConnectionString, opcoes.ContainerFotos);
            _container.CreateIfNotExists(PublicAccessType.Blob);
        }

        public async Task<string> SalvarAsync(IFormFile arquivo, string extensao)
        {
            var nomeBlob = $"{Subpasta}/{Guid.NewGuid():N}{extensao}";
            var blob = _container.GetBlobClient(nomeBlob);

            var contentType = extensao.Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? "image/png"
                : "image/jpeg";

            await using var stream = arquivo.OpenReadStream();
            await blob.UploadAsync(stream, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });

            return blob.Uri.ToString();
        }

        public void Excluir(string url)
        {
            try
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var caminho = uri.AbsolutePath.TrimStart('/');
                    var prefixoContainer = _container.Name + "/";
                    var nomeBlob = caminho.StartsWith(prefixoContainer, StringComparison.OrdinalIgnoreCase)
                        ? caminho[prefixoContainer.Length..]
                        : $"{Subpasta}/{Path.GetFileName(uri.LocalPath)}";
                    _container.DeleteBlobIfExists(nomeBlob);
                }
            }
            catch
            {
                // Ignora falha de limpeza de blob órfão
            }
        }
    }
}
