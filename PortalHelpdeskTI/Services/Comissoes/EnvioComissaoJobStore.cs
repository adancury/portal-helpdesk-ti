using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PortalHelpdeskTI.Models.Comissoes;
using PortalHelpdeskTI.Pdf;
using PortalHelpdeskTI.Services;
using Microsoft.AspNetCore.Hosting;
using System.Collections.Concurrent;
using System.Globalization;


namespace PortalHelpdeskTI.Services.Comissoes
{
    public sealed class EnvioComissaoJobStore
    {
        private readonly ConcurrentDictionary<string, JobState> _jobs = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;
        private static readonly SemaphoreSlim _resumoLock = new(1, 1);


        public EnvioComissaoJobStore(IServiceScopeFactory scopeFactory, IWebHostEnvironment env, IConfiguration config,
            IMemoryCache cache)
        {
            _scopeFactory = scopeFactory;
            _env = env;
            _config = config;
            _cache = cache;
        }

        public string StartJob(string periodo, string grupo)
        {
            // periodo: "YYYY-M" ou "YYYY-MM"
            if (!TryParsePeriodo(periodo, out var ano, out var mes))
                throw new ArgumentException("Período inválido.", nameof(periodo));

            grupo = (grupo ?? "").Trim().ToUpperInvariant();
            if (grupo != "IE" && grupo != "REP" && grupo != "FISCAL")
                throw new ArgumentException("Grupo inválido. Use IE, REP ou FISCAL.", nameof(grupo));

            var jobId = Guid.NewGuid().ToString("N");

            var st = new JobState
            {
                JobId = jobId,
                Periodo = periodo,
                Grupo = grupo, // ✅ NOVO
                Status = grupo == "REP"
                ? "Iniciando... (Representantes)"
                : grupo == "FISCAL"
                    ? "Iniciando... (Fiscal - Resumo Geral)"
                    : "Iniciando... (Interno/Externo)"
                        };

            _jobs[jobId] = st;

            _ = Task.Run(async () =>
            {
                try
                {
                    // ✅ passa o grupo para o processamento
                    await ProcessarAsync(jobId, ano, mes, grupo, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Update(jobId, s =>
                    {
                        s.Status = "Erro";
                        s.Finished = true;
                        s.FinishedAt = DateTime.Now;
                        s.Falhas++;
                        s.FalhasItens.Add(new EnvioFalhaItem
                        {
                            SlpCode = 0,
                            SlpName = "Processo",
                            Email = "",
                            Erro = ex.Message
                        });
                    });
                }
            });

            return jobId;
        }

        public JobState? Get(string jobId)
            => _jobs.TryGetValue(jobId, out var st) ? st : null;

        private void Update(string jobId, Action<JobState> apply)
        {
            if (!_jobs.TryGetValue(jobId, out var st)) return;
            lock (st.LockObj)
            {
                apply(st);
            }
        }

        private async Task ProcessarAsync(string jobId, int ano, int mes, string grupo, CancellationToken ct)
        {
            Update(jobId, s =>
            {
                s.Status = "Carregando vendedores...";
                s.Total = 0;
                s.Processados = 0;
                s.Enviados = 0;
                s.Falhas = 0;
                s.SemEmail = 0;
                s.FalhasItens.Clear();
            });

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var svc = scope.ServiceProvider.GetRequiredService<IComissoesService>();
            var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

            grupo = (grupo ?? "IE").Trim().ToUpperInvariant();

            var cultura = new CultureInfo("pt-BR");
            var logoPath = Path.Combine(_env.WebRootPath, "images", "logo.png");

            // Lista do appsettings (usada em REP e FISCAL)
            var destinatariosResumo = _config
                .GetSection("Comissoes:EmailsResumoGeral")
                .Get<string[]>() ?? Array.Empty<string>();

            // =========================================================
            // 1) MODO FISCAL: somente reenvio do Resumo Geral (REP)
            // =========================================================
            if (grupo == "FISCAL")
            {
                try
                {
                    if (destinatariosResumo.Length == 0)
                        throw new InvalidOperationException("Configuração 'Comissoes:EmailsResumoGeral' não definida.");

                    var destinatariosValidos = destinatariosResumo
                        .Select(x => (x ?? "").Trim().TrimEnd(';', ','))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    // ✅ Zera e configura contadores para refletir o envio do resumo
                    Update(jobId, s =>
                    {
                        s.Status = "Gerando PDF Resumo Geral (Fiscal)...";
                        s.Total = destinatariosResumo.Length; // total de destinatários (pode incluir vazios)
                        s.Processados = 0;
                        s.Enviados = 0;
                        s.Falhas = 0;
                        s.SemEmail = 0;
                        s.FalhasItens.Clear();
                    });

                    var resumo = await GetResumoFromCacheOrBuildAsync(svc, ano, mes, ct);

                    // Força somente representantes no resumo fiscal
                    resumo.Linhas = resumo.Linhas
                        .Where(l =>
                        {
                            var t = (l.TipoVendedor ?? "").Trim().ToUpperInvariant();
                            return t == "REPRESENTANTE" || t == "REP";
                        })
                        .OrderBy(l => l.SlpName)
                        .ToList();

                    if (resumo.Linhas.Count == 0)
                        throw new InvalidOperationException("Resumo fiscal: sem representantes no período.");

                    var pdfResumoBytes = ComissaoResumoGeralPdfBuilder.Gerar(resumo, logoPath);

                    var nomeResumoPdf = $"Resumo_Comissoes_REP_{ano:D4}{mes:D2}.pdf";
                    var assuntoResumo = $"Resumo Geral de Comissões (Representantes) - {mes:D2}/{ano:D4} - BRW";

                    var corpoResumoHtml = $@"
                    <p>Olá,</p>
                    <p>Reenvio do <strong>Resumo Geral de Comissões</strong> referente ao período <strong>{mes:D2}/{ano:D4}</strong>.</p>
                    <p><strong>Grupo:</strong> Representantes</p>
                    <br/>
                    <p>Atenciosamente,<br/>BRW</p>";

                    Update(jobId, s => s.Status = $"Enviando Resumo Geral ({destinatariosResumo.Length} destinatários)...");

                    foreach (var dest in destinatariosValidos)
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            await email.EnviarComAnexoPdfAsync(
                                dest,
                                assuntoResumo,
                                corpoResumoHtml,
                                pdfResumoBytes,
                                nomeResumoPdf,
                                ct
                            );

                            Update(jobId, s =>
                            {
                                s.Processados++;
                                s.Enviados++;
                            });
                        }
                        catch (Exception exDest)
                        {
                            Update(jobId, s =>
                            {
                                s.Processados++;
                                s.Falhas++;
                                s.FalhasItens.Add(new EnvioFalhaItem
                                {
                                    SlpCode = 0,
                                    SlpName = "Resumo Fiscal",
                                    Email = dest,
                                    Erro = exDest.Message
                                });
                            });
                        }
                    }

                    Update(jobId, s =>
                    {
                        s.Status = "Concluído (Resumo Fiscal enviado)";
                        s.Finished = true;
                        s.FinishedAt = DateTime.Now;
                    });

                    return; // ✅ não envia para vendedores
                }
                catch (Exception ex)
                {
                    Update(jobId, s =>
                    {
                        s.Status = "Erro (Resumo Fiscal)";
                        s.Finished = true;
                        s.FinishedAt = DateTime.Now;
                        s.Falhas++;
                        s.FalhasItens.Add(new EnvioFalhaItem
                        {
                            SlpCode = 0,
                            SlpName = "Resumo Fiscal",
                            Email = "(lista appsettings)",
                            Erro = ex.Message
                        });
                    });

                    return;
                }
            }

