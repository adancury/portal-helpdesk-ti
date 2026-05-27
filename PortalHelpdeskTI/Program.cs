using Microsoft.AspNetCore.Authentication.Cookies; // ⬅️ novo
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.SqlClient;            // + ADD
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OfficeOpenXml;
using PortalHelpdeskTI.Filters;
using PortalHelpdeskTI.Infrastructure;     // + ADD (IHanaDb/HanaDb)
using PortalHelpdeskTI.Models;
using PortalHelpdeskTI.Services;
using PortalHelpdeskTI.Services.API;
using PortalHelpdeskTI.Services.AvaliacaoChamado;
using PortalHelpdeskTI.Services.Calendario;
using PortalHelpdeskTI.Services.Comissoes;
using PortalHelpdeskTI.Services.Integracoes;
using PortalHelpdeskTI.Services.Inventario;
using PortalHelpdeskTI.Services.PainelTecnico;
using PortalHelpdeskTI.Services.Permissoes;
using PortalHelpdeskTI.Services.Quizzes;
using PortalHelpdeskTI.Services.Relatorios;
using PortalHelpdeskTI.Services.SAP; // Service Layer
using PortalHelpdeskTI.Services.ServiceLayer;
using System;
using System.Data;                         // + ADD

ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

var builder = WebApplication.CreateBuilder(args);

// -------------------- Services --------------------
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(opt =>
{
    opt.IdleTimeout = TimeSpan.FromHours(4);
    opt.Cookie.Name = ".PortalHelpdeskTI.Session";
    opt.Cookie.HttpOnly = true;
    opt.Cookie.IsEssential = true;
    opt.Cookie.SameSite = SameSiteMode.Lax;
    opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null
            );
        }));

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<AutenticacaoFilter>();
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AcessoNegado";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // options.Cookie.Name = ".PortalHelpdeskTI.Auth";
    });

builder.Services.AddAuthorization();

// Configurações
builder.Services.Configure<PortalHelpdeskTI.Models.Email.EmailSettings>(
    builder.Configuration.GetSection("EmailSettings"));

builder.Services.Configure<TranscricaoSettings>(builder.Configuration.GetSection("Transcricao"));
builder.Services.Configure<ChatGptOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.Configure<RupturaPrevisaoJobSettings>(builder.Configuration.GetSection("RupturaPrevisaoJob"));
builder.Services.Configure<WmsDadosApiOptions>(builder.Configuration.GetSection("WmsDadosApi"));

// App services
builder.Services.AddTransient<TranscricaoService>();
builder.Services.AddTransient<AtaService>();

// Email (unificado) - ÚNICO REGISTRO
builder.Services.AddScoped<IEmailService, EmailService>();

builder.Services.AddScoped<ChamadoService>();
builder.Services.AddScoped<LembreteAvaliacaoService>();
builder.Services.AddScoped<RelatorioTempoService>();
builder.Services.AddScoped<IQuizService, QuizService>();
builder.Services.AddScoped<IHolidayProvider, HolidayProvider>();
builder.Services.AddScoped<AnexoService>();
builder.Services.AddScoped<AvaliacaoService>();
builder.Services.AddMemoryCache();

//===== aprovações
builder.Services.AddScoped<PortalHelpdeskTI.Services.Aprovacoes.IApprovalService,
                           PortalHelpdeskTI.Services.Aprovacoes.ApprovalService>();

// ===== Service Layer (SAP B1) =====
builder.Services.AddSingleton<ServiceLayerCookieStore>();
builder.Services.AddHttpClient<ServiceLayerClient>((sp, client) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var baseUrl = cfg["SapB1:BaseUrl"] ?? "https://sapslbrw.skyinone.net:50000/b1s/v1/";
    client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(100);
});

// ===== HANA (ODBC) - ÚNICO REGISTRO
builder.Services.AddSingleton<IHanaDb, HanaDb>();

// ===== DB do Portal (SQL Server) - ÚNICO REGISTRO
builder.Services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<RelatorioProdutosService>();
builder.Services.AddTransient<RepresentantesVendasService>();
builder.Services.AddTransient<MudancaCarteiraService>();
builder.Services.AddScoped<RelatorioRupturasService>();
builder.Services.AddScoped<RelatorioRupturaPrevisaoService>();
builder.Services.AddScoped<StatusIndicadorService>();
builder.Services.AddScoped<CadColaboradorService>();

// Escolha 1 (recomendado): scoped
builder.Services.AddScoped<PrevisaoRupturaService>();

builder.Services.AddScoped<DashboardLiberacaoPedidosService>();
builder.Services.AddTransient<EstoqueEcommerceService>();
builder.Services.AddTransient<DashboardSavingComprasService>();
builder.Services.AddScoped<IPainelTecnicoIndicadoresService, PainelTecnicoIndicadoresService>();

builder.Services.AddScoped<IComissoesService, ComissoesService>();
builder.Services.AddSingleton<EnvioComissaoJobStore>();

// 🔐 Permissões
builder.Services.AddScoped<IRelatorioPermissaoService, RelatorioPermissaoService>();

// ✅ NOVO: Catálogo de Relatórios (resolve o erro do RelatoriosController)
builder.Services.AddScoped<RelatoriosCatalogoService>();

// ✅ NOVO: Comissões de Vendedor - cadastro de vendedores
builder.Services.AddScoped<IComissaoVendedorSyncService, ComissaoVendedorSyncService>();
builder.Services.AddScoped<MudancaCarteiraServiceLayerService>();
builder.Services.AddScoped<APIServices>();
builder.Services.AddScoped<RedistribuicaoCarteiraService>();
builder.Services.AddScoped<InventarioRedeService>();

// integrações wms
builder.Services.AddScoped<PortalHelpdeskTI.Services.Integracoes.IntegracoesWmsService>();
builder.Services.AddHttpClient<WmsDadosApiClient>((sp, client) =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WmsDadosApiOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(Math.Max(10, opt.TimeoutSeconds));
});
builder.Services.AddScoped<WmsProcessosSyncService>();
builder.Services.AddScoped<WmsProcessosQueryService>();
builder.Services.AddScoped<NexxSalesforceMonitorService>();

//Indicador de TI
builder.Services.AddScoped<PortalHelpdeskTI.Services.Relatorios.IndicadorTiService>();

//Comissão extra
builder.Services.AddScoped<PortalHelpdeskTI.Services.Relatorios.ComissaoExtraService>();

//API envia NFE para WMS FLJ
builder.Services.AddHttpClient<WmsApiService>();
builder.Services.AddScoped<WmsFilaFaturamentoService>();
builder.Services.AddScoped<WmsFaturamentoPayloadService>();
builder.Services.AddScoped<WmsProcessadorFilaService>();
builder.Services.AddSingleton<RupturaPrevisaoJobRunner>();
builder.Services.AddHostedService<WmsFilaBackgroundService>();
builder.Services.AddHostedService<WmsProcessosBackgroundService>();
builder.Services.AddHostedService<LembreteAvaliacaoBackgroundService>();
builder.Services.AddHostedService<RupturaPrevisaoBackgroundService>();

//MONITOR DO ENVIO DE NOTA
builder.Services.AddScoped<IntegracoesNotasService>();

//inativação pn
builder.Services.AddScoped<InativacaoParceiroService>();

var app = builder.Build();

// -------------------- Middleware pipeline --------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor
});

app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "monitorSalesforce",
    pattern: "MonitorSalesforce",
    defaults: new { controller = "MonitorSalesforce", action = "Index" });


app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapGet("/ping", () => Results.Ok());

app.Run();
