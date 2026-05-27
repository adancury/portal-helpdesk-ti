using PortalHelpdeskTI.Models;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Linq;
using System;

namespace PortalHelpdeskTI.Services
{
    public class AnexoService
    {
        private readonly AppDbContext _context;

        public AnexoService(AppDbContext context)
        {
            _context = context;
        }

        public (bool sucesso, string mensagem) SalvarAnexo(int chamadoId, IFormFile anexo, int? interacaoId = null)
        {
            if (anexo == null || anexo.Length == 0)
                return (false, "Nenhum arquivo selecionado.");

            var extensoesPermitidas = new[] { ".pdf", ".docx", ".jpg", ".png", ".mp4", ".xlsx",".pptx",".txt" };
            var extensao = Path.GetExtension(anexo.FileName).ToLowerInvariant();

            /*if (!extensoesPermitidas.Contains(extensao))
                return (false, "Tipo de arquivo não permitido.");*/

            if (anexo.Length > 100 * 1024 * 1024)
                return (false, "Arquivo excede o tamanho máximo permitido (10 MB).");

            var uploads = Path.Combine(Directory.GetCurrentDirectory(), "Anexos", chamadoId.ToString());

            if (!Directory.Exists(uploads))
            {
                Directory.CreateDirectory(uploads);
                Console.WriteLine($"Criado diretório: {uploads}");
            }
            else
            {
                Console.WriteLine($"Diretório já existe: {uploads}");
            }

            var nomeOriginal = Path.GetFileName(anexo.FileName);
            var nomeBase = Path.GetFileNameWithoutExtension(nomeOriginal);
            var extensaoOriginal = Path.GetExtension(nomeOriginal);
            var nomeComIdentificador = $"{chamadoId}_{nomeBase}{extensaoOriginal}";
            var filePath = Path.Combine(uploads, nomeComIdentificador);

            int contador = 1;
            while (File.Exists(filePath))
            {
                nomeComIdentificador = $"{chamadoId}_{nomeBase}_{contador}{extensaoOriginal}";
                filePath = Path.Combine(uploads, nomeComIdentificador);
                contador++;
            }

            Console.WriteLine($"Salvando anexo em: {filePath}");

            try
            {
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    anexo.CopyTo(stream);
                }
                Console.WriteLine("Arquivo salvo com sucesso!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao salvar arquivo: {ex.Message}");
                return (false, $"Erro ao salvar arquivo: {ex.Message}");
            }
            var caminhoRelativo = nomeComIdentificador; // Apenas o nome do arquivo
            var novoAnexo = new Anexo
            {
                ChamadoId = chamadoId,
                NomeOriginal = nomeOriginal,
                CaminhoArquivo = caminhoRelativo,
                InteracaoId = interacaoId
            };

            _context.Anexos.Add(novoAnexo);
            _context.SaveChanges();

            return (true, "Anexo salvo com sucesso.");
        }
    }
}