            // =========================================================
            // 2) Carrega vendedores e envia e-mails individuais (IE ou REP)
            // =========================================================
            var vendedores = await db.ComissaoVendedores
                .Where(v => v.Ativo && v.ParticipaRelatorio)
                .OrderBy(v => v.SlpName)
                .ToListAsync(ct);

            if (grupo == "REP")
            {
                vendedores = vendedores
                    .Where(v =>
                    {
                        var t = (v.TipoVendedor ?? "").Trim().ToUpperInvariant();
                        return t == "REPRESENTANTE" || t == "REP";
                    })
                    .ToList();
            }
            else // IE (padrão)
            {
                vendedores = vendedores
                    .Where(v =>
                    {
                        var t = (v.TipoVendedor ?? "").Trim().ToUpperInvariant();
                        return t != "REPRESENTANTE" && t != "REP";
                    })
                    .ToList();
            }

            Update(jobId, s =>
            {
                s.Total = vendedores.Count;
                s.Status = "Enviando e-mails...";
            });

            foreach (var v in vendedores)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var emailPara = (v.Email ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(emailPara))
                    {
                        Update(jobId, s =>
                        {
                            s.Processados++;
                            s.SemEmail++;
                        });
                        continue;
                    }

                    var (ini, fim) = CalcularPeriodoApuracao(v.TipoVendedor, ano, mes);

                    var rel = await svc.GerarRelatorioAsync(v.SlpCode, ini, fim, ct);
                    var pdfBytes = ComissaoPdfBuilder.Gerar(rel, logoPath);
                    var nomePdf = $"Comissao_{v.SlpCode}_{ini:yyyyMM}.pdf";

