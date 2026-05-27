// Services/Relatorios/MudancaCarteiraService.cs
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.IO;
using System.Threading.Tasks;
using ExcelDataReader;
using Microsoft.Extensions.Configuration;

namespace PortalHelpdeskTI.Services.Relatorios
{
    // Classe auxiliar para representar cada linha da planilha
    public class MudancaCarteiraLinha
    {
        public string CardCode { get; set; } = string.Empty;
        public int SlpCode { get; set; }
    }

    public class MudancaCarteiraService
    {
        private readonly string _connStr;

        public MudancaCarteiraService(IConfiguration cfg)
        {
            // Use aqui o MESMO nome de connection string que você usa nos outros relatórios (DadosProdutos / Representantes)
            _connStr = cfg.GetConnectionString("HanaConn");
        }

        /// <summary>
        /// Lê a planilha (CardCode | SlpCode) e aplica update em OCRD.
        /// </summary>
        public async Task<MudancaCarteiraResultado> ProcessarAsync(
            Stream excelStream,
            bool atualizarPrincipal)
        {
            // Necessário pro ExcelDataReader ler alguns encodings
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var linhas = new List<MudancaCarteiraLinha>();

            using (var reader = ExcelReaderFactory.CreateReader(excelStream))
            {
                // Suposição: primeira aba, primeira linha cabeçalho
                do
                {
                    while (reader.Read())
                    {
                        if (reader.Depth == 0) continue; // pula header

                        var cardCode = reader.GetValue(0)?.ToString()?.Trim();
                        var slpStr = reader.GetValue(1)?.ToString()?.Trim();

                        if (string.IsNullOrWhiteSpace(cardCode) || string.IsNullOrWhiteSpace(slpStr))
                            continue;

                        if (!int.TryParse(slpStr, out var slpCode))
                            continue;

                        var linha = new MudancaCarteiraLinha
                        {
                            CardCode = cardCode,
                            SlpCode = slpCode
                        };

                        linhas.Add(linha);
                    }
                } while (reader.NextResult());
            }

            var resultado = new MudancaCarteiraResultado
            {
                Total = linhas.Count
            };

            if (linhas.Count == 0)
                return resultado;

            using (var conn = new OdbcConnection(_connStr))
            {
                await conn.OpenAsync();
                using (var tx = conn.BeginTransaction())
                {
                    // HANA via ODBC usa "?" como placeholder.
                    var sql = atualizarPrincipal
                        ? "UPDATE \"OCRD\" SET \"SlpCode\" = ? WHERE \"CardCode\" = ?"
                        : "UPDATE \"OCRD\" SET \"U_SegVendedor\" = ? WHERE \"CardCode\" = ?";

                    using (var cmd = new OdbcCommand(sql, conn, tx))
                    {
                        // Ordem dos parâmetros precisa bater com os "?"
                        cmd.Parameters.Add("SlpCode", OdbcType.Int);
                        cmd.Parameters.Add("CardCode", OdbcType.VarChar, 50);

                        foreach (var linha in linhas)
                        {
                            cmd.Parameters[0].Value = linha.SlpCode;
                            cmd.Parameters[1].Value = linha.CardCode;

                            try
                            {
                                var affected = await cmd.ExecuteNonQueryAsync();
                                if (affected > 0)
                                {
                                    resultado.Atualizados++;
                                }
                                else
                                {
                                    resultado.Erros.Add(
                                        $"Cliente {linha.CardCode}: não encontrado ou não atualizado.");
                                }
                            }
                            catch (Exception ex)
                            {
                                resultado.Erros.Add(
                                    $"Cliente {linha.CardCode}: erro ao atualizar ({ex.Message}).");
                            }
                        }
                    }

                    tx.Commit();
                }
            }

            return resultado;
        }
    }
}