                    var assunto = $"Relatório de Comissão - {ini:MM/yyyy} - BRW";

                    // identifica se é representante (independente do grupo que foi chamado)
                    var tipoVend = (v.TipoVendedor ?? "").Trim().ToUpperInvariant();
                    var isRep = (tipoVend == "REPRESENTANTE" || tipoVend == "REP");

                    string corpoHtml;

                    if (isRep)
                    {
                        // ✅ Corpo para REPRESENTANTE
                        var descontos = rel.DescontoCondicionado + rel.DescontosRepresentante;
                        var comissaoLiquida = rel.ComissaoBruta - descontos;
                        var valorReceber = comissaoLiquida - rel.Tributos;
                        corpoHtml = $@"
                                    <p>Olá, <strong>{v.SlpName}</strong>,</p>

                                    <p>
                                    Segue em anexo o <strong>relatório de comissão</strong> referente ao período 
                                    <strong>{ini:dd/MM/yyyy} a {fim:dd/MM/yyyy}</strong>.
                                    </p>

                                    <p><strong>Valor a receber:</strong> {valorReceber.ToString("C", cultura)}</p>

                                    <br/>

                                    <p>
                                    <strong>ATENÇÃO:</strong><br/>
                                    Favor elaborar a <strong>Nota Fiscal</strong> informando no corpo da NF que o serviço foi prestado
                                    no município onde a sua empresa está situada.
                                    </p>

                                    <p>
                                    O pagamento da comissão será realizado <strong>somente após a emissão e envio da Nota Fiscal</strong>
                                    para o financeiro.
                                    </p>

                                    <p>
                                    Caso existam <strong>observações no relatório</strong>, verifique atentamente e siga as orientações
                                    descritas nele.
                                    </p>

                                    <p>
                                    Envio da Nota Fiscal para:<br/>
                                    <strong>fiscal@brwsuprimentos.com.br</strong>
                                    </p>

                                    <br/>

                                    <p>Atenciosamente,<br/><strong>BRW Suprimentos</strong></p>";
                    }
                    else
                    {
                        // ✅ Corpo para VENDEDOR INTERNO (IE)
                        corpoHtml = $@"
                                    <p>Olá, <strong>{v.SlpName}</strong>,</p>

                                    <p>
                                    Segue em anexo o <strong>relatório de comissão</strong> referente ao período 
                                    <strong>{ini:dd/MM/yyyy} a {fim:dd/MM/yyyy}</strong>.
                                    </p>

                                    <p><strong>Valor a receber:</strong> {rel.ValorReceber.ToString("C", cultura)}</p>

                                    <br/>

                                    <p>
                                    Em caso de divergências ou dúvidas, favor retornar para o time responsável com as evidências
                                    necessárias.
                                    </p>

                                    <br/>

                                    <p>Atenciosamente,<br/><strong>BRW Suprimentos</strong></p>";
                    }

                    var emailCopia = "vitoria.pereira@brwsuprimentos.com.br";
                    await email.EnviarComAnexoPdfAsync(emailPara, assunto, corpoHtml, pdfBytes, nomePdf, ct, emailCopia);

                    Update(jobId, s =>
                    {
                        s.Processados++;
                        s.Enviados++;
                    });
                }
                catch (Exception ex)
                {
                    Update(jobId, s =>
                    {
                        s.Processados++;
                        s.Falhas++;
                        s.FalhasItens.Add(new EnvioFalhaItem
                        {
                            SlpCode = v.SlpCode,
                            SlpName = v.SlpName,
                            Email = (v.Email ?? "").Trim(),
                            Erro = ex.Message
                        });
                    });
                }
            }

            // =========================================================
            // 3) Resumo Geral: somente quando grupo == REP
            // =========================================================
            if (grupo == "REP")
            {
                try
                {
                    if (destinatariosResumo.Length == 0)
                        throw new InvalidOperationException("Configuração 'Comissoes:EmailsResumoGeral' não definida.");

                    Update(jobId, s => s.Status = "Gerando PDF Resumo Geral (Fiscal)...");

                    var resumo = await GetResumoFromCacheOrBuildAsync(svc, ano, mes, ct);

                    // Garante apenas representantes no resumo
                    resumo.Linhas = resumo.Linhas
                        .Where(l =>
                        {
                            var t = (l.TipoVendedor ?? "").Trim().ToUpperInvariant();
                            return t == "REPRESENTANTE" || t == "REP";
                        })
                        .OrderBy(l => l.SlpName)
                        .ToList();

                    if (resumo.Linhas.Count > 0)
                    {
                        var pdfResumoBytes = ComissaoResumoGeralPdfBuilder.Gerar(resumo, logoPath);

                        var nomeResumoPdf = $"Resumo_Comissoes_REP_{ano:D4}{mes:D2}.pdf";
                        var assuntoResumo = $"Resumo Geral de Comissões (Representantes) - {mes:D2}/{ano:D4} - BRW";

                        var corpoResumoHtml = $@"
                        <p>Olá,</p>
                        <p>Segue em anexo o <strong>Resumo Geral de Comissões</strong> referente ao período <strong>{mes:D2}/{ano:D4}</strong>.</p>
                        <p><strong>Grupo:</strong> Representantes</p>
                        <br/>
                        <p>Atenciosamente,<br/>BRW</p>";

                        Update(jobId, s => s.Status = $"Enviando Resumo Geral ({destinatariosResumo.Length} destinatários)...");

                        foreach (var dest in destinatariosResumo)
                        {
                            if (string.IsNullOrWhiteSpace(dest)) continue;

                            await email.EnviarComAnexoPdfAsync(
                                dest.Trim(),
                                assuntoResumo,
                                corpoResumoHtml,
                                pdfResumoBytes,
                                nomeResumoPdf,
                                ct
                            );
                        }
                    }
                }
                catch (Exception exResumo)
                {
                    Update(jobId, s =>
                    {
                        s.Falhas++;
                        s.FalhasItens.Add(new EnvioFalhaItem
                        {
                            SlpCode = 0,
                            SlpName = "Resumo Geral (REP)",
                            Email = "(lista appsettings)",
                            Erro = exResumo.Message
                        });
                    });
                }
            }

            Update(jobId, s =>
            {
                s.Status = "Concluído";
                s.Finished = true;
                s.FinishedAt = DateTime.Now;
            });
        }

        private static bool TryParsePeriodo(string periodo, out int ano, out int mes)
        {
            ano = 0;
            mes = 0;
            if (string.IsNullOrWhiteSpace(periodo)) return false;

            var parts = periodo.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (!int.TryParse(parts[0], out ano)) return false;
            if (!int.TryParse(parts[1], out mes)) return false;

            return mes >= 1 && mes <= 12;
        }

        private static (DateTime Ini, DateTime Fim) CalcularPeriodoApuracao(string? tipoVendedor, int ano, int mes)
        {
            var tipo = (tipoVendedor ?? "").Trim().ToUpperInvariant();

            if (tipo == "REPRESENTANTE" || tipo == "REP")
            {
                var ini = new DateTime(ano, mes, 1);
                var fim = new DateTime(ano, mes, DateTime.DaysInMonth(ano, mes));
                return (ini, fim);
            }

            var primeiroDiaMesRef = new DateTime(ano, mes, 1);
            var mesAnterior = primeiroDiaMesRef.AddMonths(-1);

            var ini2 = new DateTime(mesAnterior.Year, mesAnterior.Month, 26);
            var fim2 = new DateTime(ano, mes, 25);
            return (ini2, fim2);
        }

        // =========================
        // Tipos do Job (ATENÇÃO: não duplicar membros aqui)
        // =========================

        public sealed class JobState
        {
            public string JobId { get; set; } = "";
            public string Periodo { get; set; } = "";
            public string Status { get; set; } = "";

            public int Total { get; set; }
            public int Processados { get; set; }
            public int Enviados { get; set; }

            // IMPORTANTÍSSIMO: existe UMA ÚNICA vez
            public int Falhas { get; set; }

            public int SemEmail { get; set; }

            public bool Finished { get; set; }
            public DateTime? FinishedAt { get; set; }

            public List<EnvioFalhaItem> FalhasItens { get; set; } = new();

            public object LockObj { get; } = new object();
            public string Grupo { get; set; } = "IE";

        }

        public sealed class EnvioFalhaItem
        {
            public int SlpCode { get; set; }
            public string SlpName { get; set; } = "";
            public string Email { get; set; } = "";
            public string Erro { get; set; } = "";
        }
        private async Task<ResumoComissaoVm> GetResumoFromCacheOrBuildAsync(
    IComissoesService svc,
    int ano,
    int mes,
    CancellationToken ct)
        {
            var cacheKey = $"comissao:resumo:{ano:D4}-{mes:D2}";

            if (_cache.TryGetValue(cacheKey, out ResumoComissaoVm? vm) && vm != null)
                return vm;

            await _resumoLock.WaitAsync(ct);
            try
            {
                if (_cache.TryGetValue(cacheKey, out vm) && vm != null)
                    return vm;

                vm = await svc.GerarResumoAsync(ano, mes, ct);

                _cache.Set(cacheKey, vm, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20)
                });

                return vm;
            }
            finally
            {
                _resumoLock.Release();
            }
        }

    }
}
